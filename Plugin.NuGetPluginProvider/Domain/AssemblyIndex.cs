using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Plugin.NuGetPluginProvider.Domain
{
	/// <summary>Index of assemblies downloaded by the on-demand resolver</summary>
	public class AssemblyIndex
	{
		private readonly Object _lock = new Object();
		private readonly Dictionary<String, List<KeyValuePair<AssemblyName, String>>> _byName = new Dictionary<String, List<KeyValuePair<AssemblyName, String>>>(StringComparer.OrdinalIgnoreCase);
		private readonly HashSet<String> _folders = new HashSet<String>(StringComparer.OrdinalIgnoreCase);

		/// <summary>Number of indexed assemblies</summary>
		public Int32 Count
		{
			get
			{
				lock(this._lock)
					return this._byName.Values.Sum(v => v.Count);
			}
		}

		/// <summary>Add all assemblies from the folder and its subfolders</summary>
		/// <param name="folder">Folder to scan</param>
		public void AddFolder(String folder)
		{
			if(!Directory.Exists(folder))
				return;

			lock(this._lock)
			{
				if(!this._folders.Add(Path.GetFullPath(folder)))
					return;

				foreach(String file in Directory.EnumerateFiles(folder, "*.dll", SearchOption.AllDirectories))
				{
					AssemblyName name;
					try
					{
						name = AssemblyName.GetAssemblyName(file);
					} catch(BadImageFormatException)
					{//Native library
						continue;
					} catch(IOException)
					{
						continue;
					}

					if(!this._byName.TryGetValue(name.Name, out List<KeyValuePair<AssemblyName, String>> list))
					{
						list = new List<KeyValuePair<AssemblyName, String>>();
						this._byName.Add(name.Name, list);
					}
					if(!list.Exists(i => String.Equals(i.Value, file, StringComparison.OrdinalIgnoreCase)))
						list.Add(new KeyValuePair<AssemblyName, String>(name, file));
				}
			}
		}

		/// <summary>Find the assembly with exactly the same full name</summary>
		/// <param name="name">Requested assembly</param>
		/// <returns>Path to the assembly or null</returns>
		public String FindExact(AssemblyName name)
		{
			lock(this._lock)
				return this._byName.TryGetValue(name.Name, out List<KeyValuePair<AssemblyName, String>> list)
					? list.FirstOrDefault(i => String.Equals(i.Key.FullName, name.FullName, StringComparison.OrdinalIgnoreCase)).Value
					: null;
		}

		/// <summary>Find the assembly with the same name and public key token, the same major and equal or higher version</summary>
		/// <param name="name">Requested assembly</param>
		/// <returns>Path to the assembly with the highest version or null</returns>
		public String FindCompatible(AssemblyName name)
		{
			lock(this._lock)
			{
				if(!this._byName.TryGetValue(name.Name, out List<KeyValuePair<AssemblyName, String>> list))
					return null;

				Byte[] token = name.GetPublicKeyToken() ?? Array.Empty<Byte>();
				return list
					.Where(i => (i.Key.GetPublicKeyToken() ?? Array.Empty<Byte>()).SequenceEqual(token))
					.Where(i => IsCompatibleVersion(name.Version, i.Key.Version))
					.OrderByDescending(i => i.Key.Version)
					.Select(i => i.Value)
					.FirstOrDefault();
			}
		}

		/// <summary>Candidate version can replace the requested one</summary>
		/// <param name="requested">Requested version or null for any</param>
		/// <param name="candidate">Candidate version</param>
		/// <returns>Same major and equal or higher version</returns>
		public static Boolean IsCompatibleVersion(Version requested, Version candidate)
			=> requested == null
				|| (candidate != null && candidate.Major == requested.Major && candidate >= requested);
	}
}