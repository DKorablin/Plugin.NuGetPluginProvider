using System;
using System.Runtime.Serialization;

namespace Plugin.NuGetPluginProvider.Data.Json
{
	[DataContract]
	internal class PluginsItem
	{
		[DataMember]
		public String Name { get; set; }
	}
}