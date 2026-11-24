using System;
using System.IO;
using System.Linq;
using Plugin.NuGetPluginProvider.Data;
using Xunit;

namespace Plugin.NuGetPluginProvider.Tests
{
	public class NuGetManifestTests
	{
		[Fact]
		public void Load_MissingFile_ReturnsDefault()
		{
			using(TempFolder temp = new TempFolder())
			{
				NuGetManifest manifest = NuGetManifest.Load(temp.Combine(NuGetManifest.FileName), out Boolean created);

				Assert.True(created);
				Assert.Equal(2, manifest.Feeds.Count);
				Assert.True(manifest.Feeds[0].Enabled);
				Assert.False(manifest.Feeds[1].Enabled);
				Assert.Single(manifest.GetEnabledFeeds());
				Assert.Equal(temp.Path, manifest.GetTargetRoot());
			}
		}

		[Fact]
		public void SaveAndLoad_RoundTrip_DoesNotDuplicateDefaultFeeds()
		{
			using(TempFolder temp = new TempFolder())
			{
				NuGetManifest manifest = NuGetManifest.CreateDefault(temp.Combine(NuGetManifest.FileName));
				manifest.Packages.Add(new PackageRequest() { Id = "AlphaOmega.SAL.Plugin.Timers", Version = "1.0.5", });
				manifest.ResolveOnDemand = false;
				manifest.Save();

				NuGetManifest loaded = NuGetManifest.Load(manifest.FilePath, out Boolean created);

				Assert.False(created);
				Assert.Equal(2, loaded.Feeds.Count);
				Assert.False(loaded.ResolveOnDemand);
				Assert.Equal("1.0.5", loaded.FindPackage("alphaomega.sal.plugin.timers").Version);
			}
		}

		[Fact]
		public void Parse_PartialJson_KeepsDefaultsAndDropsInvalidEntries()
		{
			NuGetManifest manifest = NuGetManifest.Parse("{ \"Packages\": [ { \"Id\": \"A\" }, { \"Version\": \"1.0\" }, null ], \"ResolveTimeoutSeconds\": 0 }", @"C:\x\Plugins.NuGet.json");

			Assert.Single(manifest.Packages);
			Assert.Empty(manifest.Feeds);
			Assert.True(manifest.CheckForUpdates);
			Assert.Equal(30, manifest.ResolveTimeoutSeconds);
			Assert.Equal("AlphaOmega.SAL.Plugin", manifest.SearchTerm);
		}

		[Fact]
		public void GetPackageFolder_RelativeAndPackageTargetPath()
		{
			NuGetManifest manifest = NuGetManifest.Parse("{ \"TargetPath\": \"NuGet\" }", @"C:\host\Plugins.NuGet.json");
			PackageRequest defaultRequest = new PackageRequest() { Id = "A", };
			PackageRequest customRequest = new PackageRequest() { Id = "B", TargetPath = @"D:\plugins", };
			manifest.Packages.Add(defaultRequest);
			manifest.Packages.Add(customRequest);

			Assert.Equal(@"C:\host\NuGet\A", manifest.GetPackageFolder(defaultRequest));
			Assert.Equal(@"D:\plugins\B", manifest.GetPackageFolder(customRequest));
			Assert.Equal(new[] { @"C:\host\NuGet", @"D:\plugins" }, manifest.GetTargetRoots().ToArray());
		}

		[Fact]
		public void FeedInfo_ExpandsEnvironmentVariables()
		{
			String variable = "SAL_NUGET_TEST_" + Guid.NewGuid().ToString("N");
			Environment.SetEnvironmentVariable(variable, "secret");
			try
			{
				FeedInfo feed = new FeedInfo() { Password = "%" + variable + "%", UserName = "%SAL_NUGET_UNDEFINED_VARIABLE%", };

				Assert.Equal("secret", feed.GetPassword());
				Assert.Null(feed.GetUserName());
				Assert.False(feed.IsPlainTextSecret);
				Assert.True(new FeedInfo() { Password = "ghp_plain", }.IsPlainTextSecret);
			} finally
			{
				Environment.SetEnvironmentVariable(variable, null);
			}
		}

		[Fact]
		public void InstallState_CorruptedFile_IsTreatedAsMissing()
		{
			using(TempFolder temp = new TempFolder())
			{
				File.WriteAllText(temp.Combine(InstallState.FileName), "{ not json");
				Assert.Null(InstallState.Load(temp.Path));

				new InstallState() { Id = "A", Version = "1.0.0", }.Save(temp.Path);
				Assert.Equal("1.0.0", InstallState.Load(temp.Path).GetVersion().ToNormalizedString());
			}
		}
	}
}