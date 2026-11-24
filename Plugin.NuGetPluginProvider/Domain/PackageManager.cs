using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using Plugin.NuGetPluginProvider.Data;
using SAL.Flatbed;

namespace Plugin.NuGetPluginProvider.Domain
{
	/// <summary>Reconciles the manifest with installed packages, checks for updates and installs packages</summary>
	public sealed class PackageManager : IDisposable
	{
		private readonly ITraceSource _trace;
		private readonly SourceCacheContext _cache;
		private readonly IReadOnlyList<IPackageFeed> _feeds;
		private readonly PackageInstaller _installer;
		private readonly UpdateChecker _checker;
		private readonly Object _installLock = new Object();

		/// <summary>Provider manifest</summary>
		public NuGetManifest Manifest { get; }

		/// <summary>Host environment</summary>
		public HostEnvironment Host { get; }

		/// <summary>Root folder for staging and on-demand downloads (%LOCALAPPDATA%\SAL\NuGet)</summary>
		public String DataRoot { get; }

		/// <summary>On-demand assembly resolver</summary>
		public OnDemandResolver Resolver { get; }

		/// <summary>Create package manager</summary>
		/// <param name="manifest">Provider manifest</param>
		/// <param name="host">Host environment</param>
		/// <param name="dataRoot">Root folder for staging and on-demand downloads</param>
		/// <param name="trace">Trace source</param>
		public PackageManager(NuGetManifest manifest, HostEnvironment host, String dataRoot, ITraceSource trace)
		{
			this.Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
			this.Host = host ?? throw new ArgumentNullException(nameof(host));
			this.DataRoot = dataRoot ?? throw new ArgumentNullException(nameof(dataRoot));
			this._trace = trace ?? throw new ArgumentNullException(nameof(trace));

			LimitNuGetRetries();
			this._cache = new SourceCacheContext();
			TraceLogger logger = new TraceLogger(trace);
			this._feeds = manifest.GetEnabledFeeds().Select(f => (IPackageFeed)new FeedClient(f, this._cache, logger, trace)).ToArray();
			foreach(FeedInfo feed in manifest.GetEnabledFeeds().Where(f => f.IsPlainTextSecret))
				this._trace.TraceEvent(TraceEventType.Warning, 5, "Feed {0} stores the password as plain text in {1}. Use an environment variable reference like %SAL_NUGET_TOKEN% instead", feed, manifest.FilePath);

			this._installer = new PackageInstaller(host, this._feeds, Path.Combine(dataRoot, "staging"), trace);
			this._checker = new UpdateChecker(new CompatibilityChecker(host), this._feeds, trace) { IncludePrerelease = manifest.IncludePrerelease, };
			this.Resolver = new OnDemandResolver(host, this._feeds, this._installer, Path.Combine(dataRoot, "resolved"), TimeSpan.FromSeconds(manifest.ResolveTimeoutSeconds), new AssemblyIndex(), trace)
			{
				AllowUnsigned = manifest.ResolveUnsignedOnDemand,
			};
		}

		/// <summary>NuGet retries each failed request 6 times with 1 second delay. That blocks the host start when a feed is offline</summary>
		/// <remarks>Only process environment variables are changed and only when they are not set by the user</remarks>
		private static void LimitNuGetRetries()
		{
			SetIfMissing("NUGET_ENHANCED_MAX_NETWORK_TRY_COUNT", "2");
			SetIfMissing("NUGET_ENHANCED_NETWORK_RETRY_DELAY_MILLISECONDS", "500");
		}

		private static void SetIfMissing(String name, String value)
		{
			if(String.IsNullOrEmpty(Environment.GetEnvironmentVariable(name)))
				Environment.SetEnvironmentVariable(name, value);
		}

		/// <summary>Remove leftovers from the previous run and index assemblies downloaded on demand</summary>
		public void Cleanup()
		{
			foreach(String root in this.Manifest.GetTargetRoots())
				PackageInstaller.CleanupRenamedFiles(root);
			PackageInstaller.CleanupRenamedFiles(Path.Combine(this.DataRoot, "resolved"));
			PackageInstaller.DeleteDirectory(Path.Combine(this.DataRoot, "staging"));

			String resolved = Path.Combine(this.DataRoot, "resolved");
			if(Directory.Exists(resolved))
				foreach(String package in Directory.GetDirectories(resolved))
					foreach(String version in Directory.GetDirectories(package))
						if(InstallState.Load(version) != null)
							this.Resolver.Index.AddFolder(version);
		}

