using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace Plugin.NuGetPluginProvider.NuGetClient.Info.Json
{
	[DataContract]
	internal class InfoRootJson
	{
		[DataMember(Name = "@id")]
		public String id { get; set; }

		[DataMember]
		public Int32 count { get; set; }

		[DataMember]
		public String commitId { get; set; }

		public String commitTimeStamp { get; set; }

		[DataMember]
		public InfoDataJson[] items { get; set; }

		public InfoVersionJson.CatalogEntry GetLatestVersion()
		{
			List<InfoVersionJson.CatalogEntry> versions = new List<InfoVersionJson.CatalogEntry>();
			foreach(var a in this.items)
				foreach(var b in a.items)
					versions.Add(b.catalogEntry);

			return versions.OrderByDescending(p => p.GetVersion()).First();
		}
	}
}