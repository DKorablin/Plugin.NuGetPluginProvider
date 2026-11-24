using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Versioning;
using Plugin.NuGetPluginProvider.Data;
using SAL.Flatbed;

namespace Plugin.NuGetPluginProvider.Domain
{
	/// <summary>Result of applying staged files into the package folder</summary>
	public class ApplyResult
	{
		/// <summary>Number of copied (new or changed) files</summary>
		public Int32 CopiedFiles { get; internal set; }

		/// <summary>Number of locked files renamed to <see cref="PackageInstaller.RenamedExtension"/> instead of deleting or overwriting</summary>
		public Int32 RenamedFiles { get; internal set; }

		/// <summary>Some files were locked, the new version will be used after restart</summary>
		public Boolean RestartRequired => this.RenamedFiles > 0;
	}

	/// <summary>Downloads packages, extracts lib files for the host framework and applies them into the package folder</summary>
	public class PackageInstaller
	{
		/// <summary>Extension added to locked files that can't be deleted or overwritten. They are removed on the next start</summary>
		public const String RenamedExtension = ".dl_";

		private static readonly String[] ExtractExtensions = new[] { ".dll", ".pdb", ".xml", ".json", ".config" };
		private static readonly FrameworkReducer Reducer = new FrameworkReducer();

		private readonly HostEnvironment _host;
		private readonly IReadOnlyList<IPackageFeed> _feeds;
		private readonly String _stagingRoot;
		private readonly ITraceSource _trace;

		/// <summary>Create installer</summary>
		/// <param name="host">Host environment</param>
		/// <param name="feeds">Feeds in the search order</param>
		/// <param name="stagingRoot">Folder for temporary extraction. Must not be scanned by plugin providers</param>
		/// <param name="trace">Trace source</param>
		public PackageInstaller(HostEnvironment host, IReadOnlyList<IPackageFeed> feeds, String stagingRoot, ITraceSource trace)
		{
			this._host = host ?? throw new ArgumentNullException(nameof(host));
			this._feeds = feeds ?? throw new ArgumentNullException(nameof(feeds));
			this._stagingRoot = stagingRoot ?? throw new ArgumentNullException(nameof(stagingRoot));
			this._trace = trace ?? throw new ArgumentNullException(nameof(trace));
		}

		/// <summary>Install the package with all dependencies into the package folder</summary>
		/// <param name="packageId">Package identifier</param>
		/// <param name="version">Package version</param>
		/// <param name="packageFolder">Destination folder (one folder per package)</param>
		/// <param name="token">Cancellation token</param>
		/// <returns>Apply result and the new install state</returns>
		public async Task<Tuple<ApplyResult, InstallState>> InstallAsync(String packageId, NuGetVersion version, String packageFolder, CancellationToken token)
		{
			IReadOnlyList<ResolvedPackage> packages = await new DependencyResolver(this._host, this._feeds).ResolveAsync(packageId, version, token).ConfigureAwait(false);

			String staging = Path.Combine(this._stagingRoot, packageId + "." + version.ToNormalizedString());
			DeleteDirectory(staging);
			Directory.CreateDirectory(staging);
			try
			{
				InstallState state = new InstallState()
				{
					Id = packageId,
					Version = version.ToNormalizedString(),
					Feed = packages[0].Feed.Name,
					InstalledOn = DateTime.UtcNow,
				};

				foreach(ResolvedPackage package in packages)
				{
					using(MemoryStream nupkg = new MemoryStream())
					{
						if(!await package.Feed.DownloadAsync(package.Id, package.Version, nupkg, token).ConfigureAwait(false))
							throw new InvalidOperationException($"Package {package} can't be downloaded from {package.Feed.Name}");

						nupkg.Position = 0;
						String framework = this.Extract(package, nupkg, staging, state.Files);
						if(package == packages[0])
							state.Framework = framework
								?? throw new InvalidOperationException($"Package {package} has no lib folder compatible with {this._host.Framework.GetShortFolderName()}");
					}
				}

				ApplyResult result = Apply(staging, packageFolder, state);
				this._trace.TraceEvent(TraceEventType.Information, 1, "Installed {0} {1} ({2} package(s), {3} file(s)) into {4}",
					packageId, state.Version, packages.Count, state.Files.Count, packageFolder);
				return Tuple.Create(result, state);
			} finally
			{
				DeleteDirectory(staging);
			}
		}

