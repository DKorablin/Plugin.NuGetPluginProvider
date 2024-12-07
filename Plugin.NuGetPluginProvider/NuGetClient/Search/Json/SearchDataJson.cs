using System;
using System.Runtime.Serialization;

namespace Plugin.NuGetPluginProvider.NuGetClient.Search.Json
{
	[DataContract]
	internal class SearchDataJson
	{
		[DataMember(Name = "@type")]
		public String type { get; set; }

		[DataMember]
		public String registration { get; set; }

		[DataMember]
		public String id { get; set; }

		[DataMember]
		public String version { get; set; }

		[DataMember]
		public String description { get; set; }

		[DataMember]
		public String summary { get; set; }

		[DataMember]
		public String title { get; set; }

		[DataMember]
		public String iconUrl { get; set; }

		[DataMember]
		public String licenseUrl { get; set; }

		[DataMember]
		public String projectUrl { get; set; }

		[DataMember]
		public String[] tags { get; set; }

		[DataMember]
		public String[] authors { get; set; }

		[DataMember]
		public Int32 totalDownloads { get; set; }

		[DataMember]
		public Boolean verified { get; set; }

		[DataMember]
		public SearchVersionJson[] versions { get; private set; }
	}
}