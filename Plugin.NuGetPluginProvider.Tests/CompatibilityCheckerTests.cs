using System;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Versioning;
using Plugin.NuGetPluginProvider.Domain;
using Xunit;

namespace Plugin.NuGetPluginProvider.Tests
{
	public class CompatibilityCheckerTests
	{
		private static PackageDependencyGroup[] Groups(params FakePackage[] packages)
			=> packages[0].Groups.ToArray();

		[Fact]
		public void Check_SameWindowsVersion_IsCompatible()
		{
			CompatibilityChecker checker = new CompatibilityChecker(new TestHost());
			FakePackage package = new FakePackage("P", "1.0.0")
				.DependsOn("net48", ("SAL.Windows", "1.2.12"))
				.DependsOn("net8.0-windows7.0", ("SAL.Windows", "1.2.12"), ("Newtonsoft.Json", "13.0.3"));

			CompatibilityResult result = checker.Check(Groups(package));

			Assert.True(result.IsCompatible, result.Reason);
			Assert.Equal(NuGetFramework.Parse("net8.0-windows7.0"), result.Group.TargetFramework);
		}

		[Fact]
		public void Check_FourPartVersionEqualsThreePart()
		{
			CompatibilityChecker checker = new CompatibilityChecker(new TestHost(flatbed: "1.2.12.0"));
			FakePackage package = new FakePackage("P", "1.0.0").DependsOn("net8.0", ("SAL.Flatbed", "1.2.12"));

			Assert.True(checker.Check(Groups(package)).IsCompatible);
		}

		[Theory]
		[InlineData("1.2.11", false)]
		[InlineData("1.2.13", true)]
		public void Check_DifferentVersion_IsMismatch(String required, Boolean requiresNewerHost)
		{
			CompatibilityChecker checker = new CompatibilityChecker(new TestHost());
			FakePackage package = new FakePackage("P", "1.0.0").DependsOn("net8.0", ("SAL.Flatbed", required));

			CompatibilityResult result = checker.Check(Groups(package));

			Assert.Equal(PackageCompatibility.VersionMismatch, result.Status);
			Assert.Equal(requiresNewerHost, result.RequiresNewerHost);
		}

		[Fact]
		public void Check_WindowsPackageInHeadlessHost_IsHostPackageMissing()
		{
			CompatibilityChecker checker = new CompatibilityChecker(new TestHost(windows: null));
			FakePackage package = new FakePackage("P", "1.0.0").DependsOn("net8.0", ("SAL.Windows", "1.2.12"));

			Assert.Equal(PackageCompatibility.HostPackageMissing, checker.Check(Groups(package)).Status);
		}

		[Fact]
		public void Check_AllSalDependenciesMustMatch()
		{
			CompatibilityChecker checker = new CompatibilityChecker(new TestHost(windows: "1.2.11"));
			FakePackage package = new FakePackage("P", "1.0.0").DependsOn("net8.0", ("SAL.Flatbed", "1.2.12"), ("SAL.Windows", "1.2.12"));

			Assert.Equal(PackageCompatibility.VersionMismatch, checker.Check(Groups(package)).Status);
		}

		[Fact]
		public void Check_NoSalDependency_IsNotPlugin()
		{
			CompatibilityChecker checker = new CompatibilityChecker(new TestHost());
			FakePackage package = new FakePackage("P", "1.0.0").DependsOn("net8.0", ("Newtonsoft.Json", "13.0.3"));

			Assert.Equal(PackageCompatibility.NotPlugin, checker.Check(Groups(package)).Status);
			Assert.Equal(PackageCompatibility.NotPlugin, checker.Check(Array.Empty<PackageDependencyGroup>()).Status);
		}

		[Fact]
		public void Check_OnlyNet48Group_UnsupportedOnNet8()
		{
			CompatibilityChecker checker = new CompatibilityChecker(new TestHost());
			FakePackage package = new FakePackage("P", "1.0.0").DependsOn("net48", ("SAL.Windows", "1.2.12"));

			Assert.Equal(PackageCompatibility.UnsupportedFramework, checker.Check(Groups(package)).Status);
		}

		[Fact]
		public void Check_Net48Host_SelectsNet48Group()
		{
			CompatibilityChecker checker = new CompatibilityChecker(new TestHost(framework: "net48"));
			FakePackage package = new FakePackage("P", "1.0.0")
				.DependsOn("net48", ("SAL.Windows", "1.2.12"))
				.DependsOn("net8.0-windows7.0", ("SAL.Windows", "1.2.13"));

			CompatibilityResult result = checker.Check(Groups(package));

			Assert.True(result.IsCompatible, result.Reason);
			Assert.Equal(NuGetFramework.Parse("net48"), result.Group.TargetFramework);
		}

		[Fact]
		public void CreateNetFramework_WindowsHost_IsCompatibleWithWindows7Packages()
		{
			NuGetFramework host = HostEnvironment.CreateNetFramework(new Version(8, 0, 11), new Version(10, 0, 26200));
			FrameworkReducer reducer = new FrameworkReducer();

			NuGetFramework nearest = reducer.GetNearest(host, new[] { NuGetFramework.Parse("net48"), NuGetFramework.Parse("net8.0-windows7.0"), NuGetFramework.Parse("netstandard2.0") });

			Assert.Equal("net8.0-windows10.0.26200", host.GetShortFolderName());
			Assert.Equal(NuGetFramework.Parse("net8.0-windows7.0"), nearest);
		}

		[Fact]
		public void GetPackageVersion_ReadsInformationalVersion()
		{
			NuGetVersion version = HostEnvironment.GetPackageVersion(typeof(SAL.Flatbed.IPlugin).Assembly);

			Assert.Equal(1, version.Major);
		}
	}
}