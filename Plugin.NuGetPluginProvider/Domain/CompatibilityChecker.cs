using System;
using System.Collections.Generic;
using System.Linq;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Plugin.NuGetPluginProvider.Domain
{
	/// <summary>Result of the compatibility check</summary>
	public enum PackageCompatibility
	{
		/// <summary>The package is a plugin built against exactly the host SAL version</summary>
		Compatible,
		/// <summary>The package does not depend on SAL packages, so it is not a plugin</summary>
		NotPlugin,
		/// <summary>The package has no dependency group for the host framework</summary>
		UnsupportedFramework,
		/// <summary>The package requires a SAL package the host does not provide (e.g. SAL.Windows in a headless host)</summary>
		HostPackageMissing,
		/// <summary>The package is built against a different SAL version</summary>
		VersionMismatch,
	}

	/// <summary>Compatibility of a package version with the running host</summary>
	public class CompatibilityResult
	{
		/// <summary>Compatibility status</summary>
		public PackageCompatibility Status { get; }

		/// <summary>Human readable reason</summary>
		public String Reason { get; }

		/// <summary>Dependency group selected for the host framework or null</summary>
		public PackageDependencyGroup Group { get; }

		/// <summary>The package is compatible</summary>
		public Boolean IsCompatible => this.Status == PackageCompatibility.Compatible;

		/// <summary>The package requires a newer SAL version than the host has</summary>
		public Boolean RequiresNewerHost { get; }

		internal CompatibilityResult(PackageCompatibility status, String reason, PackageDependencyGroup group, Boolean requiresNewerHost = false)
		{
			this.Status = status;
			this.Reason = reason;
			this.Group = group;
			this.RequiresNewerHost = requiresNewerHost;
		}
	}

	/// <summary>Checks that a plugin package is built against exactly the SAL version the host is running with</summary>
	public class CompatibilityChecker
	{
		private static readonly FrameworkReducer Reducer = new FrameworkReducer();

		/// <summary>Host environment</summary>
		public HostEnvironment Host { get; }

		/// <summary>Create checker for the host</summary>
		/// <param name="host">Host environment</param>
		public CompatibilityChecker(HostEnvironment host)
			=> this.Host = host ?? throw new ArgumentNullException(nameof(host));

		/// <summary>Select the dependency group nearest to the host framework</summary>
		/// <param name="groups">Package dependency groups</param>
		/// <returns>Nearest group or null when no group is compatible</returns>
		public PackageDependencyGroup GetNearestGroup(IEnumerable<PackageDependencyGroup> groups)
			=> GetNearestGroup(this.Host.Framework, groups);

		/// <summary>Select the dependency group nearest to the framework</summary>
		/// <param name="framework">Target framework</param>
		/// <param name="groups">Package dependency groups</param>
		/// <returns>Nearest group or null when no group is compatible</returns>
		public static PackageDependencyGroup GetNearestGroup(NuGetFramework framework, IEnumerable<PackageDependencyGroup> groups)
		{
			PackageDependencyGroup[] items = groups?.ToArray() ?? Array.Empty<PackageDependencyGroup>();
			if(items.Length == 0)
				return null;

			NuGetFramework nearest = Reducer.GetNearest(framework, items.Select(g => g.TargetFramework));
			return nearest == null
				? null
				: items.First(g => g.TargetFramework.Equals(nearest));
		}

		/// <summary>Check package compatibility with the host</summary>
		/// <param name="groups">Dependency groups of the package version</param>
		/// <returns>Compatibility result</returns>
		public CompatibilityResult Check(IEnumerable<PackageDependencyGroup> groups)
		{
			PackageDependencyGroup[] items = groups?.ToArray() ?? Array.Empty<PackageDependencyGroup>();
			if(items.Length == 0)
				return new CompatibilityResult(PackageCompatibility.NotPlugin, "Package has no dependencies", null);

			PackageDependencyGroup group = this.GetNearestGroup(items);
			if(group == null)
			{
				Boolean isPlugin = items.Any(g => g.Packages.Any(p => HostEnvironment.IsHostPackage(p.Id)));
				return isPlugin
					? new CompatibilityResult(PackageCompatibility.UnsupportedFramework, $"No dependency group for {this.Host.Framework.GetShortFolderName()}", null)
					: new CompatibilityResult(PackageCompatibility.NotPlugin, "Package does not depend on SAL", null);
			}

			PackageDependency[] salDependencies = group.Packages.Where(p => HostEnvironment.IsHostPackage(p.Id)).ToArray();
			if(salDependencies.Length == 0)
				return new CompatibilityResult(PackageCompatibility.NotPlugin, "Package does not depend on SAL", group);

			foreach(PackageDependency dependency in salDependencies)
			{
				NuGetVersion hostVersion = this.Host.GetHostVersion(dependency.Id);
				NuGetVersion required = dependency.VersionRange?.MinVersion;
				if(hostVersion == null)
					return new CompatibilityResult(PackageCompatibility.HostPackageMissing, $"Requires {dependency.Id} which the host does not provide", group);
				if(required == null || !VersionComparer.VersionRelease.Equals(required, hostVersion))
					return new CompatibilityResult(PackageCompatibility.VersionMismatch,
						$"Built against {dependency.Id} {required?.ToString() ?? "<any>"}, host has {hostVersion}",
						group,
						required != null && VersionComparer.VersionRelease.Compare(required, hostVersion) > 0);
			}

			return new CompatibilityResult(PackageCompatibility.Compatible, null, group);
		}
	}
}