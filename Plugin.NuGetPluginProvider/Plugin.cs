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
		#region Fields
		private readonly FilePluginArgs _args;
		private readonly IHost _host;
		private TraceSource _trace;
		#endregion Fields

		internal TraceSource Trace
		{
			get { return this._trace ?? (this._trace = Plugin.CreateTraceSource<Plugin>()); }
		}

		internal IHost Host { get { return this._host; } }

		IPluginProvider IPluginProvider.ParentProvider { get; set; }

		/// <summary>Аргументы передаваемые основному приложению</summary>
		private FilePluginArgs Args { get { return this._args; } }

		public Plugin(IHost host)
		{
			this._host = host ?? throw new ArgumentNullException(nameof(host));
			this._args = new FilePluginArgs();
		}

		#region IPlugin
		Boolean IPlugin.OnConnection(ConnectMode mode)
		{
			return true;
		}

		Boolean IPlugin.OnDisconnection(DisconnectMode mode)
		{
			return true;
		}
		#endregion IPlugin

		public void LoadPlugins()
		{//TODO: Add NuGet Search for plugins
			/* Необходимо реализовать предварительную выборку плагинов, дабы не загружать из NuGet'а всё подряд
			 * Необходимо реализовать локальное хранилище плагинов, дабы при каждом старте приложения не лазать в NuGet
			 * Так же, необходимо резолвить сборку через песочницу (скопировать код из FileDomainPluginProvider)
			 */
			foreach(String pluginPath in this.Args.PluginPath)
				if(Directory.Exists(pluginPath))
				{
					AssemblyTypesInfo[] infos;
					using(AssemblyLoader<AssemblyAnalyzer> analyzer = new AssemblyLoader<AssemblyAnalyzer>())
						infos = analyzer.Proxy.CheckAssemblies(pluginPath);

					foreach(AssemblyTypesInfo info in infos)
						this.LoadAssembly(info, ConnectMode.Startup);
				}
		}

		Assembly IPluginProvider.ResolveAssembly(String assemblyName)
		{//TODO: Не работает resolve сборок, т.к. SettingsProvider в момент загрузки плагинов - не загружен
			/*if(String.IsNullOrEmpty(assemblyName))
				throw new ArgumentNullException("assemblyName");

			AssemblyName targetName = new AssemblyName(assemblyName);

			//TODO: Надо доделать временный путь, куда будут сохраняться сборки позсле загрузки из NuGet'а (по аналогии с NetworkPluginProvider)
			NuGetDownloader extractor = new NuGetDownloader(targetName);
			Byte[] rawAssembly = extractor.GetAssemblyFromNuGet();
			if(rawAssembly != null)
				return Assembly.Load(rawAssembly);

			this.Trace.TraceEvent(TraceEventType.Warning, 5, "Assembly {0} can't be resolved in NuGet server {1} by provider {2}", assemblyName, Constant.NuGet.CreateDownloadUrl(targetName), this.GetType());*/
			IPluginProvider parentProvider = ((IPluginProvider)this).ParentProvider;
			return parentProvider == null
				? null
				: parentProvider.ResolveAssembly(assemblyName);
		}

		private void LoadAssembly(AssemblyTypesInfo info, ConnectMode mode)
		{
			try
			{
				if(info.Types.Length == 0)
					throw new InvalidOperationException("Types is empty");

				// Проверяем что плагин с таким источником ещё не загружен, если его уже загрузил родительский провайдер.
				// Загрузка из ФС так что источник должен быть по любому уникальный.
				foreach(IPluginDescription plugin in this.Host.Plugins)
					if(info.PackagePath.Equals(plugin.Source, StringComparison.InvariantCultureIgnoreCase))
						return;

				NuGetPackageEntry entry = NuGetDownloader.Extract(info.PackagePath, info.FileIndex);
				Assembly assembly = Assembly.Load(entry.Payload);
				foreach(String type in info.Types)
					this.Host.Plugins.LoadPlugin(assembly, type, info.PackagePath, mode);

			} catch(BadImageFormatException exc)//Ошибка загрузки плагина. Можно почитать заголовок загружаемого файла, но мне влом
			{
				exc.Data.Add(nameof(info.PackagePath), info.PackagePath);
				exc.Data.Add(nameof(info.FileIndex), info.FileIndex);
				this.Trace.TraceData(TraceEventType.Error, 1, exc);
				return;
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