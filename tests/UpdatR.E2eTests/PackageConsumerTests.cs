using System.IO.Compression;
using Xunit;
using static SimpleExec.Command;

namespace UpdatR.E2e;

/// <summary>
/// Both LiveTests and PackageConsumerTests spawn heavy "dotnet" subprocesses and temporarily
/// redirect the process-wide Console.Out to capture output for xunit. xunit runs different test
/// classes in parallel by default, and Console.Out is global mutable state, so without this
/// collection the two classes' captured output can get interleaved/corrupted when they run at the
/// same time (observed in CI: PackageConsumerTests' failure output contained LiveTests' Dummy.App
/// output). Putting both in the same collection forces them to run sequentially instead.
/// </summary>
[CollectionDefinition("E2E sequential", DisableParallelization = true)]
public sealed class E2eSequentialFixture;

/// <summary>
/// Packs UpdatR as a real NuGet package and consumes it from a brand new console project via a
/// plain PackageReference - the same way an actual user would. This is the only test in the repo
/// that goes through NuGet's real restore/pack graph resolution instead of a ProjectReference, so
/// it's the only place that would have caught the MSBL001 regression ("A PackageReference to the
/// package 'NuGet.Frameworks' ... without ExcludeAssets='runtime' and PrivateAssets='all' set")
/// that shipped in 6.0.0-beta.0: every other test in the solution references UpdatR.csproj via
/// ProjectReference, which never exercises NuGet's transitive-dependency/asset-exclusion logic.
/// </summary>
[Collection("E2E sequential")]
public sealed class PackageConsumerTests : IDisposable
{
    private readonly TextWriter _originalConsoleOut;
    private readonly TestOutputHelperTextWriterAdapter _outAdapter;
    private bool disposedValue;

    public PackageConsumerTests(ITestOutputHelper output)
    {
        _originalConsoleOut = Console.Out;
        _outAdapter = new TestOutputHelperTextWriterAdapter(output);

        Console.SetOut(_outAdapter);
    }

