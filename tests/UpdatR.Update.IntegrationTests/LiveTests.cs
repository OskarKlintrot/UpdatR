using static SimpleExec.Command;

namespace UpdatR.Update.IntegrationTests;

[UsesVerify]
public class LiveTests
{
    [Fact]
    public async Task UpdateDummyProject()
    {
        var runsOnGitHubActions = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));

        var root = await GetRepoRootDirectoryAsync();

        Console.WriteLine("Root: " + root);

        var dummyProjectSrc = Path.Combine(root.FullName, "tests", "UpdatR.Update.IntegrationTests", "Dummy");

        if (!Directory.Exists(dummyProjectSrc))
        {
            throw new InvalidOperationException($"Path {dummyProjectSrc} does not exist.");
        }

        var testTemp = Path.Combine(Path.GetTempPath(), "dotnet-updatr", "integrationtests");

        var dummyProject = Path.Combine(testTemp, "Dummy");

        var log = Path.Combine(testTemp, "output.md");

        if (Directory.Exists(dummyProject))
        {
            Directory.Delete(dummyProject, true);
        }

        Directory.CreateDirectory(dummyProject);

        CopyDirectory(dummyProjectSrc, dummyProject, recursive: true);

        var cliProjectPath = Path.Combine(root.FullName, "src", "UpdatR.Update.Cli");

        if (!runsOnGitHubActions)
        {
            await RunAsync(
                "dotnet",
                "build --configuration Release",
                workingDirectory: cliProjectPath);
        }

        var cli = Path.Combine(cliProjectPath, "bin", "Release", "net6.0", "UpdatR.Update.Cli.dll");

        await RunAsync(
            "dotnet",
            $"exec {cli} --output {log}",
            workingDirectory: dummyProject);

        await Verify(GetVerifyObjects());

        async IAsyncEnumerable<string> GetVerifyObjects()
        {
            yield return await File.ReadAllTextAsync(log)!;
            yield return await File.ReadAllTextAsync(Path.Combine(dummyProjectSrc, "Dummy.sln"))!;
            yield return await File.ReadAllTextAsync(Path.Combine(dummyProjectSrc, "Dummy.App", "Dummy.App.csproj"))!;
            yield return await File.ReadAllTextAsync(Path.Combine(dummyProjectSrc, "nuget.config"))!;
            yield return await File.ReadAllTextAsync(Path.Combine(dummyProject, "Dummy.sln"))!;
            yield return await File.ReadAllTextAsync(Path.Combine(dummyProject, "Dummy.App", "Dummy.App.csproj"))!;
            yield return await File.ReadAllTextAsync(Path.Combine(dummyProject, "nuget.config"))!;
        }
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
}
