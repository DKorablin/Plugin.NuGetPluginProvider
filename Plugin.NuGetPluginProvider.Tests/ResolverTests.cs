using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Versioning;
using Plugin.NuGetPluginProvider.Data;
using Plugin.NuGetPluginProvider.Domain;
using Xunit;

namespace Plugin.NuGetPluginProvider.Tests
{
	public class DependencyResolverTests
	{
		[Fact]
		public async Task ResolveAsync_SelectsLowestVersionSatisfyingAllRanges()
		{
			FakeFeed feed = new FakeFeed();
			feed.Add(new FakePackage("Plugin.A", "1.0.0").DependsOn("net8.0", ("SAL.Flatbed", "1.2.12"), ("Lib.B", "1.0.0"), ("Lib.C", "1.0.0")));
			feed.Add(new FakePackage("Lib.B", "1.0.0"));
			feed.Add(new FakePackage("Lib.B", "1.5.0"));
			feed.Add(new FakePackage("Lib.B", "2.0.0"));
			feed.Add(new FakePackage("Lib.C", "1.0.0").DependsOn("netstandard2.0", ("Lib.B", "1.5.0")));

			IReadOnlyList<ResolvedPackage> result = await new DependencyResolver(new TestHost(), new[] { feed }).ResolveAsync("Plugin.A", NuGetVersion.Parse("1.0.0"), CancellationToken.None);

			Assert.Equal(new[] { "Plugin.A 1.0.0", "Lib.B 1.5.0", "Lib.C 1.0.0" }, result.Select(r => r.ToString()).ToArray());
		}

		[Fact]
		public async Task ResolveAsync_DropsDependenciesOfReplacedVersions()
		{
			FakeFeed feed = new FakeFeed();
			feed.Add(new FakePackage("Plugin.A", "1.0.0").DependsOn("net8.0", ("Lib.B", "1.0.0"), ("Lib.C", "1.0.0")));
			feed.Add(new FakePackage("Lib.B", "1.0.0").DependsOn("net8.0", ("Lib.Old", "1.0.0")));
			feed.Add(new FakePackage("Lib.B", "2.0.0"));
			feed.Add(new FakePackage("Lib.Old", "1.0.0"));
			feed.Add(new FakePackage("Lib.C", "1.0.0").DependsOn("net8.0", ("Lib.B", "2.0.0")));

			IReadOnlyList<ResolvedPackage> result = await new DependencyResolver(new TestHost(), new[] { feed }).ResolveAsync("Plugin.A", NuGetVersion.Parse("1.0.0"), CancellationToken.None);

			Assert.Equal(new[] { "Plugin.A 1.0.0", "Lib.B 2.0.0", "Lib.C 1.0.0" }, result.Select(r => r.ToString()).ToArray());
		}

		[Fact]
		public async Task ResolveAsync_UsesFirstFeedThatHasTheVersion()
		{
			FakeFeed github = new FakeFeed("github");
			FakeFeed nuget = new FakeFeed("nuget");
			github.Add(new FakePackage("Plugin.A", "1.0.0").DependsOn("net8.0", ("Lib.B", "1.0.0")));
			nuget.Add(new FakePackage("Lib.B", "1.0.0"));

			IReadOnlyList<ResolvedPackage> result = await new DependencyResolver(new TestHost(), new[] { github, nuget }).ResolveAsync("Plugin.A", NuGetVersion.Parse("1.0.0"), CancellationToken.None);

			Assert.Same(github, result[0].Feed);
			Assert.Same(nuget, result[1].Feed);
		}

		[Fact]
		public async Task ResolveAsync_MissingDependency_Throws()
		{
			FakeFeed feed = new FakeFeed();
			feed.Add(new FakePackage("Plugin.A", "1.0.0").DependsOn("net8.0", ("Lib.Missing", "1.0.0")));

			InvalidOperationException exc = await Assert.ThrowsAsync<InvalidOperationException>(
				() => new DependencyResolver(new TestHost(), new[] { feed }).ResolveAsync("Plugin.A", NuGetVersion.Parse("1.0.0"), CancellationToken.None));
			Assert.Contains("Lib.Missing", exc.Message);
		}
	}

	public class UpdateCheckerTests
	{
		private static UpdateChecker CreateChecker(FakeFeed feed, FakeTrace trace = null)
			=> new UpdateChecker(new CompatibilityChecker(new TestHost()), new[] { feed }, trace ?? new FakeTrace());