		/// <summary>Installed plugin packages in all target folders</summary>
		/// <returns>Install states</returns>
		public IReadOnlyList<InstallState> GetInstalled()
		{
			List<InstallState> result = new List<InstallState>();
			foreach(String root in this.Manifest.GetTargetRoots())
				if(Directory.Exists(root))
					foreach(String folder in Directory.GetDirectories(root))
					{
						InstallState state = InstallState.Load(folder);
						if(state != null && String.Equals(Path.GetFileName(folder), state.Id, StringComparison.OrdinalIgnoreCase))
							result.Add(state);
					}
			return result;
		}

		/// <summary>Install, update and remove packages so the target folders match the manifest</summary>
		/// <param name="isFolderLoaded">Checks that plugins from the folder are already loaded by other providers</param>
		public void Reconcile(Func<String, Boolean> isFolderLoaded)
		{
			foreach(PackageRequest request in this.Manifest.Packages)
				try
				{
					this.ReconcilePackage(request, isFolderLoaded);
				} catch(InvalidOperationException exc)
				{//Expected failures: package or dependency not found, no lib for the host framework
					this._trace.TraceEvent(TraceEventType.Error, 5, "{0} is not installed: {1}", request, exc.Message);
				} catch(Exception exc)
				{
					exc.Data["Package"] = request.ToString();
					this._trace.TraceData(TraceEventType.Error, 5, exc);
				}

			foreach(String root in this.Manifest.GetTargetRoots())
				if(Directory.Exists(root))
					foreach(String folder in Directory.GetDirectories(root))
					{
						InstallState state = InstallState.Load(folder);
						if(state == null || this.Manifest.FindPackage(state.Id) != null)
							continue;

						ApplyResult result = PackageInstaller.Uninstall(folder);
						this._trace.TraceEvent(TraceEventType.Information, 5, "Removed {0} from {1}{2}", state, folder,
							result.RestartRequired || isFolderLoaded(folder) ? ". Restart required" : String.Empty);
					}
		}

		private void ReconcilePackage(PackageRequest request, Func<String, Boolean> isFolderLoaded)
		{
			String folder = this.Manifest.GetPackageFolder(request);
			InstallState state = InstallState.Load(folder);
			NuGetVersion pinned = null;
			if(request.Version != null && !NuGetVersion.TryParse(request.Version, out pinned))
				throw new FormatException($"Invalid version '{request.Version}' of package {request.Id} in {this.Manifest.FilePath}");

			if(state != null && (pinned == null || pinned == state.GetVersion()))
				return;//Up to date

			NuGetVersion target = this.SelectVersion(request.Id, pinned);
			if(target == null)
				return;

			this.InstallInto(request.Id, target, folder, isFolderLoaded);
		}

		/// <summary>Select the version to install</summary>
		/// <param name="packageId">Package identifier</param>
		/// <param name="pinned">Requested version or null for the latest compatible</param>
		/// <returns>Version or null when no compatible version is found</returns>
		private NuGetVersion SelectVersion(String packageId, NuGetVersion pinned)
		{
			LatestVersionResult latest = Wait(token => this._checker.FindLatestCompatibleAsync(packageId, token));
			if(pinned == null)
			{
				if(latest.Compatible == null)
					this._trace.TraceEvent(TraceEventType.Warning, 5, "No version of {0} compatible with the host ({1}) is found{2}",
						packageId, this.Host, latest.Latest == null ? ". The package is not found on any feed" : ": " + latest.LatestCompatibility?.Reason);
				return latest.Compatible?.Version;
			}

			// The pinned version must be compatible too. The compatibility is checked with the full version list returned by the feeds
			FeedPackage package = this.FindVersion(packageId, pinned);
			if(package == null)
			{
				this._trace.TraceEvent(TraceEventType.Warning, 5, "{0} {1} is not found on any feed", packageId, pinned);
				return null;
			}

			CompatibilityResult compatibility = new CompatibilityChecker(this.Host).Check(package.DependencyGroups);
			if(!compatibility.IsCompatible)
			{
				this._trace.TraceEvent(TraceEventType.Warning, 5, "{0} {1} is not installed: {2}{3}", packageId, pinned, compatibility.Reason,
					latest.Compatible == null ? String.Empty : $". The latest compatible version is {latest.Compatible.Version}");
				return null;
			}
			return pinned;
		}

