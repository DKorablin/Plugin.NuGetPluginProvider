using System;
using System.Collections.Generic;
using System.IO;
using NuGet.Versioning;

namespace Plugin.NuGetPluginProvider.Data
{
	/// <summary>.nuget-install.json stored in each installed package folder</summary>
	/// <remarks>Written last during installation. A package folder without this file is treated as broken and installed again</remarks>
	public class InstallState
	{
		/// <summary>Install state file name</summary>
		public const String FileName = ".nuget-install.json";

		/// <summary>Plugin package identifier</summary>
		public String Id { get; set; }

		/// <summary>Installed plugin package version</summary>
		public String Version { get; set; }

		/// <summary>Target framework of the extracted lib folder</summary>
		public String Framework { get; set; }

		/// <summary>Feed the plugin package was downloaded from</summary>
		public String Feed { get; set; }

		/// <summary>Installation time (UTC)</summary>
		public DateTime InstalledOn { get; set; }

		/// <summary>Files extracted into the package folder from the plugin package and its dependencies</summary>
		public List<InstalledFile> Files { get; set; } = new List<InstalledFile>();

		/// <summary>Parsed <see cref="Version"/></summary>
		/// <returns>Version or null when the version is invalid</returns>
		public NuGetVersion GetVersion()
			=> NuGetVersion.TryParse(this.Version, out NuGetVersion result) ? result : null;

		/// <summary>Load the install state from the package folder</summary>
		/// <param name="packageFolder">Package folder</param>
		/// <returns>Install state or null when the file is missing or corrupted</returns>
		public static InstallState Load(String packageFolder)
		{
			String filePath = Path.Combine(packageFolder, FileName);
			if(!File.Exists(filePath))
				return null;

			try
			{
				InstallState result = Serializer.JavaScriptDeserialize<InstallState>(File.ReadAllText(filePath));
				return result == null || String.IsNullOrEmpty(result.Id) || result.GetVersion() == null
					? null
					: result;
			} catch(Exception exc) when(exc is IOException || exc is UnauthorizedAccessException || exc is Newtonsoft.Json.JsonException)
			{
				return null;
			}
		}

		/// <summary>Save the install state into the package folder</summary>
		/// <param name="packageFolder">Package folder</param>
		public void Save(String packageFolder)
			=> File.WriteAllText(Path.Combine(packageFolder, FileName), Serializer.JavaScriptSerialize(this));

		/// <summary>Delete the install state file from the package folder</summary>
		/// <param name="packageFolder">Package folder</param>
		public static void Delete(String packageFolder)
		{
			String filePath = Path.Combine(packageFolder, FileName);
			if(File.Exists(filePath))
				File.Delete(filePath);
		}

		/// <inheritdoc/>
		public override String ToString()
			=> this.Id + " " + this.Version;
	}

	/// <summary>File extracted from a package</summary>
	public class InstalledFile
	{
		/// <summary>Path relative to the package folder</summary>
		public String Path { get; set; }

		/// <summary>Package the file was extracted from</summary>
		public String PackageId { get; set; }

		/// <summary>Version of the package the file was extracted from</summary>
		public String PackageVersion { get; set; }
	}
}