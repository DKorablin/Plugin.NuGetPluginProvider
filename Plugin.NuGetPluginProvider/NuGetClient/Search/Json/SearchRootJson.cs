using System;
using System.Runtime.Serialization;

namespace Plugin.NuGetPluginProvider.NuGetClient.Search.Json
{
	[DataContract]
	internal class SearchRootJson
	{
		[DataMember]
		public Int32 totalHits { get; set; }

		[DataMember]
		public DateTime lastReopen { get; set; }

		[DataMember]
		public String index { get; set; }

		[DataMember]
		public SearchDataJson[] data { get; set; }
	}
}