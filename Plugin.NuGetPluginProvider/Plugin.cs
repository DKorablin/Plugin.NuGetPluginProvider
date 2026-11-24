using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Plugin.FilePluginProvider;
using Plugin.NuGetPluginProvider.Data;
using Plugin.NuGetPluginProvider.Domain;
using SAL.Flatbed;

namespace Plugin.NuGetPluginProvider
{
	/// <summary>Downloads and updates SAL plugins from NuGet feeds into local folders</summary>
	/// <remarks>
	/// This provider does not load plugins. Plugins are installed into &lt;TargetPath&gt;\&lt;PackageId&gt;\ folders
	/// and are loaded by the file scanning plugin providers (the host provider, FileDomain, FileContext, PeReader)
	/// </remarks>
	public class Plugin : IPluginProvider
	{
		private const Int32 MaxRecursiveAssemblyResolve = 5;

		private readonly ConcurrentDictionary<String, Int32> _resolveDepth = new ConcurrentDictionary<String, Int32>(StringComparer.OrdinalIgnoreCase);
		private Boolean _pluginsLoadedAttached;

		internal ITraceSource Trace { get; }

		internal IHost Host { get; }

		IPluginProvider IPluginProvider.ParentProvider { get; set; }

		/// <summary>Arguments passed to the main application</summary>
		private FilePluginArgs Args { get; }

		/// <summary>Package manager or null when the provider failed to initialize</summary>
		public PackageManager Manager { get; private set; }

		public Plugin(IHost host, ITraceSource trace)
		{
			this.Host = host ?? throw new ArgumentNullException(nameof(host));
			this.Trace = trace ?? throw new ArgumentNullException(nameof(trace));
			this.Args = new FilePluginArgs();
		}

		Boolean IPlugin.OnConnection(ConnectMode mode)
			=> true;

		Boolean IPlugin.OnDisconnection(DisconnectMode mode)
		{
			if(this._pluginsLoadedAttached)
			{
				this.Host.Plugins.PluginsLoaded -= this.Plugins_PluginsLoaded;
				this._pluginsLoadedAttached = false;
			}

			if(mode == DisconnectMode.HostShutdown || mode == DisconnectMode.FlatbedClosed)
			{
				this.Manager?.Dispose();
				this.Manager = null;
			}
			return true;
		}

		/// <summary>Install, update and remove packages according to the manifest. Plugins are not loaded by this provider</summary>
		public void LoadPlugins()
		{
			if(this.Manager != null)
				return;//Already initialized

			try
			{
				String manifestPath = this.GetManifestPath();
				NuGetManifest manifest = NuGetManifest.Load(manifestPath, out Boolean created);
				if(created)
					this.TrySaveDefaultManifest(manifest);

				HostEnvironment environment = HostEnvironment.Detect();
				this.Trace.TraceEvent(TraceEventType.Verbose, 7, "Host: {0}. Manifest: {1}. Packages: {2}", environment, manifestPath, manifest.Packages.Count);

				String dataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SAL", "NuGet");
				PackageManager manager = new PackageManager(manifest, environment, dataRoot, this.Trace);
				manager.Cleanup();
				manager.Reconcile(this.IsFolderLoaded);
				this.Manager = manager;

				if(manifest.CheckForUpdates && !this._pluginsLoadedAttached)
				{
					this.Host.Plugins.PluginsLoaded += this.Plugins_PluginsLoaded;
					this._pluginsLoadedAttached = true;
				}
			} catch(Exception exc)
			{
				this.Trace.TraceData(TraceEventType.Error, 7, exc);
			}
		}

		Assembly IPluginProvider.ResolveAssembly(String assemblyName)
		{
			if(String.IsNullOrEmpty(assemblyName))
				throw new ArgumentNullException(nameof(assemblyName));

			IPluginProvider parentProvider = ((IPluginProvider)this).ParentProvider;
			Int32 depth = this._resolveDepth.AddOrUpdate(assemblyName, 1, (key, value) => value + 1);
			try
			{
				if(depth > MaxRecursiveAssemblyResolve)
					return parentProvider?.ResolveAssembly(assemblyName);

				AssemblyName targetName = new AssemblyName(assemblyName);
				PackageManager manager = this.Manager;

				// 0. Assemblies of this provider (NuGet client). Without this, on .NET Core the parent provider may load
				// a second copy with Assembly.LoadFile into an isolated context and break type identity
				Assembly own = FindLoadedAssembly(targetName) ?? LoadFromProviderFolder(targetName);
				if(own != null)
					return own;

				// 1. Assemblies downloaded on demand earlier
				String path = manager?.Resolver.Index.FindExact(targetName);
				if(path != null)
					return Assembly.LoadFrom(path);

				// 2. Other plugin providers
				Assembly result = parentProvider?.ResolveAssembly(assemblyName);
				if(result != null)
					return result;

				// 3. Downloaded assembly of the same major with equal or higher version
				path = manager?.Resolver.Index.FindCompatible(targetName);
				if(path != null)
					return Assembly.LoadFrom(path);

				// 4. Download from NuGet
				if(manager != null && manager.Manifest.ResolveOnDemand)
				{
					path = manager.Resolver.TryResolve(targetName);
					if(path != null)
						return Assembly.LoadFrom(path);
				}

				return null;
			} finally
			{
				this._resolveDepth.AddOrUpdate(assemblyName, 0, (key, value) => value - 1);
			}
		}

