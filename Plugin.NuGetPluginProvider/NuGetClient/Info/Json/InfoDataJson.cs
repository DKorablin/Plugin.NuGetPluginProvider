using System;
using System.Runtime.Serialization;

namespace Plugin.NuGetPluginProvider.NuGetClient.Info.Json
{
	[DataContract]
	internal class InfoDataJson
	{
		[DataMember(Name = "@id")]
		public String id { get; set; }

		[DataMember(Name = "@type")]
		public String type { get; set; }

		[DataMember]
		public String commitId { get; set; }

		[DataMember]
		public String commitTimeStamp { get; set; }

		[DataMember]
		public Int32 count { get; set; }

		[DataMember]
		public InfoVersionJson[] items { get; set; }
	}
}