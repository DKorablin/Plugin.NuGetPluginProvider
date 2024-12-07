using System;
using System.Runtime.Serialization;

namespace Plugin.NuGetPluginProvider.NuGetClient.Info.Json
{
	[DataContract]
	internal class InfoVersionJson
	{
		[DataContract]
		internal class CatalogEntry
		{
			[DataMember(Name = "@id")]
			public String id { get; set; }

			[DataMember]
			public String title { get; set; }

			[DataMember]
			public String packageContent { get; set; }

			[DataMember]
			public String version { get; set; }

			public Version GetVersion()
				=> new Version(version);
		}

		[DataMember(Name = "@id")]
		public String id { get; set; }

		[DataMember(Name = "@type")]
		public String type { get; set; }

		[DataMember]
		public CatalogEntry catalogEntry { get; set; }
	}
}