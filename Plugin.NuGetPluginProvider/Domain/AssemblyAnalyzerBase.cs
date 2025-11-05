using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Plugin.FilePluginProvider;

namespace Plugin.NuGetPluginProvider.Domain
{
	[Serializable]
	internal class AssemblyAnalyzerBase : MarshalByRefObject
	{
		private TraceSource _trace;

		private ResolveEventHandler _reflectionOnlyResolve;

		private ResolveEventHandler _resolve;

		protected internal TraceSource Trace { get => this._trace ?? (this._trace = Plugin.CreateTraceSource<Plugin>(".AssemblyAnalyzer")); }

		protected void AttachResolveEvents(String assemblyPath)
		{
			DirectoryInfo directory = new DirectoryInfo(assemblyPath);

			this._reflectionOnlyResolve = (s, e) => this.OnReflectionOnlyResolve(e, directory);
			this._resolve = (s, e) => this.OnResolve(e, directory);

			AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve += this._reflectionOnlyResolve;
			AppDomain.CurrentDomain.AssemblyResolve += this._resolve;//TODO: Attempt to download missing assemblies
		}

		protected void DetachResolveEvents()
		{
			AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve -= this._reflectionOnlyResolve;
			AppDomain.CurrentDomain.AssemblyResolve -= this._resolve;//TODO: Attempt to download missing assemblies
		}

		/// <summary>Attempts ReflectionOnlyLoad of current Assemblies dependents</summary>
		/// <param name="args">ReflectionOnlyAssemblyResolve event args</param>
		/// <param name="directory">The current Assemblies Directory</param>
		/// <returns>ReflectionOnlyLoadFrom loaded dependent Assembly</returns>
		private Assembly OnReflectionOnlyResolve(ResolveEventArgs args, DirectoryInfo directory)
		{

			Assembly loadedAssembly = Array.Find(AppDomain.CurrentDomain.ReflectionOnlyGetAssemblies(), (Assembly asm) => { return String.Equals(asm.FullName, args.Name, StringComparison.OrdinalIgnoreCase); });

			if(loadedAssembly != null)
				return loadedAssembly;

			AssemblyName assemblyName = new AssemblyName(args.Name);
			foreach(String pluginExtension in FilePluginArgs.LibraryExtensions)
			{
				String dependentAssemblyFilename = Path.Combine(directory.FullName, assemblyName.Name + pluginExtension);

				if(File.Exists(dependentAssemblyFilename))
					return Assembly.ReflectionOnlyLoadFrom(dependentAssemblyFilename);
			}
			return Assembly.ReflectionOnlyLoad(args.Name);
		}

		private Assembly OnResolve(ResolveEventArgs args, DirectoryInfo directory)
		{
			Assembly loadedAssembly = Array.Find(AppDomain.CurrentDomain.GetAssemblies(), asm => String.Equals(asm.FullName, args.Name, StringComparison.OrdinalIgnoreCase));

			if(loadedAssembly != null)
				return loadedAssembly;

			AssemblyName assemblyName = new AssemblyName(args.Name);
			foreach(String pluginExtension in FilePluginArgs.LibraryExtensions)
			{
				String dependentAssemblyFilename = Path.Combine(directory.FullName, assemblyName.Name + pluginExtension);

				if(File.Exists(dependentAssemblyFilename))
					return Assembly.LoadFrom(dependentAssemblyFilename);
			}

			return null;//return Assembly.Load(args.Name); - StackOverflowException
		}
	}
}