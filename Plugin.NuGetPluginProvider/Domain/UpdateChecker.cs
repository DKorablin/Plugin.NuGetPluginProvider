using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Versioning;
using Plugin.NuGetPluginProvider.Data;
using SAL.Flatbed;

namespace Plugin.NuGetPluginProvider.Domain
{
	/// <summary>Available plugin version</summary>
	public class PackageUpdateInfo
	{
		/// <summary>Package identifier</summary>
		public String Id { get; set; }

		/// <summary>Installed version or null for a plugin that is not installed</summary>
		public NuGetVersion InstalledVersion { get; set; }

		/// <summary>Latest version compatible with the host</summary>
		public NuGetVersion AvailableVersion { get; set; }

		/// <summary>Package title</summary>
		public String Title { get; set; }

		/// <summary>Package description</summary>
		public String Description { get; set; }

		/// <summary>Feed that has the version</summary>
		public String Feed { get; set; }

		/// <inheritdoc/>
		public override String ToString()
			=> this.InstalledVersion == null
				? $"{this.Id} {this.AvailableVersion}"
				: $"{this.Id} {this.InstalledVersion} -> {this.AvailableVersion}";
	}

	/// <summary>Latest compatible version lookup result</summary>
	public class LatestVersionResult
	{
		/// <summary>Latest compatible version or null</summary>
		public FeedPackage Compatible { get; set; }

		/// <summary>Feed of the compatible version</summary>
		public IPackageFeed Feed { get; set; }

		/// <summary>The latest version of the package overall (may be incompatible)</summary>
		public FeedPackage Latest { get; set; }

		/// <summary>Compatibility of <see cref="Latest"/></summary>
		public CompatibilityResult LatestCompatibility { get; set; }
	}

	/// <summary>Looks for compatible versions of plugins on the feeds</summary>
	public class UpdateChecker
	{
		private readonly CompatibilityChecker _checker;
		private readonly IReadOnlyList<IPackageFeed> _feeds;
		private readonly ITraceSource _trace;

		/// <summary>Include pre-release versions</summary>
		public Boolean IncludePrerelease { get; set; }

		/// <summary>Create update checker</summary>
		/// <param name="checker">Compatibility checker</param>
		/// <param name="feeds">Feeds in the search order</param>
		/// <param name="trace">Trace source</param>
		public UpdateChecker(CompatibilityChecker checker, IReadOnlyList<IPackageFeed> feeds, ITraceSource trace)
		{
			this._checker = checker ?? throw new ArgumentNullException(nameof(checker));
			this._feeds = feeds ?? throw new ArgumentNullException(nameof(feeds));
			this._trace = trace ?? throw new ArgumentNullException(nameof(trace));
		}

		/// <summary>Find the latest version of the package compatible with the host</summary>
		/// <param name="packageId">Package identifier</param>
		/// <param name="token">Cancellation token</param>
		/// <returns>Lookup result</returns>
		public async Task<LatestVersionResult> FindLatestCompatibleAsync(String packageId, CancellationToken token)
		{
			LatestVersionResult result = new LatestVersionResult();
			foreach(IPackageFeed feed in this._feeds)
			{
				IReadOnlyList<FeedPackage> versions;
				try
				{
					versions = await feed.GetMetadataAsync(packageId, this.IncludePrerelease, token).ConfigureAwait(false);
				} catch(Exception exc) when(!(exc is OperationCanceledException))
				{
					this._trace.TraceEvent(TraceEventType.Warning, 2, "Feed {0} failed to return {1}: {2}", feed.Name, packageId, exc.Message);
					continue;
				}

				foreach(FeedPackage package in versions)
				{
					if(result.Latest == null || package.Version > result.Latest.Version)
					{
						result.Latest = package;
						result.LatestCompatibility = this._checker.Check(package.DependencyGroups);
					}

					if((result.Compatible == null || package.Version > result.Compatible.Version)
						&& this._checker.Check(package.DependencyGroups).IsCompatible)
					{
						result.Compatible = package;
						result.Feed = feed;
					}
				}
			}
			return result;
		}

