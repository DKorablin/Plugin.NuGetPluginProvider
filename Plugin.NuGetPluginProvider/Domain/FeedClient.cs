using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Packaging;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using Plugin.NuGetPluginProvider.Data;
using SAL.Flatbed;

namespace Plugin.NuGetPluginProvider.Domain
{
	/// <summary>NuGet V3 feed client based on NuGet.Protocol</summary>
	internal class FeedClient : IPackageFeed
	{
		private const Int32 SearchPageSize = 100;
		private const Int32 MaxSearchResults = 1000;
		private const String GitHubFeedHost = "nuget.pkg.github.com";

		private readonly SourceRepository _repository;
		private readonly SourceCacheContext _cache;
		private readonly ILogger _logger;
		private readonly ITraceSource _trace;
		private static readonly TimeSpan UnavailableTimeout = TimeSpan.FromMinutes(5);
		private readonly Object _stateLock = new Object();
		private DateTime _unavailableUntil = DateTime.MinValue;

		public FeedInfo Feed { get; }

		public String Name => this.Feed.ToString();

		/// <summary>The feed did not fail recently</summary>
		/// <remarks>After a failure (e.g. 401 Unauthorized or network error) the feed is skipped for <see cref="UnavailableTimeout"/></remarks>
		public Boolean IsAvailable
		{
			get
			{
				lock(this._stateLock)
					return DateTime.UtcNow >= this._unavailableUntil;
			}
		}

		public FeedClient(FeedInfo feed, SourceCacheContext cache, ILogger logger, ITraceSource trace)
		{
			this.Feed = feed ?? throw new ArgumentNullException(nameof(feed));
			this._cache = cache ?? throw new ArgumentNullException(nameof(cache));
			this._logger = logger ?? throw new ArgumentNullException(nameof(logger));
			this._trace = trace ?? throw new ArgumentNullException(nameof(trace));

			PackageSource source = new PackageSource(feed.Url, feed.Name ?? feed.Url);
			String password = feed.GetPassword();
			if(password != null)
				source.Credentials = new PackageSourceCredential(source.Name, feed.GetUserName() ?? "token", password, true, null);

			this._repository = Repository.Factory.GetCoreV3(source);
		}

		public async Task<IReadOnlyList<String>> SearchAsync(String searchTerm, Boolean includePrerelease, CancellationToken token)
		{
			List<String> result = new List<String>();
			if(!this.IsAvailable)
				return result;

			String searchError = null;
			try
			{
				PackageSearchResource search = await this._repository.GetResourceAsync<PackageSearchResource>(token).ConfigureAwait(false);
				if(search != null)
				{
					SearchFilter filter = new SearchFilter(includePrerelease);
					for(Int32 skip = 0; skip < MaxSearchResults; skip += SearchPageSize)
					{
						IPackageSearchMetadata[] page = (await search.SearchAsync(searchTerm ?? String.Empty, filter, skip, SearchPageSize, this._logger, token).ConfigureAwait(false)).ToArray();
						result.AddRange(page.Select(p => p.Identity.Id));
						if(page.Length < SearchPageSize)
							break;
					}
				}
			} catch(Exception exc) when(!(exc is OperationCanceledException))
			{
				searchError = exc.GetBaseException().Message;
				this._trace.TraceEvent(TraceEventType.Verbose, 2, "Search on feed {0} failed: {1}", this.Name, searchError);
			}

			// GitHub Packages search may ignore the search term or fail. Fall back to the GitHub REST API listing
			if(result.Count == 0 && this.IsGitHubFeed())
				result.AddRange(await this.GuardAsync<IReadOnlyList<String>>("package listing", () => this.ListGitHubPackagesAsync(token), Array.Empty<String>()).ConfigureAwait(false));
			else if(searchError != null)
				this.MarkUnavailable("search", searchError);

			return result.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
		}

		public Task<IReadOnlyList<FeedPackage>> GetMetadataAsync(String packageId, Boolean includePrerelease, CancellationToken token)
			=> this.GuardAsync<IReadOnlyList<FeedPackage>>("metadata of " + packageId, async () =>
			{
				PackageMetadataResource resource = await this._repository.GetResourceAsync<PackageMetadataResource>(token).ConfigureAwait(false);
				if(resource == null)
					return Array.Empty<FeedPackage>();

				IEnumerable<IPackageSearchMetadata> items = await resource.GetMetadataAsync(packageId, includePrerelease, false, this._cache, this._logger, token).ConfigureAwait(false);
				return items.Select(m => new FeedPackage()
				{
					Id = m.Identity.Id,
					Version = m.Identity.Version,
					Title = String.IsNullOrEmpty(m.Title) ? m.Identity.Id : m.Title,
					Description = m.Description,
					DependencyGroups = m.DependencySets?.ToArray() ?? Array.Empty<PackageDependencyGroup>(),
				}).ToArray();
			}, Array.Empty<FeedPackage>());

