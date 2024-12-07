using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using ICSharpCode.SharpZipLib.Zip;
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

				if(assemblies.Count > 0)//Нам необходимо найти самую приближённую к текущему рантайму версию, т.к. может попастся не поддерживаемые (.NET Core) или очень старые варианты
					return assemblies.OrderByDescending(p => p.Key).First(p => p.Key <= currentRuntimeVersion).Value;
			}

			return null;
		}

		private Byte[] Download(AssemblyName asmName)
		{
			const Int32 MinBufferLength = 1024;

			HttpWebRequest request = (HttpWebRequest)WebRequest.Create(Constant.NuGet.CreateDownloadUrl(asmName));
			using(HttpWebResponse response = (HttpWebResponse)request.GetResponse())
			{
				if(response.StatusCode == HttpStatusCode.OK)
				{
					Byte[] buffer = new Byte[4 * MinBufferLength];
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
			using(ZipFile zip = new ZipFile(stream))
			{
				for(Int64 loop = 0; loop < zip.Count; loop++)
				{
					ZipEntry entry = zip[(Int32)loop];
					if(new FilePluginArgs().CheckFileExtension(entry.Name))
						using(Stream zipStream = zip.GetInputStream(entry))
							yield return new NuGetPackageEntry(entry.ZipFileIndex, entry.Name, StreamToBytes(zipStream));
				}
			}
		}

		public static NuGetPackageEntry Extract(String filePath, Int64 index)
		{
			using(FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
			using(ZipFile zip = new ZipFile(stream))
			{
				ZipEntry entry = zip[(Int32)index];
				if(entry != null)
					using(Stream zipStream = zip.GetInputStream(entry))
						return new NuGetPackageEntry(entry.ZipFileIndex, entry.Name, StreamToBytes(zipStream));
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