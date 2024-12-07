using System;
using System.Linq;
using Plugin.NuGetPluginProvider.NuGetClient.Search.Json;
using Plugin.NuGetPluginProvider.NuGetClient.Search.Xml;

namespace Plugin.NuGetPluginProvider.NuGetClient.Search
{
	internal class SearchResult
	{
		internal class SearchDataResult
		{
			public String id { get; private set; }
			public String title { get; private set; }
			public String description { get; private set; }
			public Version version { get; private set; }

			public SearchDataResult(SearchDataJson j)
			{
				this.id = j.id;
				this.title = j.title;
				this.description = j.description;
				this.version = new Version(j.version);
			}

			public SearchDataResult(SearchRootXml.SearchEntryXml x)
			{
				this.id = x.id;
				this.title = x.title;
				this.description = x.properties.Description;
				this.version = new Version(x.properties.Version);
			}
		}

		public Int32 total { get; private set; }

		public SearchDataResult[] data { get; private set; }

		public SearchResult(SearchRootJson j)
		{
			this.total = j.totalHits;
			this.data = j.data.Select(p => new SearchDataResult(p)).ToArray();
		}

		public SearchResult(SearchRootXml x)
		{
			this.total = x.entry.Length;
			this.data = x.entry.Select(p => new SearchDataResult(p)).ToArray();
		}
	}
}