		public Task<IReadOnlyList<NuGetVersion>> GetAllVersionsAsync(String packageId, CancellationToken token)
			=> this.GuardAsync<IReadOnlyList<NuGetVersion>>("versions of " + packageId, async () =>
			{
				FindPackageByIdResource resource = await this._repository.GetResourceAsync<FindPackageByIdResource>(token).ConfigureAwait(false);
				IEnumerable<NuGetVersion> versions = await resource.GetAllVersionsAsync(packageId, this._cache, this._logger, token).ConfigureAwait(false);
				return versions?.ToArray() ?? Array.Empty<NuGetVersion>();
			}, Array.Empty<NuGetVersion>());

		public Task<IReadOnlyList<PackageDependencyGroup>> GetDependencyGroupsAsync(String packageId, NuGetVersion version, CancellationToken token)
			=> this.GuardAsync<IReadOnlyList<PackageDependencyGroup>>($"dependencies of {packageId} {version}", async () =>
			{
				FindPackageByIdResource resource = await this._repository.GetResourceAsync<FindPackageByIdResource>(token).ConfigureAwait(false);
				FindPackageByIdDependencyInfo info = await resource.GetDependencyInfoAsync(packageId, version, this._cache, this._logger, token).ConfigureAwait(false);
				return info?.DependencyGroups?.ToArray();
			}, null);

		public Task<Boolean> DownloadAsync(String packageId, NuGetVersion version, Stream destination, CancellationToken token)
			=> this.GuardAsync($"download of {packageId} {version}", async () =>
			{
				FindPackageByIdResource resource = await this._repository.GetResourceAsync<FindPackageByIdResource>(token).ConfigureAwait(false);
				return await resource.CopyNupkgToStreamAsync(packageId, version, destination, this._cache, this._logger, token).ConfigureAwait(false);
			}, false);

		/// <summary>Run the feed operation. After a failure the feed is skipped for a while</summary>
		/// <remarks>NuGet already retries failed requests several times, so repeating requests to a broken feed only slows everything down</remarks>
		private async Task<T> GuardAsync<T>(String operation, Func<Task<T>> action, T fallback)
		{
			if(!this.IsAvailable)
				return fallback;

			try
			{
				return await action().ConfigureAwait(false);
			} catch(Exception exc) when(!(exc is OperationCanceledException))
			{
				this.MarkUnavailable(operation, exc.GetBaseException().Message);
				return fallback;
			}
		}

		private void MarkUnavailable(String operation, String reason)
		{
			lock(this._stateLock)
			{
				if(DateTime.UtcNow < this._unavailableUntil)
					return;//Already reported
				this._unavailableUntil = DateTime.UtcNow + UnavailableTimeout;
			}
			this._trace.TraceEvent(TraceEventType.Warning, 2, "Feed {0} is skipped for {1} minutes, {2} failed: {3}", this.Name, UnavailableTimeout.TotalMinutes, operation, reason);
		}

		private Boolean IsGitHubFeed()
			=> Uri.TryCreate(this.Feed.Url, UriKind.Absolute, out Uri url)
				&& String.Equals(url.Host, GitHubFeedHost, StringComparison.OrdinalIgnoreCase);

		/// <summary>List NuGet packages of the GitHub feed owner using the GitHub REST API</summary>
		/// <param name="token">Cancellation token</param>
		/// <returns>Package names</returns>
		private async Task<IReadOnlyList<String>> ListGitHubPackagesAsync(CancellationToken token)
		{
			String owner = new Uri(this.Feed.Url).AbsolutePath.Trim('/').Split('/')[0];
			String password = this.Feed.GetPassword();
			if(String.IsNullOrEmpty(owner) || password == null)
				return Array.Empty<String>();

			List<String> result = new List<String>();
			using(HttpClient client = new HttpClient())
			{
				client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SAL-NuGetPluginProvider", "1.0"));
				client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
				client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", password);

				foreach(String scope in new[] { "users", "orgs" })
				{
					for(Int32 page = 1; page <= MaxSearchResults / SearchPageSize; page++)
					{
						Uri url = new Uri($"https://api.github.com/{scope}/{Uri.EscapeDataString(owner)}/packages?package_type=nuget&per_page={SearchPageSize}&page={page}");
						using(HttpResponseMessage response = await client.GetAsync(url, token).ConfigureAwait(false))
						{
							if(!response.IsSuccessStatusCode)
								break;

							JArray items = JArray.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
							result.AddRange(items.Select(i => (String)i["name"]).Where(n => !String.IsNullOrEmpty(n)));
							if(items.Count < SearchPageSize)
								break;
						}
					}

					if(result.Count > 0)
						break;
				}
			}
			return result;
		}
	}
}