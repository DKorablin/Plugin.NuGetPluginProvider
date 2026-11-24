using System;

namespace Plugin.NuGetPluginProvider.Data
{
	/// <summary>Plugin package requested in the manifest</summary>
	public class PackageRequest
	{
		/// <summary>NuGet package identifier</summary>
		public String Id { get; set; }

		/// <summary>Pinned version. When null, the latest compatible version is installed once and then kept</summary>
		public String Version { get; set; }

		/// <summary>Optional folder for this package, overrides <see cref="NuGetManifest.TargetPath"/></summary>
		public String TargetPath { get; set; }

		/// <inheritdoc/>
		public override String ToString()
			=> this.Version == null ? this.Id : this.Id + " " + this.Version;
	}
}