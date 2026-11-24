using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Versioning;
using Plugin.NuGetPluginProvider.Data;
using Plugin.NuGetPluginProvider.Domain;
using Xunit;

namespace Plugin.NuGetPluginProvider.Tests
{
	public class PackageInstallerTests
	{
		private static InstallState Stage(String staging, params String[] files)
		{
			InstallState state = new InstallState() { Id = "P", Version = "1.0.0", };
			foreach(String file in files)
			{
				String path = Path.Combine(staging, file);
				Directory.CreateDirectory(Path.GetDirectoryName(path));
				File.WriteAllText(path, "new " + file);
				state.Files.Add(new InstalledFile() { Path = file, PackageId = "P", PackageVersion = "1.0.0", });
			}
			return state;
		}

		[Theory]
		[InlineData("lib/net48/A.dll", "A.dll")]
		[InlineData("lib/net48/de/A.resources.dll", @"de\A.resources.dll")]
		[InlineData("lib/net48/_._", null)]
		[InlineData("lib/A.dll", null)]
		public void GetRelativeLibPath(String item, String expected)
			=> Assert.Equal(expected, PackageInstaller.GetRelativeLibPath(item));

		[Fact]
		public void Apply_ReplacesChangedAndRemovesObsoleteFiles()
		{
			using(TempFolder temp = new TempFolder())
			{
				String target = temp.Combine("target", "P");
				String staging1 = temp.Combine("staging1");
				PackageInstaller.Apply(staging1, target, Stage(staging1, "A.dll", "Old.dll"));

				String staging2 = temp.Combine("staging2");
				ApplyResult result = PackageInstaller.Apply(staging2, target, Stage(staging2, "A.dll", "New.dll"));

				Assert.False(result.RestartRequired);
				Assert.True(File.Exists(Path.Combine(target, "A.dll")));
				Assert.True(File.Exists(Path.Combine(target, "New.dll")));
				Assert.False(File.Exists(Path.Combine(target, "Old.dll")));
				Assert.Equal(new[] { "A.dll", "New.dll" }, InstallState.Load(target).Files.Select(f => f.Path).ToArray());
			}
		}

		/// <summary>Copy a real assembly to the path and load it, so the file is locked like a loaded plugin</summary>
		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
		private static WeakReference LockLikeLoadedAssembly(String path)
		{
			File.Copy(typeof(NuGetVersion).Assembly.Location, path, true);
			System.Runtime.Loader.AssemblyLoadContext context = new System.Runtime.Loader.AssemblyLoadContext("lock", true);
			context.LoadFromAssemblyPath(path);
			context.Unload();//Unloaded only after GC
			return new WeakReference(context);
		}

		[Fact]
		public void Apply_LoadedAssembly_IsRenamedAndCleanedUpAfterUnlock()
		{
			using(TempFolder temp = new TempFolder())
			{
				String target = temp.Combine("target", "P");
				Directory.CreateDirectory(target);
				String staging = temp.Combine("staging");
				InstallState state = Stage(staging, "A.dll");

				WeakReference context = LockLikeLoadedAssembly(Path.Combine(target, "A.dll"));
				Assert.ThrowsAny<Exception>(() => File.Delete(Path.Combine(target, "A.dll")));

				ApplyResult result = PackageInstaller.Apply(staging, target, state);

				Assert.True(result.RestartRequired);
				Assert.Equal("new A.dll", File.ReadAllText(Path.Combine(target, "A.dll")));
				Assert.Single(Directory.GetFiles(target, "*" + PackageInstaller.RenamedExtension));

				for(Int32 loop = 0; context.IsAlive && loop < 10; loop++)
				{
					GC.Collect();
					GC.WaitForPendingFinalizers();
				}

				PackageInstaller.CleanupRenamedFiles(temp.Combine("target"));
				Assert.Empty(Directory.GetFiles(target, "*" + PackageInstaller.RenamedExtension));
				Assert.NotNull(InstallState.Load(target));
			}
		}

