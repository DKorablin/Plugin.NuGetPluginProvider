using System;
using System.Collections.Generic;
using Plugin.NuGetPluginProvider.NuGetClient.Search.Json;
using Plugin.NuGetPluginProvider.NuGetClient.Search.Xml;

namespace Plugin.NuGetPluginProvider.NuGetClient.Search
{
	internal class NuGetSearcher : JsonRequest
	{
		private readonly String _searchUrl;
		private readonly Boolean _noSkip;

		public NuGetSearcher(String searchUrl)
		{
			this._searchUrl = searchUrl;
			this._noSkip = !searchUrl.Contains("{Skip}");
		}

		public IEnumerable<SearchResult.SearchDataResult> Search(Int32 skip = 0)
		{
			while(true)
			{
				SearchResult result = this.SearchTopN(skip);
				if(result == null || result.data == null || result.data.Length == 0)
					yield break;

				foreach(var item in result.data)
					yield return item;

				skip += result.data.Length;
				if(skip > result.total)
					yield break;
			}
		}

		public SearchResult SearchTopN(Int32 skip)
		{
			if(skip > 0 && this._noSkip)
				throw new ArgumentException(nameof(skip));

			Uri absoluteUri = new Uri(this._searchUrl.Replace("{Skip}", skip.ToString()));
			base.InvokeRequest<SearchRootJson, SearchRootXml>(absoluteUri, out SearchRootJson j, out SearchRootXml x);
			if(x != null)
				return new SearchResult(x);
			else if(j != null)
				return new SearchResult(j);
			else throw new NotImplementedException();
		}
	}
}