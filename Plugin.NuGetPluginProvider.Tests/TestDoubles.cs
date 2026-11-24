using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using Plugin.NuGetPluginProvider.Domain;
using SAL.Flatbed;

namespace Plugin.NuGetPluginProvider.Tests
{
	internal sealed class FakeTrace : ITraceSource
	{
		public List<String> Messages { get; } = new List<String>();

		public void TraceEvent(TraceEventType eventType, Int32 id, String message)
			=> this.Messages.Add(eventType + ": " + message);

		public void TraceEvent(TraceEventType eventType, Int32 id, String format, params Object[] args)
			=> this.Messages.Add(eventType + ": " + String.Format(format, args));

		public void TraceData(TraceEventType eventType, Int32 id, Object data)
			=> this.Messages.Add(eventType + ": " + data);
	}

	/// <summary>Host environment that does not consider any assembly as already loaded</summary>
	internal sealed class TestHost : HostEnvironment
	{
		public TestHost(String flatbed = "1.2.12", String windows = "1.2.12", String framework = "net8.0-windows10.0.19041")
			: base(NuGetVersion.Parse(flatbed), windows == null ? null : NuGetVersion.Parse(windows), NuGetFramework.Parse(framework))
		{
		}

		public override Boolean IsAlreadyLoaded(String packageId, NuGetVersion minVersion)
			=> false;
	}

	/// <summary>Package stored in the fake feed</summary>
	internal sealed class FakePackage
	{
		public String Id { get; set; }
		public NuGetVersion Version { get; set; }
		public List<PackageDependencyGroup> Groups { get; } = new List<PackageDependencyGroup>();
		/// <summary>Path inside the package -> content</summary>
		public Dictionary<String, Byte[]> Files { get; } = new Dictionary<String, Byte[]>();

		public FakePackage(String id, String version)
		{
			this.Id = id;
			this.Version = NuGetVersion.Parse(version);
		}

		public FakePackage DependsOn(String framework, params (String Id, String Range)[] dependencies)
		{
			this.Groups.Add(new PackageDependencyGroup(NuGetFramework.Parse(framework),
				dependencies.Select(d => new PackageDependency(d.Id, VersionRange.Parse(d.Range)))));
			return this;
		}

		public FakePackage WithFile(String path, String content = null)
		{
			this.Files[path] = Encoding.UTF8.GetBytes(content ?? path);
			return this;
		}

		/// <summary>Build .nupkg content</summary>
		public Byte[] ToNupkg()
		{
			using(MemoryStream stream = new MemoryStream())
			{
				using(ZipArchive zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
				{
					StringBuilder groups = new StringBuilder();
					foreach(PackageDependencyGroup group in this.Groups)
					{
						groups.AppendFormat("<group targetFramework=\"{0}\">", group.TargetFramework.GetFrameworkString());
						foreach(PackageDependency dependency in group.Packages)
							groups.AppendFormat("<dependency id=\"{0}\" version=\"{1}\" />", dependency.Id, dependency.VersionRange.ToNormalizedString());
						groups.Append("</group>");
					}

					String nuspec = $"<?xml version=\"1.0\"?><package xmlns=\"http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd\"><metadata><id>{this.Id}</id><version>{this.Version}</version><authors>test</authors><description>test</description><dependencies>{groups}</dependencies></metadata></package>";
					using(StreamWriter writer = new StreamWriter(zip.CreateEntry(this.Id + ".nuspec").Open()))
						writer.Write(nuspec);

					foreach(KeyValuePair<String, Byte[]> file in this.Files)
						using(Stream entry = zip.CreateEntry(file.Key).Open())
							entry.Write(file.Value, 0, file.Value.Length);
				}
				return stream.ToArray();
			}
		}
	}

	/// <summary>In-memory feed</summary>
	internal sealed class FakeFeed : IPackageFeed
	{
		private readonly List<FakePackage> _packages = new List<FakePackage>();

		public String Name { get; }

		public List<String> SearchResult { get; } = new List<String>();

		public Int32 DownloadCount { get; private set; }

		public FakeFeed(String name = "fake")
			=> this.Name = name;

		public FakePackage Add(FakePackage package)
		{
			this._packages.Add(package);
			return package;
		}

		private IEnumerable<FakePackage> Find(String id)
			=> this._packages.Where(p => String.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

		public Task<IReadOnlyList<String>> SearchAsync(String searchTerm, Boolean includePrerelease, CancellationToken token)
			=> Task.FromResult<IReadOnlyList<String>>(this.SearchResult.ToArray());

		public Task<IReadOnlyList<FeedPackage>> GetMetadataAsync(String packageId, Boolean includePrerelease, CancellationToken token)
			=> Task.FromResult<IReadOnlyList<FeedPackage>>(this.Find(packageId)
				.Where(p => includePrerelease || !p.Version.IsPrerelease)
				.Select(p => new FeedPackage() { Id = p.Id, Version = p.Version, Title = p.Id, DependencyGroups = p.Groups.ToArray(), })
				.ToArray());

		public Task<IReadOnlyList<NuGetVersion>> GetAllVersionsAsync(String packageId, CancellationToken token)
			=> Task.FromResult<IReadOnlyList<NuGetVersion>>(this.Find(packageId).Select(p => p.Version).ToArray());

		public Task<IReadOnlyList<PackageDependencyGroup>> GetDependencyGroupsAsync(String packageId, NuGetVersion version, CancellationToken token)
			=> Task.FromResult<IReadOnlyList<PackageDependencyGroup>>(this.Find(packageId).FirstOrDefault(p => p.Version == version)?.Groups.ToArray());

		public async Task<Boolean> DownloadAsync(String packageId, NuGetVersion version, Stream destination, CancellationToken token)
		{
			FakePackage package = this.Find(packageId).FirstOrDefault(p => p.Version == version);
			if(package == null)
				return false;

			this.DownloadCount++;
			Byte[] nupkg = package.ToNupkg();
			await destination.WriteAsync(nupkg, 0, nupkg.Length, token);
			return true;
		}
	}

	/// <summary>Temporary folder removed after the test</summary>
	internal sealed class TempFolder : IDisposable
	{
		public String Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "NuGetProviderTests", Guid.NewGuid().ToString("N"));

		public TempFolder()
			=> Directory.CreateDirectory(this.Path);

		public String Combine(params String[] parts)
			=> System.IO.Path.Combine(new[] { this.Path }.Concat(parts).ToArray());

		public void Dispose()
		{
			try
			{
				Directory.Delete(this.Path, true);
			} catch(IOException)
			{
			} catch(UnauthorizedAccessException)
			{
			}
		}
	}
}