		private FeedPackage FindVersion(String packageId, NuGetVersion version)
		{
			foreach(IPackageFeed feed in this._feeds)
			{
				IReadOnlyList<FeedPackage> versions;
				try
				{
					versions = Wait(token => feed.GetMetadataAsync(packageId, true, token));
				} catch(Exception exc) when(!(exc is OperationCanceledException))
				{
					this._trace.TraceEvent(TraceEventType.Warning, 5, "Feed {0} failed to return {1}: {2}", feed.Name, packageId, exc.Message);
					continue;
				}

				FeedPackage result = versions.FirstOrDefault(v => v.Version == version);
				if(result != null)
					return result;
			}
			return null;
		}

		private void InstallInto(String packageId, NuGetVersion version, String folder, Func<String, Boolean> isFolderLoaded)
		{
			lock(this._installLock)
			{
				Boolean wasLoaded = isFolderLoaded(folder);
				Tuple<ApplyResult, InstallState> result = Wait(token => this._installer.InstallAsync(packageId, version, folder, token));
				if(result.Item1.RestartRequired || wasLoaded)
					this._trace.TraceEvent(TraceEventType.Information, 5, "{0} {1} is installed into {2}. Restart required to load the new version", packageId, version, folder);
			}
		}

		/// <summary>Find newer compatible versions of the installed plugins</summary>
		/// <returns>Available updates</returns>
		public IReadOnlyList<PackageUpdateInfo> CheckForUpdates()
			=> Wait(token => this._checker.CheckForUpdatesAsync(this.GetInstalled(), token));

		/// <summary>Find compatible plugins that are not requested in the manifest</summary>
		/// <returns>Available plugins</returns>
		public IReadOnlyList<PackageUpdateInfo> FindAvailablePlugins()
		{
			HashSet<String> exclude = new HashSet<String>(this.Manifest.Packages.Select(p => p.Id), StringComparer.OrdinalIgnoreCase);
			foreach(InstallState state in this.GetInstalled())
				exclude.Add(state.Id);
			return Wait(token => this._checker.FindAvailablePluginsAsync(this.Manifest.SearchTerm, exclude, token));
		}

		/// <summary>Check for updates and new plugins and write them to the log</summary>
		public void LogUpdatesAndNewPlugins()
		{
			foreach(PackageUpdateInfo update in this.CheckForUpdates())
				this._trace.TraceEvent(TraceEventType.Information, 6, "Update available: {0} {1} -> {2}. Set \"Version\": \"{2}\" for this package in {3} and restart",
					update.Id, update.InstalledVersion, update.AvailableVersion, this.Manifest.FilePath);

			foreach(PackageUpdateInfo plugin in this.FindAvailablePlugins())
				this._trace.TraceEvent(TraceEventType.Information, 6, "New plugin available: {0} {1} - {2}. Add {{ \"Id\": \"{0}\" }} to \"Packages\" in {3} and restart",
					plugin.Id, plugin.AvailableVersion, plugin.Title, this.Manifest.FilePath);
		}

		/// <summary>Add or update the package in the manifest and install it</summary>
		/// <param name="packageId">Package identifier</param>
		/// <param name="version">Version to install or null for the latest compatible</param>
		/// <param name="isFolderLoaded">Checks that plugins from the folder are already loaded by other providers</param>
		/// <returns>The package is installed</returns>
		public Boolean Install(String packageId, String version, Func<String, Boolean> isFolderLoaded)
		{
			if(String.IsNullOrEmpty(packageId))
				throw new ArgumentNullException(nameof(packageId));

			NuGetVersion pinned = null;
			if(version != null && !NuGetVersion.TryParse(version, out pinned))
				throw new ArgumentException($"Invalid version '{version}'", nameof(version));

			NuGetVersion target = this.SelectVersion(packageId, pinned);
			if(target == null)
				return false;

			PackageRequest request = this.Manifest.FindPackage(packageId);
			if(request == null)
			{
				request = new PackageRequest() { Id = packageId, };
				this.Manifest.Packages.Add(request);
			}
			request.Version = version == null ? null : target.ToNormalizedString();
			this.Manifest.Save();

			this.InstallInto(request.Id, target, this.Manifest.GetPackageFolder(request), isFolderLoaded);
			return true;
		}

		/// <summary>Run the asynchronous operation synchronously</summary>
		/// <remarks>Runs on the thread pool, so it can't deadlock on the UI synchronization context</remarks>
		private static T Wait<T>(Func<CancellationToken, Task<T>> operation)
			=> Task.Run(() => operation(CancellationToken.None)).GetAwaiter().GetResult();

		/// <inheritdoc/>
		public void Dispose()
		{
			this._cache.Dispose();
		}
	}
}