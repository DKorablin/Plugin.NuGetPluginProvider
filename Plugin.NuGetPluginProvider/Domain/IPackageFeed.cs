using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Packaging;
using NuGet.Versioning;

namespace Plugin.NuGetPluginProvider.Domain
{
	/// <summary>Package found on a feed</summary>
	public class FeedPackage
	{
		/// <summary>Package identifier</summary>
		public String Id { get; set; }

		/// <summary>Package version</summary>
		public NuGetVersion Version { get; set; }

		/// <summary>Package title</summary>
		public String Title { get; set; }

		/// <summary>Package description</summary>
		public String Description { get; set; }

		/// <summary>Dependency groups of the package version</summary>
		public IReadOnlyList<PackageDependencyGroup> DependencyGroups { get; set; } = Array.Empty<PackageDependencyGroup>();
	}

	/// <summary>Operations on a NuGet feed used by the provider</summary>
	public interface IPackageFeed
	{
		/// <summary>Feed name used in logs</summary>
		String Name { get; }

		/// <summary>Search for package identifiers on the feed</summary>
		/// <param name="searchTerm">Search term</param>
		/// <param name="includePrerelease">Include pre-release versions</param>
		/// <param name="token">Cancellation token</param>
		/// <returns>Distinct package identifiers</returns>
		Task<IReadOnlyList<String>> SearchAsync(String searchTerm, Boolean includePrerelease, CancellationToken token);

		/// <summary>Get all listed versions of the package with their dependencies</summary>
		/// <param name="packageId">Package identifier</param>
		/// <param name="includePrerelease">Include pre-release versions</param>
		/// <param name="token">Cancellation token</param>
		/// <returns>Package versions or an empty list when the package is not found</returns>
		Task<IReadOnlyList<FeedPackage>> GetMetadataAsync(String packageId, Boolean includePrerelease, CancellationToken token);

		/// <summary>Get all versions of the package</summary>
		/// <param name="packageId">Package identifier</param>
		/// <param name="token">Cancellation token</param>
		/// <returns>Versions or an empty list when the package is not found</returns>
		Task<IReadOnlyList<NuGetVersion>> GetAllVersionsAsync(String packageId, CancellationToken token);

		/// <summary>Get dependency groups of the package version</summary>
		/// <param name="packageId">Package identifier</param>
		/// <param name="version">Package version</param>
		/// <param name="token">Cancellation token</param>
		/// <returns>Dependency groups or null when the package version is not found</returns>
		Task<IReadOnlyList<PackageDependencyGroup>> GetDependencyGroupsAsync(String packageId, NuGetVersion version, CancellationToken token);

		/// <summary>Download the package</summary>
		/// <param name="packageId">Package identifier</param>
		/// <param name="version">Package version</param>
		/// <param name="destination">Stream to write .nupkg to</param>
		/// <param name="token">Cancellation token</param>
		/// <returns>The package was found and downloaded</returns>
		Task<Boolean> DownloadAsync(String packageId, NuGetVersion version, Stream destination, CancellationToken token);
	}
}