		private static FakeFeed CreateFeed()
		{
			FakeFeed feed = new FakeFeed();
			feed.Add(new FakePackage("AlphaOmega.SAL.Plugin.A", "1.0.0").DependsOn("net8.0", ("SAL.Flatbed", "1.2.12")));
			feed.Add(new FakePackage("AlphaOmega.SAL.Plugin.A", "1.1.0").DependsOn("net8.0", ("SAL.Flatbed", "1.2.12")));
			feed.Add(new FakePackage("AlphaOmega.SAL.Plugin.A", "1.2.0").DependsOn("net8.0", ("SAL.Flatbed", "1.2.13")));
			feed.Add(new FakePackage("AlphaOmega.SAL.Plugin.B", "3.0.0").DependsOn("net8.0", ("SAL.Windows", "1.2.12")));
			feed.Add(new FakePackage("AlphaOmega.SAL.Plugin.Old", "1.0.0").DependsOn("net8.0", ("SAL.Windows", "1.2.10")));
			feed.Add(new FakePackage("AlphaOmega.SAL.Library", "1.0.0").DependsOn("net8.0", ("Newtonsoft.Json", "13.0.1")));
			feed.SearchResult.AddRange(new[] { "AlphaOmega.SAL.Plugin.A", "AlphaOmega.SAL.Plugin.B", "AlphaOmega.SAL.Plugin.Old", "AlphaOmega.SAL.Library", "Other.Package" });
			return feed;
		}

		[Fact]
		public async Task FindLatestCompatibleAsync_IgnoresVersionsForOtherHosts()
		{
			LatestVersionResult result = await CreateChecker(CreateFeed()).FindLatestCompatibleAsync("AlphaOmega.SAL.Plugin.A", CancellationToken.None);

			Assert.Equal("1.1.0", result.Compatible.Version.ToNormalizedString());
			Assert.Equal("1.2.0", result.Latest.Version.ToNormalizedString());
			Assert.True(result.LatestCompatibility.RequiresNewerHost);
		}

		[Fact]
		public async Task CheckForUpdatesAsync_ReportsNewerCompatibleVersion()
		{
			UpdateChecker checker = CreateChecker(CreateFeed());
			InstallState[] installed =
			{
				new InstallState() { Id = "AlphaOmega.SAL.Plugin.A", Version = "1.0.0", },
				new InstallState() { Id = "AlphaOmega.SAL.Plugin.B", Version = "3.0.0", },
			};

			IReadOnlyList<PackageUpdateInfo> updates = await checker.CheckForUpdatesAsync(installed, CancellationToken.None);

			PackageUpdateInfo update = Assert.Single(updates);
			Assert.Equal("AlphaOmega.SAL.Plugin.A 1.0.0 -> 1.1.0", update.ToString());
		}

		[Fact]
		public async Task FindAvailablePluginsAsync_ReturnsOnlyCompatiblePluginsMatchingTheTerm()
		{
			IReadOnlyList<PackageUpdateInfo> plugins = await CreateChecker(CreateFeed()).FindAvailablePluginsAsync("AlphaOmega.SAL", new HashSet<String>(new[] { "alphaomega.sal.plugin.b" }, StringComparer.OrdinalIgnoreCase), CancellationToken.None);

			Assert.Equal(new[] { "AlphaOmega.SAL.Plugin.A 1.1.0" }, plugins.Select(p => p.ToString()).ToArray());
		}
	}

	public class OnDemandResolverTests
	{
		[Theory]
		[InlineData("Newtonsoft.Json, Version=13.0.0.0, Culture=neutral, PublicKeyToken=30ad4fe6b2a6aeed", false)]
		[InlineData("Plugin.Timers.resources, Version=1.0.0.0, Culture=de, PublicKeyToken=null", true)]
		[InlineData("Plugin.Timers.resources, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null", true)]
		[InlineData("Plugin.Timers.XmlSerializers, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null", true)]
		[InlineData("mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089", true)]
		[InlineData("SAL.Windows, Version=1.2.12.0, Culture=neutral, PublicKeyToken=null", true)]
		[InlineData("NuGet.Protocol, Version=7.9.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35", true)]
		[InlineData("System.Private.CoreLib", true)]
		public void ShouldSkip(String name, Boolean expected)
			=> Assert.Equal(expected, OnDemandResolver.ShouldSkip(new AssemblyName(name)));

		[Fact]
		public void SelectCandidates_SameMajorStableFromHighest()
		{
			NuGetVersion[] versions = new[] { "4.3.0", "4.5.0", "4.5.5", "4.6.0-preview1", "5.0.0", "3.9.0" }.Select(NuGetVersion.Parse).ToArray();

			String[] result = OnDemandResolver.SelectCandidates(new Version(4, 0, 1, 2), versions).Select(v => v.ToNormalizedString()).ToArray();

			Assert.Equal(new[] { "4.5.5", "4.5.0", "4.3.0" }, result);
		}

		[Theory]
		[InlineData("13.0.0.0", "13.0.0.0", true)]
		[InlineData("13.0.0.0", "13.0.1.0", true)]
		[InlineData("13.0.1.0", "13.0.0.0", false)]
		[InlineData("12.0.0.0", "13.0.0.0", false)]
		public void IsCompatibleVersion(String requested, String candidate, Boolean expected)
			=> Assert.Equal(expected, AssemblyIndex.IsCompatibleVersion(Version.Parse(requested), Version.Parse(candidate)));

