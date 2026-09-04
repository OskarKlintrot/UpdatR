using System.Text.Json;
using static SimpleExec.Command;

namespace UpdatR.E2e;

[Collection("E2E sequential")]
public sealed class LiveTests : IDisposable
{
    private readonly TextWriter _originalConsoleOut;
    private readonly TestOutputHelperTextWriterAdapter _outAdapter;
    private bool disposedValue;

    public LiveTests(ITestOutputHelper output)
    {
        _originalConsoleOut = Console.Out;
        _outAdapter = new TestOutputHelperTextWriterAdapter(output);

        Console.SetOut(_outAdapter);
    }

    [Fact]
    public async Task UpdateDummyProject()
    {
        var runsOnGitHubActions = !string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable("GITHUB_ACTIONS")
        );

        var root = await GetRepoRootDirectoryAsync();

        Console.WriteLine("Root: " + root);

        var dummyProjectSrc = Path.Combine(root.FullName, "tests", "UpdatR.E2eTests", "Dummy");

        if (!Directory.Exists(dummyProjectSrc))
        {
            throw new InvalidOperationException($"Path {dummyProjectSrc} does not exist.");
        }

        var testTemp = Path.Combine(Path.GetTempPath(), "dotnet-updatr", "e2etests");

        var dummyProject = Path.Combine(testTemp, "Dummy");

        var log = Path.Combine(testTemp, "output.md");
        var title = Path.Combine(testTemp, "title.md");
        var description = Path.Combine(testTemp, "description.md");

        if (Directory.Exists(dummyProject))
        {
            Directory.Delete(dummyProject, true);
        }

        Directory.CreateDirectory(dummyProject);

        CopyDirectory(dummyProjectSrc, dummyProject, recursive: true);

        var cli = await BuildAndGetCliPathAsync(root, runsOnGitHubActions);

        var (stdOutput, stdError) = await ReadAsync(
            "dotnet",
            $"exec {cli} --output-path {log} --title {title} --description {description}",
            workingDirectory: dummyProject,
            ct: TestContext.Current.CancellationToken
        );

        Console.WriteLine("CLI stdout:");
        Console.WriteLine(stdOutput);
        Console.WriteLine("CLI stderr:");
        Console.WriteLine(stdError);

        if (!File.Exists(log))
        {
            var crashLog = Path.Combine(Path.GetTempPath(), "dotnet-updatr-crash.log");
            var crashLogContent = File.Exists(crashLog)
                ? await File.ReadAllTextAsync(crashLog, TestContext.Current.CancellationToken)
                : "(no crash log found)";

            throw new InvalidOperationException(
                $"CLI did not produce {log}. Stdout: {stdOutput} Stderr: {stdError} Crash log: {crashLogContent}"
            );
        }

        await Verify(GetVerifyObjects());

