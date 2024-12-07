using System;
using Plugin.NuGetPluginProvider.NuGetClient.Info.Json;
using Plugin.NuGetPluginProvider.NuGetClient.Info.Xml;

namespace Plugin.NuGetPluginProvider.NuGetClient.Info
{
	internal class LatestVersionResult
	{
		public String Id { get; set; }
		public String Title { get; set; }
		public String DownloadUrl { get; set; }
		public Version Version { get; set; }

		public LatestVersionResult(InfoVersionJson.CatalogEntry j)
		{
			this.Id = j.id;
			this.Title = j.title;
			this.DownloadUrl = j.packageContent;
			this.Version = j.GetVersion();
		}

		public LatestVersionResult(InfoRootXml x)
		{
			this.Id = x.id;
			this.Title = x.title;
			this.DownloadUrl = x.content.src;
			this.Version = x.properties.GetVersion();
		}
	}
}