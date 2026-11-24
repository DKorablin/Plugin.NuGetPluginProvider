# NuGet plugin provider
[![Auto build](https://github.com/DKorablin/Plugin.NuGetPluginProvider/actions/workflows/release.yml/badge.svg)](https://github.com/DKorablin/Plugin.NuGetPluginProvider/releases/latest)

A SAL plugin provider that keeps plugins up to date from NuGet feeds:
1. Finds new plugins on the feeds that are compatible with the running host.
2. Checks for newer versions of the downloaded plugins.
3. Downloads and updates plugins into local folders, one subfolder per package.
4. Resolves dependencies. All dependencies are downloaded together with the plugin, and missing strong-named assemblies can be downloaded on demand at runtime.

**This provider does not load plugins.** Loading is left to the plugin providers that scan folders recursively:
- the host's built-in provider (when the target folder is under the host folder)
- [FileDomainPluginProvider](https://github.com/DKorablin/Plugin.FileDomainPluginProvider)
- [FileContextPluginProvider](https://github.com/DKorablin/Plugin.FileContextPluginProvider)
- [PeReaderPluginProvider](https://github.com/DKorablin/Plugin.PeReaderPluginProvider)

FilePluginProvider, MemoryPluginProvider and NetworkPluginProvider only scan the top level of a folder, so they don't see the package subfolders.

## Compatibility
A package is treated as a plugin when it depends on `SAL.Flatbed` and/or `SAL.Windows`. It is compatible only when each of those dependencies has **exactly** the version the host is running with. The host version is read from the loaded `SAL.Flatbed.dll` and `SAL.Windows.dll`. A package that needs `SAL.Windows` is not compatible with a host that has no UI.

For the host framework (`net48`, or `net8.0-windows` and newer), the nearest dependency group and `lib/` folder of the package are used.

## Manifest
The configuration is stored in `Plugins.NuGet.json` in the first `SAL_Path` folder (or the current folder). A default file is created on the first start:
```json
{
  "Feeds": [
    { "Name": "nuget.org", "Url": "https://api.nuget.org/v3/index.json", "Enabled": true },
    { "Name": "github_DKorablin", "Url": "https://nuget.pkg.github.com/DKorablin/index.json",
      "UserName": "%SAL_NUGET_USER%", "Password": "%SAL_NUGET_TOKEN%", "Enabled": false }
  ],
  "TargetPath": null,
  "SearchTerm": "AlphaOmega.SAL.Plugin",
  "IncludePrerelease": false,
  "CheckForUpdates": true,
  "ResolveOnDemand": true,
  "ResolveUnsignedOnDemand": false,
  "ResolveTimeoutSeconds": 30,
  "Packages": [
    { "Id": "AlphaOmega.SAL.Plugin.Timers", "Version": null, "TargetPath": null }
  ]
}
```
| Setting | Description |
|---|---|
| `Feeds` | NuGet V3 feeds, searched in the listed order. `%NAME%` in `UserName` and `Password` is replaced with the environment variable value. The provider warns when a password is stored as plain text. |
| `TargetPath` | Folder where packages are installed, as `<TargetPath>\<PackageId>\`. The default is the manifest folder. A relative path is relative to the manifest folder. Each package can override it. |
| `SearchTerm` | Term used to search for new plugins. Package ids must contain this term. |
| `CheckForUpdates` | After all plugins are loaded, check for updates and new plugins in the background and write the results to the log. |
| `ResolveOnDemand` | Download an assembly from NuGet when no plugin provider can resolve it. |
| `ResolveUnsignedOnDemand` | Also download assemblies without a strong name. This is off by default (see [On-demand resolution](#on-demand-resolution)). |
| `Packages` | Requested plugins. If `Version` is `null`, the latest compatible version is installed once and then kept. A version number pins that version. |

### GitHub Packages
The SAL plugins are published to GitHub Packages. That feed requires authentication even for public packages. To use it:
1. Create a personal access token with the `read:packages` scope.
2. Set the `SAL_NUGET_USER` and `SAL_NUGET_TOKEN` environment variables.
3. Enable the `github_DKorablin` feed in the manifest.

## Installing and updating
There is no UI yet. Everything is driven by the manifest and reported in the trace log:
- **Install a plugin:** add `{ "Id": "<package id>" }` to `Packages` and restart the host.
- **Update a plugin:** the log shows `Update available: <id> <current> -> <new>`. Set `"Version": "<new>"` for that package and restart.
- **New plugins:** the log shows `New plugin available: <id> <version> - <title>`.
- **Remove a plugin:** remove the entry from `Packages` and restart. Only files installed by this provider are deleted.

Changes are applied while the providers load at startup. Whether a new or updated plugin is loaded in the same run depends on the order of the plugin providers:
- If the loading provider runs after this one, the change is loaded in the same run.
- Otherwise it is loaded on the next start, and the log says `Restart required`.

A loaded assembly can't be overwritten or deleted. Instead it is renamed to `*.dl_` and removed on the next start.

Temporary and on-demand files are stored in `%LOCALAPPDATA%\SAL\NuGet\`. This folder is outside the plugin folders, so other providers don't scan it.

## On-demand resolution
When no plugin provider can resolve an assembly, the provider looks for a NuGet package with the same id as the assembly name. It probes the highest stable versions of the same major version and downloads the first one whose DLL has:
- the same name
- the same public key token
- the same major version and an equal or higher version

The package and its dependencies are stored in `%LOCALAPPDATA%\SAL\NuGet\resolved\` and reused on the next start.

Limitations:
- Packages whose id differs from the assembly name can't be found. For example, `AlphaOmega.PEReader` contains `PEReader.dll`.
- Assemblies without a strong name are not downloaded by default. Anyone can publish a package with that name to a public feed.
- Resolution blocks the thread that requested the assembly for up to `ResolveTimeoutSeconds`. Failed names are not retried in the same session.

## Not supported yet
- Native `runtimes/` assets are not extracted. A warning is written for packages that contain them.
- Package signatures are not verified.