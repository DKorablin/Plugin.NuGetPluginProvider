using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Versioning;
using SAL.Flatbed;

namespace Plugin.NuGetPluginProvider.Domain
{
	/// <summary>Downloads an assembly from NuGet when no plugin provider can resolve it</summary>
	/// <remarks>The package identifier is assumed to be equal to the assembly name</remarks>
	public class OnDemandResolver
	{
		private const Int32 MaxProbes = 5;
		private static readonly FrameworkReducer Reducer = new FrameworkReducer();

		private static readonly String[] SkipNames = new[] { "mscorlib", "netstandard", "Microsoft.GeneratedCode", HostEnvironment.FlatbedPackageId, HostEnvironment.WindowsPackageId, typeof(OnDemandResolver).Assembly.GetName().Name, };
		private static readonly String[] SkipPrefixes = new[] { "System.Private.", "NuGet.", };
		private static readonly String[] SkipSuffixes = new[] { ".resources", ".XmlSerializers", };

		/// <summary>1 while an on-demand resolution is running. Nested requests (from any thread) are not resolved to prevent recursion</summary>
		private static Int32 _active;

		private readonly HostEnvironment _host;
		private readonly IReadOnlyList<IPackageFeed> _feeds;
		private readonly PackageInstaller _installer;
		private readonly String _resolvedRoot;
		private readonly TimeSpan _timeout;
		private readonly ITraceSource _trace;
		private readonly ConcurrentDictionary<String, Byte> _failed = new ConcurrentDictionary<String, Byte>(StringComparer.OrdinalIgnoreCase);

		/// <summary>Downloaded assemblies</summary>
		public AssemblyIndex Index { get; }

		/// <summary>Allow downloading assemblies without strong name</summary>
		/// <remarks>
		/// The package is found by assembly name only. For strong-named assemblies the public key token proves the publisher,
		/// an unsigned assembly with the same name can be published to a public feed by anyone
		/// </remarks>
		public Boolean AllowUnsigned { get; set; }

		/// <summary>Create on-demand resolver</summary>
		/// <param name="host">Host environment</param>
		/// <param name="feeds">Feeds in the search order</param>
		/// <param name="installer">Installer used to download the package with dependencies</param>
		/// <param name="resolvedRoot">Folder for downloaded packages. Must not be scanned by plugin providers</param>
		/// <param name="timeout">Maximum time of one resolution</param>
		/// <param name="index">Index of downloaded assemblies</param>
		/// <param name="trace">Trace source</param>
		public OnDemandResolver(HostEnvironment host, IReadOnlyList<IPackageFeed> feeds, PackageInstaller installer, String resolvedRoot, TimeSpan timeout, AssemblyIndex index, ITraceSource trace)
		{
			this._host = host ?? throw new ArgumentNullException(nameof(host));
			this._feeds = feeds ?? throw new ArgumentNullException(nameof(feeds));
			this._installer = installer ?? throw new ArgumentNullException(nameof(installer));
			this._resolvedRoot = resolvedRoot ?? throw new ArgumentNullException(nameof(resolvedRoot));
			this._timeout = timeout;
			this.Index = index ?? throw new ArgumentNullException(nameof(index));
			this._trace = trace ?? throw new ArgumentNullException(nameof(trace));
		}

		/// <summary>The assembly is never downloaded on demand</summary>
		/// <param name="name">Requested assembly</param>
		/// <returns>Resource, framework, SAL or NuGet client assembly</returns>
		public static Boolean ShouldSkip(AssemblyName name)
		{
			if(name == null || String.IsNullOrEmpty(name.Name))
				return true;
			if(!String.IsNullOrEmpty(name.CultureName) && !String.Equals(name.CultureName, "neutral", StringComparison.OrdinalIgnoreCase))
				return true;
			return SkipNames.Any(n => String.Equals(name.Name, n, StringComparison.OrdinalIgnoreCase))
				|| SkipPrefixes.Any(p => name.Name.StartsWith(p, StringComparison.OrdinalIgnoreCase))
				|| SkipSuffixes.Any(s => name.Name.EndsWith(s, StringComparison.OrdinalIgnoreCase));
		}

		/// <summary>The assembly failed to resolve earlier in this session</summary>
		/// <param name="name">Requested assembly</param>
		/// <returns>Resolution was already attempted and failed</returns>
		public Boolean HasFailed(AssemblyName name)
			=> this._failed.ContainsKey(name.FullName);

		/// <summary>Download the package with the assembly and return the path to the assembly</summary>
		/// <param name="name">Requested assembly</param>
		/// <returns>Path to the downloaded assembly or null</returns>
		public String TryResolve(AssemblyName name)
		{
			if(ShouldSkip(name) || this.HasFailed(name))
				return null;

			if(!this.AllowUnsigned && IsUnsigned(name))
			{
				this._failed.TryAdd(name.FullName, 0);
				this._trace.TraceEvent(TraceEventType.Verbose, 4, "Assembly {0} has no public key token and is not downloaded on demand. Set ResolveUnsignedOnDemand to allow it", name.FullName);
				return null;
			}

			if(Interlocked.CompareExchange(ref _active, 1, 0) != 0)
				return null;

			try
			{
				using(CancellationTokenSource cancel = new CancellationTokenSource())
				{
					Task<String> task = Task.Run(() => this.ResolveAsync(name, cancel.Token));
					if(!task.Wait(this._timeout))
					{
						cancel.Cancel();
						this._failed.TryAdd(name.FullName, 0);
						this._trace.TraceEvent(TraceEventType.Warning, 4, "On-demand resolution of {0} timed out after {1}", name.FullName, this._timeout);
						return null;
					}

					if(task.Result == null)
						this._failed.TryAdd(name.FullName, 0);
					return task.Result;
				}
			} catch(AggregateException exc)
			{
				this._failed.TryAdd(name.FullName, 0);
				Exception error = exc.GetBaseException();
				error.Data["Assembly"] = name.FullName;
				this._trace.TraceData(TraceEventType.Error, 4, error);
				return null;
			} finally
			{
				Interlocked.Exchange(ref _active, 0);
			}
		}

