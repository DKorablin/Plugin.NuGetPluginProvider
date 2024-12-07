using System;
using System.Reflection;

namespace Plugin.NuGetPluginProvider
{
	internal static class Constant
	{
		public const String PackageSearchExtension = "*.nupkg";

		public static class NuGet
		{
			private const String NuGetDlServerUrlArgs2 = "https://www.nuget.org/api/v2/package/{AssemblyName}/{Version}";
			private const String NuGetSearchServerArgs1 = "https://api-v2v3search-0.nuget.org/query?q=Tags:\"sal\"&prerelease=false&skip={Skip}";

			public static String CreateDownloadUrl(AssemblyName asmName)
				=> NuGetDlServerUrlArgs2.Replace("{Assembly}", asmName.Name).Replace("{Version}", asmName.Version.ToString());

			public static String CreateSearchUrl(Int32 skip = 0)
				=> NuGetSearchServerArgs1.Replace("{Skip}", skip.ToString());
		}
	}
}