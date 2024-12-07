using System;
using Plugin.NuGetPluginProvider.NuGetClient.Info.Json;
using Plugin.NuGetPluginProvider.NuGetClient.Info.Xml;

namespace Plugin.NuGetPluginProvider.NuGetClient.Info
{
	internal class NuGetInfo
	{
		private readonly String _infoUrlArgs1;

		public NuGetInfo(String infoUrlArgs1)
		{
			if(!infoUrlArgs1.Contains("{0}"))
				throw new ArgumentException();

			this._infoUrlArgs1 = infoUrlArgs1;
		}

		public LatestVersionResult Download(String name)
		{
			JsonRequest request = new JsonRequest();
			request.InvokeRequest<InfoRootJson, InfoRootXml>(new Uri(String.Format(this._infoUrlArgs1, name)), out InfoRootJson j1, out InfoRootXml x1);
			if(j1 != null)
				return new LatestVersionResult(j1.GetLatestVersion());
			else if(x1 != null)
				return new LatestVersionResult(x1);
			else throw new ArgumentNullException();
		}
	}
}