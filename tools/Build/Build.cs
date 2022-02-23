using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using BuildingBlocks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NuGet.Configuration;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using Octokit;
using Octokit.Internal;
using static Bullseye.Targets;
using static SimpleExec.Command;

var services = new ServiceCollection()
    .AddLogging(builder =>
    {
        builder.SetMinimumLevel(LogLevel.Information);
        builder.AddConsole();
    })
    .BuildServiceProvider();

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
var srcDir = Path.Combine(rootDir.FullName, "src");

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
        $"update --path {solutionFile} --verbosity Verbose --output {Path.Combine(Path.GetTempPath(), "output.txt")}",
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

    await RunAsync(
        "git",
        "checkout -b update");

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

    var output = File.ReadAllText(Path.Combine(Path.GetTempPath(), "output.md"));
    var title = output.Split(Environment.NewLine)[0];
    var body = "# PR created automatically by UpdatR."
        + Environment.NewLine
        + string.Concat(output.Split(Environment.NewLine)[1..]);

    if (prs.Count == 0)
    {
        await client.PullRequest.Create(repositoryId, new NewPullRequest(title, "update", "main")
        {
            Body = body
        });
    }
    else if (prs.Count == 1)
    {
        await client.PullRequest.Update(repositoryId, prs[0].Number, new PullRequestUpdate
        {
            Title = title,
            Body = body
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

    var packagesToBe = GetPackagesIn(srcDir);
    var releaseNotes = Path.Combine(srcDir, "Build", "docs", "release-notes.txt");
    var readme = Path.Combine(rootDir.FullName, "README.md");
    var icon = Path.Combine(srcDir, "Build", "icon.png");

    if (!File.Exists(releaseNotes))
    {
        throw new InvalidOperationException($"{releaseNotes} not found.");
    }

    if (!File.Exists(readme))
    {
        throw new InvalidOperationException($"{readme} not found.");
    }

    if (!File.Exists(icon))
    {
        throw new InvalidOperationException($"{icon} not found.");
    }

    var fullReadmeContent = await File.ReadAllLinesAsync(readme);
    var readmeIconStart = Array.IndexOf(fullReadmeContent, "# Icon");

    foreach (var (csproj, packageId) in packagesToBe)
    {
        var projectRoot = Directory.GetParent(csproj)!.FullName;

        var subHeadings = fullReadmeContent.Where(x => Regex.IsMatch(x, "^#{1,2}[^#].*")).ToArray();

        if (subHeadings[^1] != "# Icon")
        {
            throw new InvalidOperationException("Icon info should be last in README.md");
        }

        var iconContent = fullReadmeContent[Array.IndexOf(fullReadmeContent, "# Icon")..]
            .Select(x => x.Replace("# Icon", "## Icon"));

        var readmeContentStart = Array.IndexOf(fullReadmeContent, $"## {packageId}");

        if (readmeContentStart == -1)
        {
            throw new InvalidOperationException($"Missing README.md-section for {packageId}.");
        }

        var readmeContentEnd = Array.IndexOf(fullReadmeContent, subHeadings[Array.IndexOf(subHeadings, $"## {packageId}") + 1]);

        var readmeContent = fullReadmeContent[readmeContentStart..readmeContentEnd]
            .Select(x => x.StartsWith("##", StringComparison.OrdinalIgnoreCase) ? x[1..] : x)
            .Union(iconContent);

        File.Copy(icon, Path.Combine(projectRoot, "images", "icon.png"), true);
        File.Copy(releaseNotes, Path.Combine(projectRoot, "docs", "release-notes.txt"), true);
        await File.WriteAllLinesAsync(Path.Combine(projectRoot, "docs", "README.md"), readmeContent, Encoding.UTF8);
    }

    await RunAsync("dotnet",
        $"build --configuration Release /p:Version=\"{version}\" /bl:\"{buildLogFile}\" \"{solutionFile}\"");

    static IEnumerable<(string Csproj, string PackageId)> GetPackagesIn(string srcDir)
    {
        foreach (var project in Directory.EnumerateFiles(srcDir, "*.csproj", SearchOption.AllDirectories))
        {
            var doc = new XmlDocument
            {
                PreserveWhitespace = true,
            };

            doc.Load(project);

            var packageId = doc.SelectSingleNode("/Project/PropertyGroup/PackageId");

            if (packageId is not null)
            {
                yield return (project, packageId.InnerText);
            }
        }
    }
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

    var nuGetLogger = new NuGetLogger(services.GetRequiredService<ILogger<NuGetLogger>>());

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
