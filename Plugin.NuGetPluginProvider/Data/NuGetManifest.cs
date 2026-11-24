using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace Plugin.NuGetPluginProvider.Data
{
	/// <summary>Plugins.NuGet.json: feeds, requested packages and provider options</summary>
	/// <remarks>The settings provider is not available while plugin providers are loading, so the provider keeps its own configuration file</remarks>
	public class NuGetManifest
	{
		/// <summary>Manifest file name</summary>
		public const String FileName = "Plugins.NuGet.json";

		/// <summary>nuget.org V3 service index</summary>
		public const String NuGetOrgUrl = "https://api.nuget.org/v3/index.json";

		/// <summary>GitHub Packages feed where the SAL plugins are published</summary>
		public const String GitHubFeedUrl = "https://nuget.pkg.github.com/DKorablin/index.json";

		/// <summary>NuGet feeds. Searched in the listed order</summary>
		public List<FeedInfo> Feeds { get; set; } = new List<FeedInfo>();

		/// <summary>Folder where plugin packages are installed (one subfolder per package). When null, the manifest folder is used</summary>
		public String TargetPath { get; set; }

		/// <summary>Search term used to find new plugins on the feeds. Results are additionally filtered by package identifier containing this term</summary>
		public String SearchTerm { get; set; } = "AlphaOmega.SAL.Plugin";

		/// <summary>Include pre-release package versions</summary>
		public Boolean IncludePrerelease { get; set; }

		/// <summary>Check for updates and new plugins in background after all plugins are loaded</summary>
		public Boolean CheckForUpdates { get; set; } = true;

		/// <summary>Download assemblies from NuGet when no plugin provider can resolve them</summary>
		public Boolean ResolveOnDemand { get; set; } = true;

		/// <summary>Also download assemblies without strong name on demand</summary>
		/// <remarks>The package is found by the assembly name only, an unsigned assembly with the same name can be published to a public feed by anyone</remarks>
		public Boolean ResolveUnsignedOnDemand { get; set; }

		/// <summary>Maximum time spent on one on-demand assembly resolution</summary>
		public Int32 ResolveTimeoutSeconds { get; set; } = 30;

		/// <summary>Requested plugin packages</summary>
		public List<PackageRequest> Packages { get; set; } = new List<PackageRequest>();

		/// <summary>Path to the manifest file</summary>
		[JsonIgnore]
		public String FilePath { get; private set; }

		/// <summary>Create the default manifest</summary>
		/// <param name="filePath">Path where the manifest is stored</param>
		/// <returns>Manifest with nuget.org and a disabled GitHub Packages feed</returns>
		public static NuGetManifest CreateDefault(String filePath)
			=> new NuGetManifest()
			{
				FilePath = filePath,
				Feeds = new List<FeedInfo>()
				{
					new FeedInfo() { Name = "nuget.org", Url = NuGetOrgUrl, },
					new FeedInfo() { Name = "github_DKorablin", Url = GitHubFeedUrl, UserName = "%SAL_NUGET_USER%", Password = "%SAL_NUGET_TOKEN%", Enabled = false, },
				},
			};

		/// <summary>Load the manifest or create the default one when the file does not exist</summary>
		/// <param name="filePath">Path to the manifest file</param>
		/// <param name="created">The file did not exist and the default manifest was returned</param>
		/// <returns>Loaded manifest</returns>
		public static NuGetManifest Load(String filePath, out Boolean created)
		{
			if(String.IsNullOrEmpty(filePath))
				throw new ArgumentNullException(nameof(filePath));

			created = !File.Exists(filePath);
			NuGetManifest result = created
				? CreateDefault(filePath)
				: Parse(File.ReadAllText(filePath), filePath);
			return result;
		}

		/// <summary>Parse the manifest from JSON</summary>
		/// <param name="json">Manifest JSON</param>
		/// <param name="filePath">Path to the manifest file</param>
		/// <returns>Parsed manifest</returns>
		public static NuGetManifest Parse(String json, String filePath)
		{
			NuGetManifest result = Serializer.JavaScriptDeserialize<NuGetManifest>(json) ?? new NuGetManifest();
			result.FilePath = filePath;
			result.Feeds = (result.Feeds ?? new List<FeedInfo>()).Where(f => f != null && !String.IsNullOrEmpty(f.Url)).ToList();
			result.Packages = (result.Packages ?? new List<PackageRequest>()).Where(p => p != null && !String.IsNullOrEmpty(p.Id)).ToList();
			if(result.ResolveTimeoutSeconds <= 0)
				result.ResolveTimeoutSeconds = 30;
			return result;
		}

		/// <summary>Save the manifest to <see cref="FilePath"/></summary>
		public void Save()
		{
			String directory = Path.GetDirectoryName(this.FilePath);
			if(!String.IsNullOrEmpty(directory))
				Directory.CreateDirectory(directory);
			File.WriteAllText(this.FilePath, Serializer.JavaScriptSerialize(this));
		}

		/// <summary>Enabled feeds</summary>
		/// <returns>Feeds in the configured order</returns>
		public IEnumerable<FeedInfo> GetEnabledFeeds()
			=> this.Feeds.Where(f => f.Enabled);

		/// <summary>Find the requested package by identifier</summary>
		/// <param name="id">Package identifier</param>
		/// <returns>Requested package or null</returns>
		public PackageRequest FindPackage(String id)
			=> this.Packages.FirstOrDefault(p => String.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

		/// <summary>Get the root folder where packages are installed</summary>
		/// <param name="request">Package request or null for the default root</param>
		/// <returns>Full path to the root folder</returns>
		public String GetTargetRoot(PackageRequest request = null)
		{
			String path = request?.TargetPath ?? this.TargetPath;
			String baseDirectory = Path.GetDirectoryName(Path.GetFullPath(this.FilePath));
			if(String.IsNullOrEmpty(path))
				return baseDirectory;

			path = Environment.ExpandEnvironmentVariables(path);
			return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(baseDirectory, path));
		}

		/// <summary>Get the folder where the package is installed</summary>
		/// <param name="request">Package request</param>
		/// <returns>Full path to the package folder</returns>
		public String GetPackageFolder(PackageRequest request)
			=> Path.Combine(this.GetTargetRoot(request), request.Id);

		/// <summary>All distinct root folders where packages may be installed</summary>
		/// <returns>Full paths</returns>
		public IEnumerable<String> GetTargetRoots()
			=> new[] { this.GetTargetRoot() }
				.Concat(this.Packages.Select(p => this.GetTargetRoot(p)))
				.Distinct(StringComparer.OrdinalIgnoreCase);
	}
}