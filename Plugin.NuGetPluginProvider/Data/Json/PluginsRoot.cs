using System;
using System.Runtime.Serialization;

namespace Plugin.NuGetPluginProvider.Data.Json
{
	[DataContract]
	internal class PluginsRoot
	{
		[DataMember]
		public Uri Host { get; set; }

		[DataMember]
		public PluginsItem[] Items { get; set; }
	}
}