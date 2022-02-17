using System.Text.RegularExpressions;
using BuildingBlocks;
using NuGet.Configuration;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using Octokit;
using Octokit.Internal;
using static Bullseye.Targets;
using static SimpleExec.Command;

var runsOnGitHubActions = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));

if (runsOnGitHubActions)
{
    Console.WriteLine("Running on GitHub Actions.");
}

var rootDir = await GetRepoRootDirectoryAsync();

Directory.SetCurrentDirectory(rootDir.FullName);

var buildToolDir = Path.GetFullPath(Path.Combine("tools", "Build"));
var artifactsDir = Path.GetFullPath("Artifacts");
var logsDir = Path.Combine(artifactsDir, "logs");
var buildLogFile = Path.Combine(logsDir, "build.binlog");

var solutionFile = Path.Combine(rootDir.FullName, "UpdatR.sln");

Target("artifactDirectories", () =>
{
    Directory.CreateDirectory(artifactsDir);
    Directory.CreateDirectory(logsDir);
});

Target("restore-tools", () =>
{
    Run("dotnet",
        "tool restore",
        workingDirectory: buildToolDir);
});

Target("update-packages", DependsOn("restore-tools"), () =>
{
    Run("dotnet",
        $"update --path {solutionFile} --verbosity Verbose",
        workingDirectory: buildToolDir);
});

Target("create-update-pr", DependsOn("update-packages"), async () =>
{
    if (!runsOnGitHubActions)
    {
        throw new NotImplementedException();
    }

    var (message, _) = await ReadAsync("git", "status");

    var dirty = !message.Contains("nothing to commit, working tree clean", StringComparison.OrdinalIgnoreCase);

    if (!dirty)
    {
        Console.WriteLine("No changes made.");

        return;
    }

    await RunAsync(
        "git",
        "config user.name \"GitHub Actions Bot\"");

    await RunAsync(
        "git",
        "config user.email \"<>\"");

    //await RunAsync(
    //    "git",
    //    "checkout -b update");

    await RunAsync("git",
        "switch -c update");

    await RunAsync(
        "git",
        "commit -am \"chore: Update all packages\"");

    await RunAsync(
        "git",
        "push --set-upstream origin update --force");

    const int repositoryId = 459606942;
    var githubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");

    if (string.IsNullOrWhiteSpace(githubToken))
    {
        throw new InvalidOperationException("Access token not found. Should be set to environment variable 'GITHUB_TOKEN'");
    }

    InMemoryCredentialStore credentials = new(new Credentials(githubToken));
    GitHubClient client = new(new ProductHeaderValue("UpdatR.Build"), credentials);

    var prs = await client.PullRequest.GetAllForRepository(
        repositoryId,
        new PullRequestRequest
        {
            Base = "main",
            Head = "update",
            State = ItemStateFilter.Open
        });

    if (prs.Count == 0)
    {
        await client.PullRequest.Create(repositoryId, new NewPullRequest("📦 Update packages", "update", "main")
        {
            Body = "PR created automatically by UpdatR."
        });
    }
    else if (prs.Count == 1)
    {
        await client.PullRequest.Update(repositoryId, prs[0].Number, new PullRequestUpdate
        {
            Title = "📦 Update packages",
            Body = "PR created automatically by UpdatR."
        });
    }
    else
    {
        throw new InvalidOperationException("Found multiple open PR:s from UpdatR, how did that happen?");
    }
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
        $"test --configuration Release --no-build \"{solutionFile}\"");
});

Target("push", DependsOn("build"), async () =>
{
    var accessToken = Environment.GetEnvironmentVariable("API_ACCESS_TOKEN");

    if (string.IsNullOrWhiteSpace(accessToken))
    {
        throw new InvalidOperationException("Access token not found. Should be set to environment variable 'API_ACCESS_TOKEN'");
    }

    var version = await GetVersionAsync();

    var nuGetLogger = new NuGetLogger(LogLevel.Information);

    SourceCacheContext cache = new();
    var providers = new List<Lazy<INuGetResourceProvider>>();
    providers.AddRange(NuGet.Protocol.Core.Types.Repository.Provider.GetCoreV3());
    var packageSource = new PackageSource("https://api.nuget.org/v3/index.json");
    var sourceRepository = new SourceRepository(packageSource, providers);

    // Push to NuGet
    var packageUpdateResource = await sourceRepository.GetResourceAsync<PackageUpdateResource>();
    var symbolPackageUpdateResourceV3 = await sourceRepository.GetResourceAsync<SymbolPackageUpdateResourceV3>();

    var packages = Directory
        .EnumerateFiles(artifactsDir, "*.nupkg")
        .ToList();

    await packageUpdateResource.Push(
        packages,
        symbolSource: null,
        timeoutInSecond: 60,
        disableBuffering: false,
        getApiKey: _ => accessToken,
        getSymbolApiKey: null,
        noServiceEndpoint: false,
        skipDuplicate: true,
        symbolPackageUpdateResourceV3,
        nuGetLogger);
});

Target("default", DependsOn("test", "restore-tools"));

await RunTargetsAndExitAsync(args);

async Task<NuGetVersion> GetVersionAsync()
{
    var tag = string.Empty;

    if (runsOnGitHubActions)
    {
        var (tags, _) = await ReadAsync("git", "ls-remote --tags --sort=-version:refname --refs origin");

        var firstLine = tags.Split(Environment.NewLine)[0];

        tag = Regex.Match(firstLine, @"refs\/tags\/(?<Tag>.*)").Groups["Tag"].Value;
    }
    else
    {
        (tag, _) = await ReadAsync("git", "describe --abbrev=0 --tags");
    }

    return NuGetVersion.TryParse(tag[1..], out var version)
        ? version
        : throw new InvalidOperationException($"$Invalid version: {tag}.");
}

static async Task<DirectoryInfo> GetRepoRootDirectoryAsync()
{
    var (stdOutput, stdError) = await ReadAsync("git", "rev-parse --show-toplevel");

    return new DirectoryInfo(stdOutput.Trim());
}
