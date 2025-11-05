using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using Plugin.FilePluginProvider;

namespace Plugin.NuGetPluginProvider.NuGetClient
{
	internal class NuGetDownloader
	{
		public Byte[] DownloadAssemblyFromNuGet(AssemblyName asmName)
		{
			Byte[] payload = this.Download(asmName);
			if(payload != null)
			{
				Version currentRuntimeVersion = new Version(Assembly.GetExecutingAssembly().ImageRuntimeVersion.Trim('v'));
				Dictionary<Version, Byte[]> assemblies = new Dictionary<Version, Byte[]>();
				foreach(NuGetPackageEntry entry in NuGetDownloader.Extract(payload))
				{

					Assembly reflection = Assembly.ReflectionOnlyLoad(entry.Payload);
					Version runtimeVersion = new Version(reflection.ImageRuntimeVersion.Trim('v'));
					if(reflection.FullName == asmName.FullName
						&& runtimeVersion <= currentRuntimeVersion)
						assemblies.Add(runtimeVersion, entry.Payload);
				}

				if(assemblies.Count > 0)//We need to find the version closest to the current runtime, as we may come across unsupported (.NET Core) or very old versions.
					return assemblies.OrderByDescending(p => p.Key).First(p => p.Key <= currentRuntimeVersion).Value;
			}

			return null;
		}

		private Byte[] Download(AssemblyName asmName)
		{
			HttpWebRequest request = (HttpWebRequest)WebRequest.Create(Constant.NuGet.CreateDownloadUrl(asmName));
			using(HttpWebResponse response = (HttpWebResponse)request.GetResponse())
			{
				if(response.StatusCode == HttpStatusCode.OK)
				{
					using(Stream stream = response.GetResponseStream())
						return StreamToBytes(stream);
				} else return null;
			}
		}

		public static IEnumerable<NuGetPackageEntry> Extract(String filePath)
		{
			using(FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
				return Extract(stream);
		}

		public static IEnumerable<NuGetPackageEntry> Extract(Byte[] payload)
		{
			using(MemoryStream stream = new MemoryStream(payload))
				return Extract(stream);
		}

		public static IEnumerable<NuGetPackageEntry> Extract(Stream stream)
		{
			List<NuGetPackageEntry> entries = new List<NuGetPackageEntry>();
			
			using(ZipArchive zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false))
			{
				Int32 index = 0;
				foreach(ZipArchiveEntry entry in zip.Entries)
				{
					if(FilePluginArgs.CheckFileExtension(entry.FullName))
					{
						using(Stream zipStream = entry.Open())
						{
							entries.Add(new NuGetPackageEntry(index, entry.FullName, StreamToBytes(zipStream)));
						}
					}
					index++;
				}
			}
			
			return entries;
		}

		public static NuGetPackageEntry Extract(String filePath, Int64 index)
		{
			using(FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
			using(ZipArchive zip = new ZipArchive(stream, ZipArchiveMode.Read))
			{
				Int32 currentIndex = 0;
				foreach(ZipArchiveEntry entry in zip.Entries)
				{
					if(currentIndex == index)
					{
						using(Stream zipStream = entry.Open())
						{
							return new NuGetPackageEntry(index, entry.FullName, StreamToBytes(zipStream));
						}
					}
					currentIndex++;
				}
			}
			return null;
		}

		internal static Byte[] StreamToBytes(Stream stream)
		{
			const Int32 MinBufferLength = 1024;
			Byte[] buffer = new Byte[4 * MinBufferLength];
			using(MemoryStream memory = new MemoryStream())
			{
				Int32 count = 0;
				do
				{
					count = stream.Read(buffer, 0, buffer.Length);
					memory.Write(buffer, 0, count);
				} while(count != 0);
				return memory.ToArray();
			}
		}
	}
}