		/// <summary>Find updates of the installed plugins</summary>
		/// <param name="installed">Installed plugins</param>
		/// <param name="token">Cancellation token</param>
		/// <returns>Plugins with newer compatible versions</returns>
		public async Task<IReadOnlyList<PackageUpdateInfo>> CheckForUpdatesAsync(IEnumerable<InstallState> installed, CancellationToken token)
		{
			List<PackageUpdateInfo> result = new List<PackageUpdateInfo>();
			foreach(InstallState state in installed)
			{
				NuGetVersion current = state.GetVersion();
				LatestVersionResult latest = await this.FindLatestCompatibleAsync(state.Id, token).ConfigureAwait(false);
				if(latest.Compatible != null && latest.Compatible.Version > current)
					result.Add(CreateInfo(latest, current));
				else if(latest.Latest != null && latest.Latest.Version > current && latest.LatestCompatibility?.RequiresNewerHost == true)
					this._trace.TraceEvent(TraceEventType.Verbose, 3, "{0} {1} is available but requires a newer host: {2}", state.Id, latest.Latest.Version, latest.LatestCompatibility.Reason);
			}
			return result;
		}

		/// <summary>Find compatible plugins on the feeds</summary>
		/// <param name="searchTerm">Search term. Package identifiers must contain this term</param>
		/// <param name="exclude">Package identifiers to skip (already requested or installed)</param>
		/// <param name="token">Cancellation token</param>
		/// <returns>Compatible plugins</returns>
		public async Task<IReadOnlyList<PackageUpdateInfo>> FindAvailablePluginsAsync(String searchTerm, ISet<String> exclude, CancellationToken token)
		{
			HashSet<String> ids = new HashSet<String>(StringComparer.OrdinalIgnoreCase);
			foreach(IPackageFeed feed in this._feeds)
				try
				{
					foreach(String id in await feed.SearchAsync(searchTerm, this.IncludePrerelease, token).ConfigureAwait(false))
						if(MatchesSearchTerm(id, searchTerm) && (exclude == null || !exclude.Contains(id)))
							ids.Add(id);
				} catch(Exception exc) when(!(exc is OperationCanceledException))
				{
					this._trace.TraceEvent(TraceEventType.Warning, 2, "Search on feed {0} failed: {1}", feed.Name, exc.Message);
				}

			List<PackageUpdateInfo> result = new List<PackageUpdateInfo>();
			foreach(String id in ids.OrderBy(i => i, StringComparer.OrdinalIgnoreCase))
			{
				LatestVersionResult latest = await this.FindLatestCompatibleAsync(id, token).ConfigureAwait(false);
				if(latest.Compatible != null)
					result.Add(CreateInfo(latest, null));
				else if(latest.Latest != null && latest.LatestCompatibility?.Status != PackageCompatibility.NotPlugin)
					this._trace.TraceEvent(TraceEventType.Verbose, 3, "Plugin {0} {1} is not compatible: {2}", id, latest.Latest.Version, latest.LatestCompatibility?.Reason);
			}
			return result;
		}

		/// <summary>Package identifier contains the search term</summary>
		/// <param name="packageId">Package identifier</param>
		/// <param name="searchTerm">Search term or null</param>
		/// <returns>The identifier matches</returns>
		public static Boolean MatchesSearchTerm(String packageId, String searchTerm)
			=> String.IsNullOrEmpty(searchTerm)
				|| packageId.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) >= 0;

		private static PackageUpdateInfo CreateInfo(LatestVersionResult latest, NuGetVersion installed)
			=> new PackageUpdateInfo()
			{
				Id = latest.Compatible.Id,
				InstalledVersion = installed,
				AvailableVersion = latest.Compatible.Version,
				Title = latest.Compatible.Title,
				Description = latest.Compatible.Description,
				Feed = latest.Feed?.Name,
			};
	}
}