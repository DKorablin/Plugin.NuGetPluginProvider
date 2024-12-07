using System;

namespace Plugin.NuGetPluginProvider.NuGetClient
{
	internal class NuGetPackageEntry
	{
		public Int64 EntryIndex { get; }
		public String FileName { get; }
		public Byte[] Payload { get; }

		public NuGetPackageEntry(Int64 entryIndex, String fileName, Byte[] payload)
		{
			this.EntryIndex = entryIndex;
			this.FileName = fileName;
			this.Payload = payload;
		}
	}
}