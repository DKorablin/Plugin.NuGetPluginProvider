using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using AlphaOmega.Reflection;
using Plugin.FilePluginProvider;
using Plugin.NuGetPluginProvider.Domain;
using Plugin.NuGetPluginProvider.NuGetClient;
using SAL.Flatbed;

namespace Plugin.NuGetPluginProvider
{
	public class Plugin : IPluginProvider
	{
		private TraceSource _trace;

		internal TraceSource Trace
		{
			get => this._trace ?? (this._trace = Plugin.CreateTraceSource<Plugin>());
		}

		internal IHost Host { get; }

		IPluginProvider IPluginProvider.ParentProvider { get; set; }

		/// <summary>Arguments passed to the main application</summary>
		private FilePluginArgs Args { get; }

		public Plugin(IHost host)
		{
			this.Host = host ?? throw new ArgumentNullException(nameof(host));
			this.Args = new FilePluginArgs();
		}

		Boolean IPlugin.OnConnection(ConnectMode mode)
			=> true;

		Boolean IPlugin.OnDisconnection(DisconnectMode mode)
			=> true;

		public void LoadPlugins()
		{//TODO: Add NuGet Search for plugins
			/* We need to implement plugin prefetching to avoid loading everything from NuGet
			* We need to implement local plugin storage to avoid accessing NuGet every time the application starts
			* We also need to resolve the assembly through a sandbox (copy the code from FileDomainPluginProvider)
			*/
			foreach(String pluginPath in this.Args.PluginPath)
				if(Directory.Exists(pluginPath))
				{
					AssemblyTypesInfo[] items;
					using(AssemblyLoader<AssemblyAnalyzer> analyzer = new AssemblyLoader<AssemblyAnalyzer>())
						items = analyzer.Proxy.CheckAssemblies(pluginPath);

					foreach(AssemblyTypesInfo info in items)
						this.LoadAssembly(info, ConnectMode.Startup);
				}
		}

		Assembly IPluginProvider.ResolveAssembly(String assemblyName)
		{//TODO: Assembly resolution does not work because the SettingsProvider is not loaded when the plugins are loaded.
			/*if(String.IsNullOrEmpty(assemblyName))
				throw new ArgumentNullException("assemblyName");

			AssemblyName targetName = new AssemblyName(assemblyName);

			//TODO: We need to add a temporary path where assemblies will be saved after downloading from NuGet (similar to NetworkPluginProvider)
			NuGetDownloader extractor = new NuGetDownloader(targetName);
			Byte[] rawAssembly = extractor.GetAssemblyFromNuGet();
			if(rawAssembly != null)
				return Assembly.Load(rawAssembly);

			this.Trace.TraceEvent(TraceEventType.Warning, 5, "Assembly {0} can't be resolved in NuGet server {1} by provider {2}", assemblyName, Constant.NuGet.CreateDownloadUrl(targetName), this.GetType());*/
			IPluginProvider parentProvider = ((IPluginProvider)this).ParentProvider;
			return parentProvider?.ResolveAssembly(assemblyName);
		}

		private void LoadAssembly(AssemblyTypesInfo info, ConnectMode mode)
		{
			try
			{
				if(info.Types.Length == 0)
					throw new InvalidOperationException("Types are empty");

				// Check that the plugin with this source hasn't yet been loaded if it's already loaded by the parent provider.
				// Loading from the file system, so the source must be unique.
				foreach(IPluginDescription plugin in this.Host.Plugins)
					if(info.PackagePath.Equals(plugin.Source, StringComparison.InvariantCultureIgnoreCase))
						return;

				NuGetPackageEntry entry = NuGetDownloader.Extract(info.PackagePath, info.FileIndex);
				Assembly assembly = Assembly.Load(entry.Payload);
				foreach(String type in info.Types)
					this.Host.Plugins.LoadPlugin(assembly, type, info.PackagePath, mode);

			} catch(BadImageFormatException exc)//Plugin loading error. I could read the title of the file being loaded, but I'm too lazy.
			{
				exc.Data.Add(nameof(info.PackagePath), info.PackagePath);
				exc.Data.Add(nameof(info.FileIndex), info.FileIndex);
				this.Trace.TraceData(TraceEventType.Error, 1, exc);
			} catch(Exception exc)
			{
				exc.Data.Add(nameof(info.PackagePath), info.PackagePath);
				exc.Data.Add(nameof(info.FileIndex), info.FileIndex);
				this.Trace.TraceData(TraceEventType.Error, 1, exc);
			}
		}

		internal static TraceSource CreateTraceSource<T>(String name = null) where T : IPlugin
		{
			TraceSource result = new TraceSource(typeof(T).Assembly.GetName().Name + name);
			result.Switch.Level = SourceLevels.All;
			result.Listeners.Remove("Default");
			result.Listeners.AddRange(System.Diagnostics.Trace.Listeners);
			return result;
		}
	}
}