using System.Reflection;
using System.Runtime.InteropServices;

[assembly: Guid("25ea8c1f-932d-48c2-be4c-34e165f068d7")]
[assembly: ComVisible(false)]
[assembly: System.CLSCompliant(true)]

#if NETSTANDARD || NETCOREAPP
[assembly: AssemblyMetadata("ProjectUrl", "https://github.com/DKorablin/Plugin.NuGetPluginProvider")]
#else
[assembly: AssemblyTitle("Plugin.NuGetPluginProvider")]
[assembly: AssemblyProduct("NuGet plugin provider")]
[assembly: AssemblyCompany("Danila Korablin")]
[assembly: AssemblyCopyright("Copyright © Danila Korablin 2019-2024")]

#if DEBUG
[assembly: AssemblyConfiguration("Debug")]
#else
[assembly: AssemblyConfiguration("Release")]
#endif

#endif