        async IAsyncEnumerable<string> GetVerifyObjects()
        {
            yield return await File.ReadAllTextAsync(log)!;
            yield return await File.ReadAllTextAsync(title)!;
            yield return await File.ReadAllTextAsync(description)!;
            yield return await File.ReadAllTextAsync(Path.Combine(dummyProjectSrc, "Dummy.sln"))!;
            yield return await File.ReadAllTextAsync(
                Path.Combine(dummyProjectSrc, "Dummy.App", "Dummy.App.csproj")
            )!;
            yield return await File.ReadAllTextAsync(
                Path.Combine(dummyProjectSrc, "nuget.config")
            )!;
            yield return await File.ReadAllTextAsync(Path.Combine(dummyProject, "Dummy.sln"))!;
            yield return await File.ReadAllTextAsync(
                Path.Combine(dummyProject, "Dummy.App", "Dummy.App.csproj")
            )!;
            yield return await File.ReadAllTextAsync(Path.Combine(dummyProject, "nuget.config"))!;
        }
    }

    /// <summary>
    /// Verifies <c>--output json</c>: stdout must contain nothing but the JSON summary - no log
    /// lines, no color codes, no plain-text summary - so it can be piped straight into another
    /// program or parsed by an agent, while diagnostics still show up on stderr.
    /// </summary>
    [Fact]
    public async Task UpdateDummyProjectOutputJson()
    {
        var runsOnGitHubActions = !string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable("GITHUB_ACTIONS")
        );

        var root = await GetRepoRootDirectoryAsync();

        Console.WriteLine("Root: " + root);

        var dummyProjectSrc = Path.Combine(root.FullName, "tests", "UpdatR.E2eTests", "Dummy");

        if (!Directory.Exists(dummyProjectSrc))
        {
            throw new InvalidOperationException($"Path {dummyProjectSrc} does not exist.");
        }

        var testTemp = Path.Combine(Path.GetTempPath(), "dotnet-updatr", "e2etests");

        var dummyProject = Path.Combine(testTemp, "DummyJson");

        if (Directory.Exists(dummyProject))
        {
            Directory.Delete(dummyProject, true);
        }

        Directory.CreateDirectory(dummyProject);

        CopyDirectory(dummyProjectSrc, dummyProject, recursive: true);

        var cli = await BuildAndGetCliPathAsync(root, runsOnGitHubActions);

        // --dry-run so the mutated Dummy project from UpdateDummyProject can't leak in via a
        // shared NuGet http-cache race, and --verbosity Information so there's plenty of log
        // output that would show up on stdout if it were misrouted there instead of stderr.
        var (stdOutput, stdError) = await ReadAsync(
            "dotnet",
            $"exec {cli} --dry-run --output json --verbosity Information",
            workingDirectory: dummyProject,
            ct: TestContext.Current.CancellationToken
        );

        Console.WriteLine("CLI stdout:");
        Console.WriteLine(stdOutput);
        Console.WriteLine("CLI stderr:");
        Console.WriteLine(stdError);

        // Must be valid, parseable JSON with nothing else mixed in - no leading/trailing log
        // lines, no ANSI color codes, no plain-text summary banner.
        using var document = JsonDocument.Parse(stdOutput);

        Assert.True(document.RootElement.TryGetProperty("schemaVersion", out _));
        Assert.True(document.RootElement.TryGetProperty("updatedPackagesCount", out _));

        // Logging is only enabled by --verbosity Information, so finding a log line on stderr
        // (and not stdout) confirms it was actually redirected rather than merely absent.
        Assert.DoesNotContain("info:", stdOutput, StringComparison.Ordinal);
        Assert.Contains("info:", stdError, StringComparison.Ordinal);
    }

    private static async Task<string> BuildAndGetCliPathAsync(
        DirectoryInfo root,
        bool runsOnGitHubActions
    )
    {
        var cliProjectPath = Path.Combine(root.FullName, "src", "dotnet-updatr");

        if (!runsOnGitHubActions)
        {
            await RunAsync(
                "dotnet",
                "build --configuration Release",
                workingDirectory: cliProjectPath,
                ct: TestContext.Current.CancellationToken
            );
        }

        var cli = Path.Combine(cliProjectPath, "bin", "Release", "net10.0", "dotnet-updatr.dll");

        if (!File.Exists(cli))
        {
            throw new InvalidOperationException($"Could not find CLI assembly at {cli}.");
        }

        return cli;
    }

    private static async Task<DirectoryInfo> GetRepoRootDirectoryAsync()
    {
        var (stdOutput, stdError) = await ReadAsync("git", "rev-parse --show-toplevel");

        return new DirectoryInfo(stdOutput.Trim());
    }

    private static void CopyDirectory(string sourceDir, string destinationDir, bool recursive)
    {
        var dir = new DirectoryInfo(sourceDir);

        if (!dir.Exists)
        {
            throw new DirectoryNotFoundException($"Source directory not found: {dir.FullName}");
        }

        // Cache directories before we start copying
        DirectoryInfo[] dirs = dir.GetDirectories();

        Directory.CreateDirectory(destinationDir);

        // Get the files in the source directory and copy to the destination directory
        foreach (FileInfo file in dir.GetFiles())
        {
            string targetFilePath = Path.Combine(destinationDir, file.Name);
            file.CopyTo(targetFilePath);
        }

        // If recursive and copying subdirectories, recursively call this method
        if (recursive)
        {
            foreach (DirectoryInfo subDir in dirs)
            {
                string newDestinationDir = Path.Combine(destinationDir, subDir.Name);

                CopyDirectory(subDir.FullName, newDestinationDir, true);
            }
        }
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
