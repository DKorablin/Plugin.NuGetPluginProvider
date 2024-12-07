using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Plugin.NuGetPluginProvider.NuGetClient;
using SAL.Flatbed;

namespace Plugin.NuGetPluginProvider.Domain
{
	[Serializable]
	internal class AssemblyAnalyzer : AssemblyAnalyzerBase
	{
		public AssemblyTypesInfo[] CheckAssemblies(String path)
		{
			Version currentRuntimeVersion = new Version(Assembly.GetExecutingAssembly().ImageRuntimeVersion.Trim('v'));
			base.AttachResolveEvents(path);

			List<AssemblyTypesInfo> assemblies = new List<AssemblyTypesInfo>();
			try
			{
				foreach(String filePath in Directory.GetFiles(path, Constant.PackageSearchExtension))
				{
					using(FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
						foreach(NuGetPackageEntry entry in NuGetDownloader.Extract(stream))
						{
							List<String> types = new List<String>();
							try
							{
								Assembly assembly = Assembly.ReflectionOnlyLoad(entry.Payload);
								Version runtimeVersion = new Version(assembly.ImageRuntimeVersion.Trim('v'));
								if(runtimeVersion > currentRuntimeVersion)
									continue;

								foreach(Type assemblyType in assembly.GetTypes())
									if(PluginUtils.IsPluginType(assemblyType))
										types.Add(assemblyType.FullName);
							} catch(BadImageFormatException)
							{
								continue;
							} catch(FileLoadException)
							{
								continue;
							} catch(Exception exc)
							{
								exc.Data.Add("Library", filePath);
								base.Trace.TraceData(TraceEventType.Error, 1, exc);
							}

							if(types.Count > 0)
							{//Находим первую подходящую сборку и выходим из Package'а
								AssemblyTypesInfo info = new AssemblyTypesInfo(filePath, entry.EntryIndex, types.ToArray());
								assemblies.Add(info);
								break;
							}
						}
				}
			} finally
			{
				base.DetachResolveEvents();
			}

			return assemblies.ToArray();
		}
	}
}