		/// <summary>Find an already loaded assembly with exactly the same full name</summary>
		/// <param name="name">Requested assembly</param>
		/// <returns>Loaded assembly or null</returns>
		private static Assembly FindLoadedAssembly(AssemblyName name)
			=> Array.Find(AppDomain.CurrentDomain.GetAssemblies(),
				a => !a.IsDynamic && String.Equals(a.FullName, name.FullName, StringComparison.OrdinalIgnoreCase));

		/// <summary>Load a dependency of this provider from the provider folder</summary>
		/// <remarks>Mimics the .NET Framework LoadFrom context probing that .NET Core does not have</remarks>
		/// <param name="name">Requested assembly</param>
		/// <returns>Loaded assembly or null when the folder has no assembly with the same name and public key token and equal or higher version</returns>
		private static Assembly LoadFromProviderFolder(AssemblyName name)
		{
			String folder = Path.GetDirectoryName(typeof(Plugin).Assembly.Location);
			if(String.IsNullOrEmpty(folder))
				return null;

			String filePath = Path.Combine(folder, name.Name + ".dll");
			if(!File.Exists(filePath))
				return null;

			try
			{
				AssemblyName candidate = AssemblyName.GetAssemblyName(filePath);
				return OnDemandResolver.IsMatch(name, candidate)
					? Assembly.LoadFrom(filePath)
					: null;
			} catch(BadImageFormatException)
			{
				return null;
			} catch(IOException)
			{
				return null;
			}
		}

		/// <summary>Find newer compatible versions of the installed plugins</summary>
		/// <returns>Available updates</returns>
		public IReadOnlyList<PackageUpdateInfo> CheckForUpdates()
			=> this.GetManager().CheckForUpdates();

		/// <summary>Find compatible plugins on the feeds that are not installed</summary>
		/// <returns>Available plugins</returns>
		public IReadOnlyList<PackageUpdateInfo> FindAvailablePlugins()
			=> this.GetManager().FindAvailablePlugins();

		/// <summary>Add the package to the manifest and install it. The plugin is loaded by the file scanning providers after restart</summary>
		/// <param name="packageId">Package identifier</param>
		/// <param name="version">Version to install or null for the latest compatible version</param>
		/// <returns>The package is installed</returns>
		public Boolean Install(String packageId, String version)
			=> this.GetManager().Install(packageId, version, this.IsFolderLoaded);

		private PackageManager GetManager()
			=> this.Manager ?? throw new InvalidOperationException("NuGet plugin provider is not initialized. See the log for details");

		private void Plugins_PluginsLoaded(Object sender, EventArgs e)
		{
			this.Host.Plugins.PluginsLoaded -= this.Plugins_PluginsLoaded;
			this._pluginsLoadedAttached = false;

			PackageManager manager = this.Manager;
			if(manager != null)
				Task.Run(() =>
				{
					try
					{
						manager.LogUpdatesAndNewPlugins();
					} catch(Exception exc)
					{
						this.Trace.TraceData(TraceEventType.Error, 7, exc);
					}
				});
		}

		/// <summary>Plugins from the folder are already loaded by other plugin providers</summary>
		/// <param name="folder">Package folder</param>
		/// <returns>At least one plugin source is inside the folder</returns>
		private Boolean IsFolderLoaded(String folder)
		{
			String prefix = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
			return this.Host.Plugins.Any(p => p.Source != null && p.Source.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
		}

		/// <summary>Use the existing manifest from the plugin folders or the first plugin folder</summary>
		/// <returns>Path to the manifest</returns>
		private String GetManifestPath()
		{
			String[] paths = this.Args.PluginPath;
			String existing = paths.Select(p => Path.Combine(p, NuGetManifest.FileName)).FirstOrDefault(File.Exists);
			return Path.GetFullPath(existing ?? Path.Combine(paths[0], NuGetManifest.FileName));
		}

		private void TrySaveDefaultManifest(NuGetManifest manifest)
		{
			try
			{
				manifest.Save();
				this.Trace.TraceEvent(TraceEventType.Information, 7, "Default NuGet manifest is created: {0}", manifest.FilePath);
			} catch(Exception exc) when(exc is IOException || exc is UnauthorizedAccessException)
			{
				this.Trace.TraceEvent(TraceEventType.Warning, 7, "Default NuGet manifest can't be created at {0}: {1}. Defaults are used", manifest.FilePath, exc.Message);
			}
		}
	}
}