		[Fact]
		public void RemoveFile_LockedFile_IsRenamed()
		{
			using(TempFolder temp = new TempFolder())
			{
				String file = temp.Combine("A.dll");
				File.WriteAllText(file, "old");
				File.WriteAllText(file + PackageInstaller.RenamedExtension, "older");

				using(new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
				using(new FileStream(file + PackageInstaller.RenamedExtension, FileMode.Open, FileAccess.Read, FileShare.Read))
				{
					// Neither file can be deleted. Renaming of a file opened without FileShare.Delete fails too,
					// so the method must report the IO error instead of looping forever
					Assert.ThrowsAny<IOException>(() => PackageInstaller.RemoveFile(file));
				}

				using(new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
					Assert.False(PackageInstaller.RemoveFile(file));//FileShare.Delete allows deletion

				Assert.False(File.Exists(file));
			}
		}

		[Fact]
		public void Uninstall_RemovesOnlyInstalledFiles()
		{
			using(TempFolder temp = new TempFolder())
			{
				String target = temp.Combine("P");
				String staging = temp.Combine("staging");
				PackageInstaller.Apply(staging, target, Stage(staging, "A.dll", @"sub\B.dll"));
				File.WriteAllText(Path.Combine(target, "user.txt"), "keep");

				ApplyResult result = PackageInstaller.Uninstall(target);

				Assert.False(result.RestartRequired);
				Assert.Equal(new[] { Path.Combine(target, "user.txt") }, Directory.GetFiles(target, "*", SearchOption.AllDirectories));
				Assert.False(Directory.Exists(Path.Combine(target, "sub")));
			}
		}

		[Fact]
		public async Task InstallAsync_ExtractsPluginAndDependenciesForHostFramework()
		{
			FakeFeed feed = new FakeFeed();
			feed.Add(new FakePackage("Plugin.A", "2.0.0")
				.DependsOn("net48", ("SAL.Windows", "1.2.12"), ("Lib.B", "1.0.0"))
				.DependsOn("net8.0-windows7.0", ("SAL.Windows", "1.2.12"), ("Lib.B", "1.0.0"))
				.WithFile("lib/net48/Plugin.A.dll", "net48")
				.WithFile("lib/net8.0-windows7.0/Plugin.A.dll", "net8")
				.WithFile("lib/net8.0-windows7.0/Plugin.A.xml")
				.WithFile("runtimes/win-x64/native/x.dll"));
			feed.Add(new FakePackage("Lib.B", "1.0.0").WithFile("lib/netstandard2.0/Lib.B.dll"));
			feed.Add(new FakePackage("Lib.B", "1.1.0").WithFile("lib/netstandard2.0/Lib.B.dll"));
			FakeTrace trace = new FakeTrace();

			using(TempFolder temp = new TempFolder())
			{
				PackageInstaller installer = new PackageInstaller(new TestHost(), new[] { feed }, temp.Combine("staging"), trace);
				String target = temp.Combine("plugins", "Plugin.A");

				Tuple<ApplyResult, InstallState> result = await installer.InstallAsync("Plugin.A", NuGetVersion.Parse("2.0.0"), target, CancellationToken.None);

				Assert.Equal("net8", File.ReadAllText(Path.Combine(target, "Plugin.A.dll")));
				Assert.True(File.Exists(Path.Combine(target, "Plugin.A.xml")));
				Assert.True(File.Exists(Path.Combine(target, "Lib.B.dll")));
				Assert.False(Directory.Exists(Path.Combine(target, "runtimes")));
				Assert.Equal("1.0.0", result.Item2.Files.Single(f => f.Path == "Lib.B.dll").PackageVersion);//Lowest applicable
				Assert.Equal("net8.0-windows7.0", InstallState.Load(target).Framework);
				Assert.False(Directory.Exists(temp.Combine("staging", "Plugin.A.2.0.0")));
				Assert.Contains(trace.Messages, m => m.Contains("native runtime assets"));
			}
		}

		[Fact]
		public async Task InstallAsync_NoLibForHost_Throws()
		{
			FakeFeed feed = new FakeFeed();
			feed.Add(new FakePackage("Plugin.A", "1.0.0").DependsOn("net48", ("SAL.Windows", "1.2.12")).WithFile("lib/net48/Plugin.A.dll"));

			using(TempFolder temp = new TempFolder())
			{
				PackageInstaller installer = new PackageInstaller(new TestHost(), new[] { feed }, temp.Combine("staging"), new FakeTrace());

				await Assert.ThrowsAsync<InvalidOperationException>(() => installer.InstallAsync("Plugin.A", NuGetVersion.Parse("1.0.0"), temp.Combine("Plugin.A"), CancellationToken.None));
				Assert.Null(InstallState.Load(temp.Combine("Plugin.A")));
			}
		}
	}
}