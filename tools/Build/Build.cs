using System.Reflection;
using NuGet.Versioning;
using static Bullseye.Targets;
using static SimpleExec.Command;

var msbuild = Assembly
    .GetEntryAssembly()!
    .GetCustomAttribute<MsBuildConfigurationAttribute>()!;

var rootDir = await GetRepoRootDirectoryAsync();

Directory.SetCurrentDirectory(rootDir.FullName);

var artifactsDir = Path.GetFullPath("Artifacts");
var logsDir = Path.Combine(artifactsDir, "logs");
var buildLogFile = Path.Combine(logsDir, "build.binlog");

var solutionFile = Path.Combine(rootDir.FullName, "src", "Update.sln");
var testProject = Path.Combine(rootDir.FullName, "tests", "Update.Tests", "Update.Tests.csproj");

Target("artifactDirectories", () =>
{
    Directory.CreateDirectory(artifactsDir);
    Directory.CreateDirectory(logsDir);
});

Target("build", DependsOn("artifactDirectories"), async () =>
{
    var version = await GetVersionAsync();

    await RunAsync("dotnet",
        $"build --configuration Release /p:Version=\"{version}\" /bl:\"{buildLogFile}\" \"{solutionFile}\"");
});

Target("test", DependsOn("build"), () =>
{
    Run("dotnet",
        $"test --configuration Release --no-build \"{testProject}\"");
});

Target("default", DependsOn("test"));

await RunTargetsAndExitAsync(args);

static async Task<NuGetVersion> GetVersionAsync()
{
    var (stdOutput, stdError) = await ReadAsync("git", "describe --abbrev=0 --tags");

    return NuGetVersion.TryParse(stdOutput[1..], out var version)
        ? version
        : throw new InvalidOperationException("Invalid version.");
}

static async Task<DirectoryInfo> GetRepoRootDirectoryAsync()
{
    var (stdOutput, stdError) = await ReadAsync("git", "rev-parse --show-toplevel");

    return new DirectoryInfo(stdOutput.Trim());
}

[AttributeUsage(AttributeTargets.Assembly, Inherited = false, AllowMultiple = false)]
internal sealed class MsBuildConfigurationAttribute : Attribute
{
    public string ProjectDir { get; }
    public string Configuration { get; }

    public MsBuildConfigurationAttribute(string projectDir, string configuration)
    {
        ProjectDir = projectDir;
        Configuration = configuration;
    }
}