using System;
using System.Linq;
using System.Reflection;
using NuGet.Frameworks;
using NuGet.Versioning;
using SAL.Flatbed;

namespace Plugin.NuGetPluginProvider.Domain
{
	/// <summary>Information about the running host: SAL package versions and target framework</summary>
	public class HostEnvironment
	{
		/// <summary>SAL core package identifier</summary>
		public const String FlatbedPackageId = "SAL.Flatbed";

		/// <summary>SAL UI package identifier</summary>
		public const String WindowsPackageId = "SAL.Windows";

		/// <summary>Version of the SAL.Flatbed package the host is running with</summary>
		public NuGetVersion FlatbedVersion { get; }

		/// <summary>Version of the SAL.Windows package the host is running with or null for hosts without UI</summary>
		public NuGetVersion WindowsVersion { get; }

		/// <summary>Framework used to select dependency groups and lib folders from packages</summary>
		public NuGetFramework Framework { get; }

		/// <summary>Create host environment description</summary>
		/// <param name="flatbedVersion">SAL.Flatbed version</param>
		/// <param name="windowsVersion">SAL.Windows version or null</param>
		/// <param name="framework">Host framework</param>
		public HostEnvironment(NuGetVersion flatbedVersion, NuGetVersion windowsVersion, NuGetFramework framework)
		{
			this.FlatbedVersion = flatbedVersion ?? throw new ArgumentNullException(nameof(flatbedVersion));
			this.WindowsVersion = windowsVersion;
			this.Framework = framework ?? throw new ArgumentNullException(nameof(framework));
		}

		/// <summary>Detect the environment of the current process</summary>
		/// <returns>Host environment</returns>
		public static HostEnvironment Detect()
		{
			Assembly flatbed = typeof(IPlugin).Assembly;
			Assembly windows = Array.Find(AppDomain.CurrentDomain.GetAssemblies(),
				a => String.Equals(a.GetName().Name, WindowsPackageId, StringComparison.OrdinalIgnoreCase));

			return new HostEnvironment(GetPackageVersion(flatbed), windows == null ? null : GetPackageVersion(windows), DetectFramework());
		}

		/// <summary>Get the package version of the SAL assembly</summary>
		/// <remarks>SAL assemblies store the package version in <see cref="AssemblyInformationalVersionAttribute"/>, the assembly version is the same version without pre-release suffix</remarks>
		/// <param name="assembly">Assembly to check</param>
		/// <returns>Package version</returns>
		public static NuGetVersion GetPackageVersion(Assembly assembly)
		{
			AssemblyInformationalVersionAttribute info = (AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(assembly, typeof(AssemblyInformationalVersionAttribute));
			if(info != null && NuGetVersion.TryParse(info.InformationalVersion, out NuGetVersion result))
				return result;

			return new NuGetVersion(assembly.GetName().Version);
		}

		/// <summary>Detect the framework of the current process</summary>
		/// <returns>.NETFramework 4.8 or netX.Y[-windowsA.B.C]</returns>
		public static NuGetFramework DetectFramework()
		{
#if NETFRAMEWORK
			return NuGetFramework.Parse("net48");
#else
			Version runtime = Environment.Version;
			Version os = Environment.OSVersion.Version;
			return OperatingSystem.IsWindows()
				? CreateNetFramework(runtime, os)
				: CreateNetFramework(runtime, null);
#endif
		}

		/// <summary>Create .NET 5+ framework with optional Windows platform</summary>
		/// <param name="runtime">Runtime version</param>
		/// <param name="windows">Windows version or null when the platform is not Windows</param>
		/// <returns>Framework, e.g. net8.0-windows10.0.26200</returns>
		public static NuGetFramework CreateNetFramework(Version runtime, Version windows)
		{
			String tfm = $"net{runtime.Major}.{runtime.Minor}";
			if(windows != null)
				tfm += $"-windows{windows.Major}.{windows.Minor}.{Math.Max(windows.Build, 0)}";
			return NuGetFramework.Parse(tfm);
		}

		/// <summary>Get the host version of the SAL package</summary>
		/// <param name="packageId">SAL.Flatbed or SAL.Windows</param>
		/// <returns>Host version or null when the host does not provide the package</returns>
		public NuGetVersion GetHostVersion(String packageId)
		{
			if(String.Equals(packageId, FlatbedPackageId, StringComparison.OrdinalIgnoreCase))
				return this.FlatbedVersion;
			if(String.Equals(packageId, WindowsPackageId, StringComparison.OrdinalIgnoreCase))
				return this.WindowsVersion;
			return null;
		}

		/// <summary>The package is provided by the host and is never installed</summary>
		/// <param name="packageId">Package identifier</param>
		/// <returns>The package is SAL.Flatbed or SAL.Windows</returns>
		public static Boolean IsHostPackage(String packageId)
			=> String.Equals(packageId, FlatbedPackageId, StringComparison.OrdinalIgnoreCase)
				|| String.Equals(packageId, WindowsPackageId, StringComparison.OrdinalIgnoreCase);

		/// <summary>An assembly with the same name as the package is already loaded with the same or higher version</summary>
		/// <param name="packageId">Package identifier (usually equals to the assembly name)</param>
		/// <param name="minVersion">Minimal required package version</param>
		/// <returns>The process already has a suitable assembly</returns>
		public virtual Boolean IsAlreadyLoaded(String packageId, NuGetVersion minVersion)
		{
			Version required = minVersion == null ? new Version(0, 0) : new Version(minVersion.Major, minVersion.Minor);
			return AppDomain.CurrentDomain.GetAssemblies()
				.Select(a => a.GetName())
				.Any(n => String.Equals(n.Name, packageId, StringComparison.OrdinalIgnoreCase) && n.Version >= required);
		}

		/// <inheritdoc/>
		public override String ToString()
			=> $"{FlatbedPackageId} {this.FlatbedVersion}, {WindowsPackageId} {this.WindowsVersion?.ToString() ?? "<none>"}, {this.Framework.GetShortFolderName()}";
	}
}