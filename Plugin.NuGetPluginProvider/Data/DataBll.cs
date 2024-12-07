using System;
using System.IO;
using Plugin.NuGetPluginProvider.Data.Json;

namespace Plugin.NuGetPluginProvider.Data
{
	internal class DataBll
	{
		private String FilePath { get; }

		private PluginsRoot Plugins { get; }

		public DataBll(String filePath)
		{
			if(String.IsNullOrEmpty(filePath))
				throw new ArgumentNullException(nameof(filePath));

			this.FilePath = filePath;

			this.Plugins = File.Exists(filePath)
				? Serializer.JavaScriptDeserialize<PluginsRoot>(File.ReadAllText(this.FilePath))
				: new PluginsRoot() { Host = new Uri("https://www.nuget.org/api/v2/package/{Name}"), Items = new PluginsItem[] { }, };
		}
	}
}