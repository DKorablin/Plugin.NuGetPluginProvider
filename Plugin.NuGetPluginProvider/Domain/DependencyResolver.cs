using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Plugin.NuGetPluginProvider.Domain
{
	/// <summary>Package selected for installation</summary>
	public class ResolvedPackage
	{
		/// <summary>Package identifier</summary>
		public String Id { get; }

		/// <summary>Selected version</summary>
		public NuGetVersion Version { get; }

		/// <summary>Feed that has the selected version</summary>
		public IPackageFeed Feed { get; }

		/// <summary>Create resolved package</summary>
		/// <param name="id">Package identifier</param>
		/// <param name="version">Selected version</param>
		/// <param name="feed">Feed that has the version</param>
		public ResolvedPackage(String id, NuGetVersion version, IPackageFeed feed)
		{
			this.Id = id;
			this.Version = version;
			this.Feed = feed;
		}

		/// <inheritdoc/>
		public override String ToString()
			=> this.Id + " " + this.Version;
	}

	/// <summary>Builds the dependency closure of a package for the host framework</summary>
	/// <remarks>
	/// For each dependency the lowest version satisfying all ranges is selected (NuGet "lowest applicable" rule).
	/// SAL host packages and packages already loaded in the process are skipped
	/// </remarks>
	public class DependencyResolver
	{
		private const Int32 MaxIterations = 20;

		private readonly HostEnvironment _host;
		private readonly IReadOnlyList<IPackageFeed> _feeds;
		private readonly Dictionary<String, List<Tuple<NuGetVersion, IPackageFeed>>> _versions = new Dictionary<String, List<Tuple<NuGetVersion, IPackageFeed>>>(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<String, IReadOnlyList<PackageDependencyGroup>> _dependencies = new Dictionary<String, IReadOnlyList<PackageDependencyGroup>>(StringComparer.OrdinalIgnoreCase);

		/// <summary>Create resolver</summary>
		/// <param name="host">Host environment</param>
		/// <param name="feeds">Feeds in the search order</param>
		public DependencyResolver(HostEnvironment host, IReadOnlyList<IPackageFeed> feeds)
		{
			this._host = host ?? throw new ArgumentNullException(nameof(host));
			this._feeds = feeds ?? throw new ArgumentNullException(nameof(feeds));
		}

		/// <summary>Resolve the package and all its dependencies</summary>
		/// <param name="packageId">Root package identifier</param>
		/// <param name="version">Root package version</param>
		/// <param name="token">Cancellation token</param>
		/// <exception cref="InvalidOperationException">The package or one of its dependencies can't be found</exception>
		/// <returns>Root package first, then dependencies</returns>
		public async Task<IReadOnlyList<ResolvedPackage>> ResolveAsync(String packageId, NuGetVersion version, CancellationToken token)
		{
			IPackageFeed rootFeed = await this.FindFeedAsync(packageId, version, token).ConfigureAwait(false)
				?? throw new InvalidOperationException($"Package {packageId} {version} is not found on any available feed");

			Dictionary<String, ResolvedPackage> chosen = new Dictionary<String, ResolvedPackage>(StringComparer.OrdinalIgnoreCase)
			{
				{ packageId, new ResolvedPackage(packageId, version, rootFeed) },
			};

			for(Int32 iteration = 0; iteration < MaxIterations; iteration++)
			{
				Dictionary<String, List<VersionRange>> ranges = await this.CollectRangesAsync(packageId, chosen, token).ConfigureAwait(false);

				Boolean changed = false;
				foreach(KeyValuePair<String, List<VersionRange>> range in ranges)
				{
					ResolvedPackage selected = await this.SelectLowestAsync(range.Key, range.Value, token).ConfigureAwait(false)
						?? throw new InvalidOperationException($"Dependency {range.Key} {String.Join(" ", range.Value.Select(r => r.PrettyPrint()))} of {packageId} {version} is not found on any available feed");

					if(!chosen.TryGetValue(range.Key, out ResolvedPackage current) || current.Version != selected.Version)
					{
						chosen[range.Key] = selected;
						changed = true;
					}
				}

				// Remove packages that are no longer referenced
				foreach(String id in chosen.Keys.Where(k => !k.Equals(packageId, StringComparison.OrdinalIgnoreCase) && !ranges.ContainsKey(k)).ToArray())
				{
					chosen.Remove(id);
					changed = true;
				}

				if(!changed)
					return new[] { chosen[packageId] }
						.Concat(chosen.Values.Where(p => !p.Id.Equals(packageId, StringComparison.OrdinalIgnoreCase)).OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase))
						.ToArray();
			}

			throw new InvalidOperationException($"Dependencies of {packageId} {version} can't be resolved in {MaxIterations} iterations");
		}

		/// <summary>Walk the graph of currently chosen versions and collect version ranges for each dependency</summary>
		private async Task<Dictionary<String, List<VersionRange>>> CollectRangesAsync(String rootId, Dictionary<String, ResolvedPackage> chosen, CancellationToken token)
		{
			Dictionary<String, List<VersionRange>> result = new Dictionary<String, List<VersionRange>>(StringComparer.OrdinalIgnoreCase);
			HashSet<String> visited = new HashSet<String>(StringComparer.OrdinalIgnoreCase);
			Queue<ResolvedPackage> queue = new Queue<ResolvedPackage>();
			queue.Enqueue(chosen[rootId]);

			while(queue.Count > 0)
			{
				ResolvedPackage package = queue.Dequeue();
				if(!visited.Add(package.Id))
					continue;

				IReadOnlyList<PackageDependencyGroup> groups = await this.GetDependencyGroupsAsync(package, token).ConfigureAwait(false);
				PackageDependencyGroup group = CompatibilityChecker.GetNearestGroup(this._host.Framework, groups);
				if(group == null)
					continue;

				foreach(PackageDependency dependency in group.Packages)
				{
					if(dependency.Id.Equals(rootId, StringComparison.OrdinalIgnoreCase)
						|| HostEnvironment.IsHostPackage(dependency.Id)
						|| this._host.IsAlreadyLoaded(dependency.Id, dependency.VersionRange?.MinVersion))
						continue;

					if(!result.TryGetValue(dependency.Id, out List<VersionRange> list))
					{
						list = new List<VersionRange>();
						result.Add(dependency.Id, list);
					}
					list.Add(dependency.VersionRange ?? VersionRange.All);

					if(chosen.TryGetValue(dependency.Id, out ResolvedPackage next))
						queue.Enqueue(next);
				}
			}

			return result;
		}

		private async Task<ResolvedPackage> SelectLowestAsync(String packageId, List<VersionRange> ranges, CancellationToken token)
		{
			List<Tuple<NuGetVersion, IPackageFeed>> versions = await this.GetVersionsAsync(packageId, token).ConfigureAwait(false);
			Boolean allowPrerelease = ranges.Any(r => r.MinVersion?.IsPrerelease == true);

			Tuple<NuGetVersion, IPackageFeed> selected = versions
				.Where(v => allowPrerelease || !v.Item1.IsPrerelease)
				.Where(v => ranges.All(r => r.Satisfies(v.Item1)))
				.OrderBy(v => v.Item1)
				.FirstOrDefault();

			return selected == null ? null : new ResolvedPackage(packageId, selected.Item1, selected.Item2);
		}

		private async Task<List<Tuple<NuGetVersion, IPackageFeed>>> GetVersionsAsync(String packageId, CancellationToken token)
		{
			if(!this._versions.TryGetValue(packageId, out List<Tuple<NuGetVersion, IPackageFeed>> result))
			{
				result = new List<Tuple<NuGetVersion, IPackageFeed>>();
				foreach(IPackageFeed feed in this._feeds)
					foreach(NuGetVersion version in await feed.GetAllVersionsAsync(packageId, token).ConfigureAwait(false))
						if(!result.Exists(v => v.Item1 == version))//The first feed that has the version wins
							result.Add(Tuple.Create(version, feed));
				this._versions.Add(packageId, result);
			}
			return result;
		}

		private async Task<IPackageFeed> FindFeedAsync(String packageId, NuGetVersion version, CancellationToken token)
		{
			List<Tuple<NuGetVersion, IPackageFeed>> versions = await this.GetVersionsAsync(packageId, token).ConfigureAwait(false);
			return versions.FirstOrDefault(v => v.Item1 == version)?.Item2;
		}

		private async Task<IReadOnlyList<PackageDependencyGroup>> GetDependencyGroupsAsync(ResolvedPackage package, CancellationToken token)
		{
			String key = package.Id + "/" + package.Version.ToNormalizedString();
			if(!this._dependencies.TryGetValue(key, out IReadOnlyList<PackageDependencyGroup> result))
			{
				result = await package.Feed.GetDependencyGroupsAsync(package.Id, package.Version, token).ConfigureAwait(false)
					?? throw new InvalidOperationException($"Package {package} is not found on feed {package.Feed.Name}");
				this._dependencies.Add(key, result);
			}
			return result;
		}
	}
}