		/// <summary>Extract lib files nearest to the host framework into the staging folder</summary>
		/// <returns>Short folder name of the selected framework or null when the package has no lib files for the host</returns>
		private String Extract(ResolvedPackage package, Stream nupkg, String staging, List<InstalledFile> files)
		{
			using(PackageArchiveReader reader = new PackageArchiveReader(nupkg, true))
			{
				FrameworkSpecificGroup[] groups = reader.GetLibItems().ToArray();
				NuGetFramework nearest = Reducer.GetNearest(this._host.Framework, groups.Select(g => g.TargetFramework));
				if(reader.GetFiles("runtimes").Any())
					this._trace.TraceEvent(TraceEventType.Warning, 1, "Package {0} contains native runtime assets that are not extracted. The plugin may not work", package);

				if(nearest == null)
				{
					if(groups.Length > 0)
						this._trace.TraceEvent(TraceEventType.Warning, 1, "Package {0} has no lib folder compatible with {1}", package, this._host.Framework.GetShortFolderName());
					return null;
				}

				FrameworkSpecificGroup group = groups.First(g => g.TargetFramework.Equals(nearest));
				foreach(String item in group.Items)
				{
					String relativePath = GetRelativeLibPath(item);
					if(relativePath == null || !ExtractExtensions.Contains(Path.GetExtension(relativePath), StringComparer.OrdinalIgnoreCase))
						continue;

					if(files.Exists(f => String.Equals(f.Path, relativePath, StringComparison.OrdinalIgnoreCase)))
					{
						this._trace.TraceEvent(TraceEventType.Verbose, 1, "File {0} from {1} is skipped, it is already provided by another package", relativePath, package);
						continue;
					}

					String destination = Path.Combine(staging, relativePath);
					Directory.CreateDirectory(Path.GetDirectoryName(destination));
					using(Stream source = reader.GetStream(item))
					using(FileStream target = new FileStream(destination, FileMode.Create, FileAccess.Write))
						source.CopyTo(target);

					files.Add(new InstalledFile() { Path = relativePath, PackageId = package.Id, PackageVersion = package.Version.ToNormalizedString(), });
				}
				return nearest.GetShortFolderName();
			}
		}

		/// <summary>Convert lib/{tfm}/path/file.dll to path/file.dll</summary>
		/// <param name="item">Path inside the package</param>
		/// <returns>Relative path or null for placeholder files</returns>
		public static String GetRelativeLibPath(String item)
		{
			String[] parts = item.Replace('\\', '/').Split('/');
			if(parts.Length < 3 || parts[parts.Length - 1] == "_._")
				return null;
			return Path.Combine(parts.Skip(2).ToArray());
		}

		/// <summary>Apply staged files into the package folder</summary>
		/// <remarks>
		/// The install state is removed first and written last, so an interrupted installation is detected and repeated.
		/// Locked files can't be deleted or overwritten but can be renamed, so they are renamed and removed on the next start
		/// </remarks>
		/// <param name="stagingFolder">Folder with the new files</param>
		/// <param name="packageFolder">Destination package folder</param>
		/// <param name="state">New install state with the list of staged files</param>
		/// <returns>Apply result</returns>
		public static ApplyResult Apply(String stagingFolder, String packageFolder, InstallState state)
		{
			ApplyResult result = new ApplyResult();
			Directory.CreateDirectory(packageFolder);

			InstallState previous = InstallState.Load(packageFolder);
			InstallState.Delete(packageFolder);

			HashSet<String> newFiles = new HashSet<String>(state.Files.Select(f => f.Path), StringComparer.OrdinalIgnoreCase);
			if(previous != null)
				foreach(InstalledFile file in previous.Files.Where(f => !newFiles.Contains(f.Path)))
					if(RemoveFile(Path.Combine(packageFolder, file.Path)))
						result.RenamedFiles++;

			foreach(InstalledFile file in state.Files)
			{
				String source = Path.Combine(stagingFolder, file.Path);
				String destination = Path.Combine(packageFolder, file.Path);
				if(File.Exists(destination) && AreEqual(source, destination))
					continue;

				Directory.CreateDirectory(Path.GetDirectoryName(destination));
				if(File.Exists(destination) && RemoveFile(destination))
					result.RenamedFiles++;
				File.Copy(source, destination, true);
				result.CopiedFiles++;
			}

			state.Save(packageFolder);
			return result;
		}

