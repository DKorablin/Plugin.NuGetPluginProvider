using System.Reflection;
using System.Runtime.InteropServices;

[assembly: Guid("25ea8c1f-932d-48c2-be4c-34e165f068d7")]
[assembly: System.CLSCompliant(true)]

#if NETSTANDARD || NETCOREAPP
[assembly: AssemblyMetadata("ProjectUrl", "https://github.com/DKorablin/Plugin.NuGetPluginProvider")]
#else
[assembly: AssemblyDescription("NuGet plugin provider")]
[assembly: AssemblyCopyright("Copyright © Danila Korablin 2019-2025")]

#endif