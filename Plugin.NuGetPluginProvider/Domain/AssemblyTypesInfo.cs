using System;

namespace Plugin.NuGetPluginProvider.Domain
{
	[Serializable]
	internal class AssemblyTypesInfo
	{
		public String PackagePath { get; }

		public Int64 FileIndex { get; }

		public String[] Types { get; }

		public AssemblyTypesInfo(String packagePath, Int64 fileIndex, String[] types)
		{
			this.PackagePath = packagePath;
			this.FileIndex = fileIndex;
			this.Types = types;
		}
	}
}