    [Fact]
    public async Task ConsumeUpdatRPackageBuildAndRunSucceedWithoutMsbl001()
    {
        var root = await GetRepoRootDirectoryAsync();

        Console.WriteLine("Root: " + root);

        var testTemp = Path.Combine(
            Path.GetTempPath(),
            "dotnet-updatr",
            "e2etests-packageconsumer"
        );

        if (Directory.Exists(testTemp))
        {
            Directory.Delete(testTemp, true);
        }

        Directory.CreateDirectory(testTemp);

        var localFeed = Path.Combine(testTemp, "feed");
        var consumerDir = Path.Combine(testTemp, "Consumer");

        Directory.CreateDirectory(localFeed);

        // Unique per test run so nothing can be served from a NuGet cache from a previous run.
        var testVersion = $"0.0.1-e2etest.{DateTime.UtcNow:yyyyMMddHHmmssfff}";

        var updatrProjectPath = Path.Combine(root.FullName, "src", "UpdatR", "UpdatR.csproj");

        await RunAsync(
            "dotnet",
            $"pack \"{updatrProjectPath}\" --configuration Release -p:PackageVersion={testVersion} -o \"{localFeed}\"",
            ct: TestContext.Current.CancellationToken
        );

        // Diagnostic, defense-in-depth check: assert the produced .nupkg actually contains
        // UpdatR.dll's bundled runtime dependency Microsoft.Build.Locator.dll alongside UpdatR.dll
        // itself. It's bundled by the CopyMicrosoftBuildLocatorToPackage target in UpdatR.csproj
        // instead of being a normal NuGet dependency (see the comments there) - if that target's
        // item-filtering ever silently stops matching (e.g. an MSBuild version/OS difference in
        // how RuntimeCopyLocalItems metadata gets populated), this fails right here at pack-time
        // instead of showing up as a confusing runtime FileNotFoundException in the consumer app
        // further down.
        var nupkgPath = Path.Combine(localFeed, $"UpdatR.{testVersion}.nupkg");

        Assert.True(File.Exists(nupkgPath), $"Expected package not found at {nupkgPath}.");

        using (var archive = ZipFile.OpenRead(nupkgPath))
        {
            var libEntries = archive
                .Entries.Where(e => e.FullName.StartsWith("lib/", StringComparison.Ordinal))
                .Select(e => e.FullName)
                .ToArray();

            Console.WriteLine("Package lib/ entries: " + string.Join(", ", libEntries));

            Assert.Contains(libEntries, e => e.EndsWith("UpdatR.dll", StringComparison.Ordinal));
            Assert.Contains(
                libEntries,
                e => e.EndsWith("Microsoft.Build.Locator.dll", StringComparison.Ordinal)
            );
        }

        await RunAsync(
            "dotnet",
            $"new console -o \"{consumerDir}\" --force",
            ct: TestContext.Current.CancellationToken
        );

        await File.WriteAllTextAsync(
            Path.Combine(consumerDir, "nuget.config"),
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
                <add key="local-updatr-feed" value="{localFeed}" />
              </packageSources>
            </configuration>
            """,
            TestContext.Current.CancellationToken
        );

        await RunAsync(
            "dotnet",
            $"add \"{consumerDir}\" package UpdatR --version {testVersion} --source \"{localFeed}\"",
            ct: TestContext.Current.CancellationToken
        );

        // Write Program.cs before building, so a single build produces a binary that both
        // proves there's no MSBL001 build error AND can immediately be executed afterwards -
        // avoids relying on `dotnet run`'s own incremental up-to-date checks re-triggering (or
        // not) a full rebuild.
        await File.WriteAllTextAsync(
            Path.Combine(consumerDir, "Program.cs"),
            $"""
            var updater = new UpdatR.Updater();
            var summary = await updater.UpdateAsync(@"{consumerDir}", dryRun: true);
            Console.WriteLine("UpdatR ran successfully. Updated packages: " + summary.UpdatedPackagesCount);
            """,
            TestContext.Current.CancellationToken
        );

        var (buildStdOutput, buildStdError) = await ReadAsync(
            "dotnet",
            "build --configuration Release",
            workingDirectory: consumerDir,
            ct: TestContext.Current.CancellationToken
        );

        Console.WriteLine("Build stdout:");
        Console.WriteLine(buildStdOutput);
        Console.WriteLine("Build stderr:");
        Console.WriteLine(buildStdError);

        // ReadAsync (SimpleExec) throws on a non-zero exit code, so a build failure (including
        // MSBuildLocator's MSBL001 hard error) already fails this test on its own. The extra
        // assertion is defense-in-depth in case the check is ever downgraded to a warning.
        Assert.DoesNotContain("MSBL001", buildStdOutput, StringComparison.OrdinalIgnoreCase);

        // Diagnostic, defense-in-depth check: assert UpdatR's bundled Microsoft.Build.Locator.dll
        // actually got copied into the consumer app's own output directory by NuGet/MSBuild
        // during the build above - not just present in the .nupkg (checked earlier). If this ever
        // fails, the break is in restore/copy-local resolution rather than in packing.
        var consumerOutputDir = Path.Combine(consumerDir, "bin", "Release", "net10.0");

        var consumerOutputFiles = Directory.Exists(consumerOutputDir)
            ? Directory.GetFiles(consumerOutputDir).Select(Path.GetFileName).ToArray()
            : [];

        Console.WriteLine(
            "Consumer output directory contents: " + string.Join(", ", consumerOutputFiles)
        );

        Assert.Contains("Microsoft.Build.Locator.dll", consumerOutputFiles);

        // Exercise the actual runtime behavior too (ModuleInitializer registering MSBuildLocator,
        // and MsBuildProjectInspector actually resolving a real MSBuild), not just the build - the
        // DLL needs to both be present (build-time check above) and loadable. Uses `dotnet exec`
        // against the binary just built, the same proven pattern LiveTests uses for the CLI,
        // instead of `dotnet run` (which would re-evaluate/rebuild the project again).
        var consumerDll = Path.Combine(consumerDir, "bin", "Release", "net10.0", "Consumer.dll");

        if (!File.Exists(consumerDll))
        {
            throw new InvalidOperationException($"Could not find built assembly at {consumerDll}.");
        }

        var (runStdOutput, runStdError) = await ReadAsync(
            "dotnet",
            $"exec \"{consumerDll}\"",
            workingDirectory: consumerDir,
            ct: TestContext.Current.CancellationToken
        );

        Console.WriteLine("Run stdout:");
        Console.WriteLine(runStdOutput);
        Console.WriteLine("Run stderr:");
        Console.WriteLine(runStdError);

        Assert.Contains("UpdatR ran successfully.", runStdOutput, StringComparison.Ordinal);
    }

    private static async Task<DirectoryInfo> GetRepoRootDirectoryAsync()
    {
        var (stdOutput, stdError) = await ReadAsync("git", "rev-parse --show-toplevel");

        return new DirectoryInfo(stdOutput.Trim());
    }

    private void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                Console.SetOut(_originalConsoleOut);

                _outAdapter.Dispose();
            }

            disposedValue = true;
        }
    }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
