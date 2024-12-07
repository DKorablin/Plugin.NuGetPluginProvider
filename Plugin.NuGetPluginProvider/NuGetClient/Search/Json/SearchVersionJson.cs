using System;
using System.Runtime.Serialization;

namespace Plugin.NuGetPluginProvider.NuGetClient.Search.Json
{
	[DataContract]
	internal class SearchVersionJson
	{
		[DataMember(Name = "@id")]
		public String id { get; private set; }

		[DataMember]
		public String downloads { get; private set; }

		[DataMember]
		public Version version { get; private set; }
	}
}