		private async Task<String> ResolveAsync(AssemblyName name, CancellationToken token)
		{
			String packageId = name.Name;
			List<Tuple<NuGetVersion, IPackageFeed>> versions = new List<Tuple<NuGetVersion, IPackageFeed>>();
			foreach(IPackageFeed feed in this._feeds)
				try
				{
					foreach(NuGetVersion version in await feed.GetAllVersionsAsync(packageId, token).ConfigureAwait(false))
						if(!versions.Exists(v => v.Item1 == version))
							versions.Add(Tuple.Create(version, feed));
				} catch(Exception exc) when(!(exc is OperationCanceledException))
				{
					this._trace.TraceEvent(TraceEventType.Verbose, 4, "Feed {0} failed to list {1}: {2}", feed.Name, packageId, exc.Message);
				}

			foreach(NuGetVersion version in SelectCandidates(name.Version, versions.Select(v => v.Item1)).Take(MaxProbes))
			{
				IPackageFeed feed = versions.First(v => v.Item1 == version).Item2;
				AssemblyName probed = await this.ProbeAssemblyAsync(feed, packageId, version, name.Name, token).ConfigureAwait(false);
				if(probed == null || !IsMatch(name, probed))
					continue;

				String folder = Path.Combine(this._resolvedRoot, packageId, version.ToNormalizedString());
				if(InstallStateExists(folder))
					this.Index.AddFolder(folder);
				else
				{
					this._trace.TraceEvent(TraceEventType.Information, 4, "Downloading {0} {1} to resolve {2}", packageId, version, name.FullName);
					await this._installer.InstallAsync(packageId, version, folder, token).ConfigureAwait(false);
					this.Index.AddFolder(folder);
				}

				return this.Index.FindExact(name) ?? this.Index.FindCompatible(name);
			}

			this._trace.TraceEvent(TraceEventType.Verbose, 4, "No NuGet package found for assembly {0}", name.FullName);
			return null;
		}

		/// <summary>Select package versions that may contain the requested assembly version</summary>
		/// <remarks>
		/// Package versions often differ from assembly versions (e.g. System.Memory 4.5.5 contains assembly 4.0.1.2),
		/// so the highest stable package of the same major is probed first: assembly versions only grow with package versions
		/// and any equal or higher assembly version of the same major is accepted
		/// </remarks>
		/// <param name="requested">Requested assembly version or null</param>
		/// <param name="versions">Available package versions</param>
		/// <returns>Stable versions of the same major from the highest to the lowest</returns>
		public static IEnumerable<NuGetVersion> SelectCandidates(Version requested, IEnumerable<NuGetVersion> versions)
			=> versions
				.Where(v => !v.IsPrerelease && (requested == null || v.Major == requested.Major))
				.Distinct()
				.OrderByDescending(v => v);

		/// <summary>The requested assembly is not strong-named</summary>
		/// <param name="name">Requested assembly</param>
		/// <returns>The public key token is missing or empty</returns>
		public static Boolean IsUnsigned(AssemblyName name)
		{
			Byte[] token = name.GetPublicKeyToken();
			return token == null || token.Length == 0;
		}

		/// <summary>The assembly found in a package can be used instead of the requested one</summary>
		/// <param name="requested">Requested assembly</param>
		/// <param name="candidate">Assembly from the package</param>
		/// <returns>Same name and public key token, same major and equal or higher version</returns>
		public static Boolean IsMatch(AssemblyName requested, AssemblyName candidate)
			=> String.Equals(requested.Name, candidate.Name, StringComparison.OrdinalIgnoreCase)
				&& (requested.GetPublicKeyToken() ?? Array.Empty<Byte>()).SequenceEqual(candidate.GetPublicKeyToken() ?? Array.Empty<Byte>())
				&& AssemblyIndex.IsCompatibleVersion(requested.Version, candidate.Version);

		private async Task<AssemblyName> ProbeAssemblyAsync(IPackageFeed feed, String packageId, NuGetVersion version, String assemblyName, CancellationToken token)
		{
			using(MemoryStream nupkg = new MemoryStream())
			{
				if(!await feed.DownloadAsync(packageId, version, nupkg, token).ConfigureAwait(false))
					return null;

				nupkg.Position = 0;
				using(PackageArchiveReader reader = new PackageArchiveReader(nupkg, true))
				{
					FrameworkSpecificGroup[] groups = reader.GetLibItems().ToArray();
					NuGetFramework nearest = Reducer.GetNearest(this._host.Framework, groups.Select(g => g.TargetFramework));
					if(nearest == null)
						return null;

					String item = groups.First(g => g.TargetFramework.Equals(nearest)).Items
						.FirstOrDefault(i => String.Equals(Path.GetFileName(i), assemblyName + ".dll", StringComparison.OrdinalIgnoreCase));
					if(item == null)
						return null;

					String tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".dll");
					try
					{
						using(Stream source = reader.GetStream(item))
						using(FileStream target = new FileStream(tempFile, FileMode.Create, FileAccess.Write))
							await source.CopyToAsync(target).ConfigureAwait(false);
						return AssemblyName.GetAssemblyName(tempFile);
					} catch(BadImageFormatException)
					{
						return null;
					} finally
					{
						File.Delete(tempFile);
					}
				}
			}
		}

		private static Boolean InstallStateExists(String folder)
			=> Data.InstallState.Load(folder) != null;
	}
}