		/// <summary>Remove files of the installed package</summary>
		/// <param name="packageFolder">Package folder with install state</param>
		/// <returns>Result where <see cref="ApplyResult.RenamedFiles"/> is the number of locked files</returns>
		public static ApplyResult Uninstall(String packageFolder)
		{
			ApplyResult result = new ApplyResult();
			InstallState state = InstallState.Load(packageFolder);
			if(state == null)
				return result;

			InstallState.Delete(packageFolder);
			foreach(InstalledFile file in state.Files)
				if(RemoveFile(Path.Combine(packageFolder, file.Path)))
					result.RenamedFiles++;

			DeleteEmptyDirectories(packageFolder);
			return result;
		}

		/// <summary>Delete files renamed during the previous update and empty folders</summary>
		/// <param name="root">Root folder with package folders</param>
		public static void CleanupRenamedFiles(String root)
		{
			if(!Directory.Exists(root))
				return;

			foreach(String directory in Directory.GetDirectories(root))
			{
				foreach(String file in Directory.EnumerateFiles(directory, "*" + RenamedExtension, SearchOption.AllDirectories).ToArray())
					try
					{
						File.Delete(file);
					} catch(IOException)
					{//Still locked
					} catch(UnauthorizedAccessException)
					{//Still locked
					}

				if(InstallState.Load(directory) == null)
					DeleteEmptyDirectories(directory);
			}
		}

		/// <summary>Delete the file or rename it when it is locked</summary>
		/// <param name="filePath">File to remove</param>
		/// <returns>The file was locked and renamed</returns>
		public static Boolean RemoveFile(String filePath)
		{
			if(!File.Exists(filePath))
				return false;

			try
			{
				File.Delete(filePath);
				return false;
			} catch(Exception exc) when(exc is IOException || exc is UnauthorizedAccessException)
			{
				Int32 index = 0;
				String renamed = filePath + RenamedExtension;
				while(File.Exists(renamed) && !TryDelete(renamed))
					renamed = filePath + "." + (++index) + RenamedExtension;

				File.Move(filePath, renamed);
				return true;
			}
		}

		private static Boolean TryDelete(String filePath)
		{
			try
			{
				File.Delete(filePath);
				return true;
			} catch(Exception exc) when(exc is IOException || exc is UnauthorizedAccessException)
			{
				return false;
			}
		}

		private static Boolean AreEqual(String file1, String file2)
		{
			if(new FileInfo(file1).Length != new FileInfo(file2).Length)
				return false;

			using(SHA256 sha = SHA256.Create())
			{
				Byte[] hash1, hash2;
				using(FileStream stream = new FileStream(file1, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
					hash1 = sha.ComputeHash(stream);
				using(FileStream stream = new FileStream(file2, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
					hash2 = sha.ComputeHash(stream);
				return hash1.SequenceEqual(hash2);
			}
		}

		private static void DeleteEmptyDirectories(String directory)
		{
			if(!Directory.Exists(directory))
				return;

			foreach(String child in Directory.GetDirectories(directory))
				DeleteEmptyDirectories(child);

			if(!Directory.EnumerateFileSystemEntries(directory).Any())
				try
				{
					Directory.Delete(directory);
				} catch(IOException)
				{//Used by another process
				}
		}

		internal static void DeleteDirectory(String directory)
		{
			if(Directory.Exists(directory))
				try
				{
					Directory.Delete(directory, true);
				} catch(IOException)
				{//Will be removed on the next run
				} catch(UnauthorizedAccessException)
				{//Will be removed on the next run
				}
		}
	}
}