		[Fact]
		public void AssemblyIndex_FindsExactAndCompatibleAssemblies()
		{
			using(TempFolder temp = new TempFolder())
			{
				String source = typeof(NuGetVersion).Assembly.Location;
				Directory.CreateDirectory(temp.Combine("pkg"));
				File.Copy(source, temp.Combine("pkg", Path.GetFileName(source)));
				File.WriteAllText(temp.Combine("pkg", "native.dll"), "not an assembly");

				AssemblyIndex index = new AssemblyIndex();
				index.AddFolder(temp.Path);
				AssemblyName actual = typeof(NuGetVersion).Assembly.GetName();
				AssemblyName older = new AssemblyName(actual.FullName) { Version = new Version(actual.Version.Major, 0, 0, 0) };

				Assert.Equal(1, index.Count);
				Assert.NotNull(index.FindExact(actual));
				Assert.Null(index.FindExact(older));
				Assert.NotNull(index.FindCompatible(older));
			}
		}

		[Fact]
		public void TryResolve_DownloadsPackageWithAssembly()
		{
			// The fake package contains a real assembly so its version can be probed
			Assembly assembly = typeof(Assert).Assembly;
			AssemblyName name = assembly.GetName();
			FakeFeed feed = new FakeFeed();
			feed.Add(new FakePackage(name.Name, $"{name.Version.Major}.{name.Version.Minor}.{name.Version.Build + 1}").WithFile("lib/net8.0/other.dll"));//Probed first, does not contain the assembly
			feed.Add(new FakePackage(name.Name, name.Version.ToString(3)))
				.Files["lib/net8.0/" + Path.GetFileName(assembly.Location)] = File.ReadAllBytes(assembly.Location);

			using(TempFolder temp = new TempFolder())
			{
				FakeTrace trace = new FakeTrace();
				PackageInstaller installer = new PackageInstaller(new TestHost(), new[] { feed }, temp.Combine("staging"), trace);
				AssemblyIndex index = new AssemblyIndex();
				OnDemandResolver resolver = new OnDemandResolver(new TestHost(), new[] { feed }, installer, temp.Combine("resolved"), TimeSpan.FromSeconds(30), index, trace);

				String path = resolver.TryResolve(name);

				Assert.NotNull(path);
				Assert.StartsWith(temp.Combine("resolved", name.Name), path);
				Assert.Equal(name.FullName, AssemblyName.GetAssemblyName(path).FullName);
				Assert.Equal(path, index.FindExact(name));
				Assert.False(resolver.HasFailed(name));
			}
		}

		[Theory]
		[InlineData("Lib, Version=1.0.0.0, PublicKeyToken=30ad4fe6b2a6aeed", "Lib, Version=1.2.0.0, PublicKeyToken=30ad4fe6b2a6aeed", true)]
		[InlineData("Lib, Version=1.0.0.0, PublicKeyToken=30ad4fe6b2a6aeed", "Lib, Version=1.0.0.0, PublicKeyToken=null", false)]
		[InlineData("Lib, Version=1.0.0.0, PublicKeyToken=30ad4fe6b2a6aeed", "Lib, Version=1.0.0.0, PublicKeyToken=b77a5c561934e089", false)]
		[InlineData("Lib, Version=1.0.0.0, PublicKeyToken=null", "Lib, Version=1.0.0.0, PublicKeyToken=null", true)]
		[InlineData("Lib, Version=2.0.0.0, PublicKeyToken=null", "Lib, Version=1.9.0.0, PublicKeyToken=null", false)]
		public void IsMatch_RequiresSamePublicKeyToken(String requested, String candidate, Boolean expected)
			=> Assert.Equal(expected, OnDemandResolver.IsMatch(new AssemblyName(requested), new AssemblyName(candidate)));

		[Fact]
		public void TryResolve_UnsignedAssembly_IsNotDownloadedByDefault()
		{
			FakeFeed feed = new FakeFeed();
			feed.Add(new FakePackage("Unsigned.Library", "1.0.0").WithFile("lib/net8.0/Unsigned.Library.dll"));
			using(TempFolder temp = new TempFolder())
			{
				FakeTrace trace = new FakeTrace();
				PackageInstaller installer = new PackageInstaller(new TestHost(), new[] { feed }, temp.Combine("staging"), trace);
				OnDemandResolver resolver = new OnDemandResolver(new TestHost(), new[] { feed }, installer, temp.Combine("resolved"), TimeSpan.FromSeconds(30), new AssemblyIndex(), trace);

				Assert.Null(resolver.TryResolve(new AssemblyName("Unsigned.Library, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null")));
				Assert.Equal(0, feed.DownloadCount);
			}
		}

		[Fact]
		public void TryResolve_UnknownAssembly_IsRememberedAsFailed()
		{
			FakeFeed feed = new FakeFeed();
			using(TempFolder temp = new TempFolder())
			{
				FakeTrace trace = new FakeTrace();
				PackageInstaller installer = new PackageInstaller(new TestHost(), new[] { feed }, temp.Combine("staging"), trace);
				OnDemandResolver resolver = new OnDemandResolver(new TestHost(), new[] { feed }, installer, temp.Combine("resolved"), TimeSpan.FromSeconds(30), new AssemblyIndex(), trace);
				AssemblyName name = new AssemblyName("Unknown.Library, Version=1.0.0.0, Culture=neutral, PublicKeyToken=30ad4fe6b2a6aeed");

				Assert.Null(resolver.TryResolve(name));
				Assert.True(resolver.HasFailed(name));
			}
		}
	}
}