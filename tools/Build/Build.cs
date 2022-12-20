// CA1852 Type 'Program' can be sealed because it has no subtypes in its containing assembly and is not externally visible
#pragma warning disable CA1852 // <-- Disabled due to bug: https://github.com/dotnet/roslyn-analyzers/issues/6141

using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using BuildingBlocks;
using Microsoft.Extensions.Configuration;
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
    .AddLogging(
        builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddConsole();
        }
    )
    .BuildServiceProvider();

var runsOnGitHubActions = !string.IsNullOrWhiteSpace(
    Environment.GetEnvironmentVariable("GITHUB_ACTIONS")
);

if (runsOnGitHubActions)
{
    Console.WriteLine("Running on GitHub Actions.");

    await RunAsync("git", "config user.name \"GitHub Actions Bot\"");

    await RunAsync("git", "config user.email \"<>\"");
}

var rootDir = await GetRepoRootDirectoryAsync();

Directory.SetCurrentDirectory(rootDir.FullName);

const int repositoryId = 459606942;
var buildToolDir = Path.GetFullPath(Path.Combine("tools", "Build"));
var artifactsDir = Path.GetFullPath("Artifacts");
var logsDir = Path.Combine(artifactsDir, "logs");
var buildLogFile = Path.Combine(logsDir, "build.binlog");

var solutionFile = Path.Combine(rootDir.FullName, "UpdatR.sln");
var srcDir = Path.Combine(rootDir.FullName, "src");
var releaseNotes = Path.Combine(srcDir, "Build", "docs", "release-notes.txt");

Target(
    "artifactDirectories",
    () =>
    {
        Directory.CreateDirectory(artifactsDir);
        Directory.CreateDirectory(logsDir);
    }
);

Target(
    "restore-tools",
    () =>
    {
        Run("dotnet", "tool restore", workingDirectory: buildToolDir);
    }
);

Target(
    "update-packages",
    DependsOn("restore-tools"),
    () =>
    {
        Run(
            "dotnet",
            $"update {solutionFile} --verbosity {nameof(LogLevel.Debug)} --title {Path.Combine(Path.GetTempPath(), "title.md")} --description {Path.Combine(Path.GetTempPath(), "description.md")}",
            workingDirectory: buildToolDir
        );
    }
);

Target(
    "create-update-pr",
    DependsOn("update-packages"),
    async () =>
    {
        var title = File.ReadAllText(Path.Combine(Path.GetTempPath(), "title.md"));
        var description = File.ReadAllText(Path.Combine(Path.GetTempPath(), "description.md"));
        var body =
            "# PR created automatically by UpdatR"
            + Environment.NewLine
            + Environment.NewLine
            + description;

        var (message, _) = await ReadAsync("git", "status");

        var dirty = !message.Contains(
            "nothing to commit, working tree clean",
            StringComparison.OrdinalIgnoreCase
        );

        if (!dirty)
        {
            Console.WriteLine("No changes made.");

            return;
        }

        await RunAsync("git", "checkout -b update");

        await RunAsync("git", $"commit -am \"chore: {title}\"");

        await RunAsync("git", "push --set-upstream origin update --force");

        var githubToken = GetToken();

        InMemoryCredentialStore credentials = new(new Credentials(githubToken));
        GitHubClient client = new(new ProductHeaderValue("UpdatR.Build"), credentials);

        var prs = await client.PullRequest.GetAllForRepository(
            repositoryId,
            new PullRequestRequest
            {
                Base = "main",
                Head = "update",
                State = ItemStateFilter.Open
            }
        );

        if (prs.Count == 0)
        {
            await client.PullRequest.Create(
                repositoryId,
                new NewPullRequest(title, "update", "main") { Body = body }
            );
        }
        else if (prs.Count == 1)
        {
            await client.PullRequest.Update(
                repositoryId,
                prs[0].Number,
                new PullRequestUpdate { Title = title, Body = body }
            );
        }
        else
        {
            throw new InvalidOperationException(
                "Found multiple open PR:s from UpdatR, how did that happen?"
            );
        }

        await client.Actions.Workflows.CreateDispatch(
            "OskarKlintrot",
            "UpdatR",
            "build.yml",
            new("update")
        );
    }
);

Target(
    "generate-docs",
    DependsOn("restore-tools"),
    async () =>
    {
        var (version, _) = await GetVersionAndTagAsync();

        var packagesToBe = GetPackagesInSrc();
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

        await RunAsync("dotnet", $"build \"{solutionFile}\"");

        var cli = Path.Combine(srcDir, "dotnet-updatr", "bin", "Debug", "net6.0");

        var (helpDescription, _) = await ReadAsync(
            "dotnet",
            "exec dotnet-updatr.dll --help",
            workingDirectory: cli
        );

        await File.WriteAllLinesAsync(
            Path.Combine("mdsource", "cli-usage.txt"),
            helpDescription.Replace("dotnet-updatr", "update").Split(Environment.NewLine)[3..]
        );

        await RunAsync("dotnet", $"mdsnippets {rootDir.FullName}", workingDirectory: buildToolDir);

        await AdjustReadmeForNuGet(packagesToBe);
        CopyIconAndReleaseNotes(packagesToBe, releaseNotes, icon);

        static async Task AdjustReadmeForNuGet(
            IEnumerable<(string Csproj, string PackageId)> packagesToBe
        )
        {
            foreach (var (csproj, packageId) in packagesToBe)
            {
                var readmePath = Path.Combine(
                    new FileInfo(csproj).DirectoryName!,
                    "docs",
                    "README.md"
                );

                var originalReadmeContent = await File.ReadAllLinesAsync(readmePath);

                var projectRoot = Directory.GetParent(csproj)!.FullName;

                var subHeadings = originalReadmeContent
                    .Where(x => Regex.IsMatch(x, "^#{1,2}[^#].*"))
                    .ToArray();

                if (!subHeadings[0].StartsWith($"## {packageId}", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Missing README.md-section for {packageId}."
                    );
                }

                if (!subHeadings[^1].StartsWith("# Icon", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Icon info should be last in README.md");
                }

                var newReadmeContent = originalReadmeContent
                    .Select(
                        x => x.StartsWith("##", StringComparison.OrdinalIgnoreCase) ? x[1..] : x
                    )
                    .Select(
                        line =>
                        {
                            foreach (var (_, id) in packagesToBe)
                            {
                                var relativeLink =
                                    $"(#{id.Replace(".", string.Empty).ToLowerInvariant()})";
                                var nugetLink = $"(https://www.nuget.org/packages/{id}/)";

                                if (line.Contains(relativeLink))
                                {
                                    line = line.Replace(relativeLink, nugetLink);
                                }
                            }

                            return line;
                        }
                    );

                await File.WriteAllLinesAsync(
                    Path.Combine(projectRoot, "docs", "README.md"),
                    newReadmeContent,
                    Encoding.UTF8
                );
            }
        }

        static void CopyIconAndReleaseNotes(
            IEnumerable<(string Csproj, string PackageId)> packagesToBe,
            string releaseNotes,
            string icon
        )
        {
            foreach (var (csproj, packageId) in packagesToBe)
            {
                var projectRoot = Directory.GetParent(csproj)!.FullName;

                File.Copy(icon, Path.Combine(projectRoot, "images", "icon.png"), true);
                File.Copy(
                    releaseNotes,
                    Path.Combine(projectRoot, "docs", "release-notes.txt"),
                    true
                );
            }
        }
    }
);

Target(
    "pack",
    DependsOn("artifactDirectories", "generate-docs"),
    async () =>
    {
        var (version, _) = await GetVersionAndTagAsync();

        await RunAsync(
            "dotnet",
            $"build --configuration Release /p:Version=\"{version}\" /bl:\"{buildLogFile}\" \"{solutionFile}\""
        );
    }
);

Target(
    "reset-generated-docs",
    DependsOn("pack"),
    () =>
    {
        var packages = GetPackagesInSrc();

        List<string> filesToReset = new();

        foreach (var (csproj, _) in packages)
        {
            var docsPath = Path.Combine(new FileInfo(csproj).DirectoryName!, "docs");
            var readmePath = Path.Combine(docsPath, "README.md");
            var releaseNotesPath = Path.Combine(docsPath, "release-notes.txt");

            if (!File.Exists(readmePath))
            {
                throw new InvalidOperationException($"Could not find {readmePath}.");
            }

            if (!File.Exists(releaseNotesPath))
            {
                throw new InvalidOperationException($"Could not find {releaseNotesPath}.");
            }

            filesToReset.Add(readmePath);
            filesToReset.Add(releaseNotesPath);
        }

        Run(
            "git",
            $"checkout {string.Join(' ', filesToReset)}",
            workingDirectory: rootDir.FullName
        );
    }
);

Target(
    "test",
    DependsOn("pack", "reset-generated-docs"),
    () =>
    {
        Run("dotnet", $"test --configuration Release --no-build \"{solutionFile}\"");
    }
);

Target(
    "push",
    DependsOn("test"),
    async () =>
    {
        var accessToken = Environment.GetEnvironmentVariable("API_ACCESS_TOKEN");

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new InvalidOperationException(
                "Access token not found. Should be set to environment variable 'API_ACCESS_TOKEN'"
            );
        }

        var nuGetLogger = new NuGetLogger(services.GetRequiredService<ILogger<NuGetLogger>>());

        SourceCacheContext cache = new();
        var providers = new List<Lazy<INuGetResourceProvider>>();
        providers.AddRange(NuGet.Protocol.Core.Types.Repository.Provider.GetCoreV3());
        var packageSource = new PackageSource("https://api.nuget.org/v3/index.json");
        var sourceRepository = new SourceRepository(packageSource, providers);

        // Push to NuGet
        var packageUpdateResource =
            await sourceRepository.GetResourceAsync<PackageUpdateResource>();
        var symbolPackageUpdateResourceV3 =
            await sourceRepository.GetResourceAsync<SymbolPackageUpdateResourceV3>();

        var packages = Directory.EnumerateFiles(artifactsDir, "*.nupkg").ToList();

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
            nuGetLogger
        );
    }
);

Target(
    "create-release",
    DependsOn("test"),
    async () =>
    {
        var githubToken = GetToken();

        InMemoryCredentialStore credentials = new(new Credentials(githubToken));
        GitHubClient client = new(new ProductHeaderValue("UpdatR.Build"), credentials);

        var (version, tagRef) = await GetVersionAndTagAsync();
        var prerelease = version.IsPrerelease;

        await client.Repository.Release.Create(
            repositoryId,
            new(tagRef) { Name = "UpdatR v" + version.ToString(), Prerelease = prerelease, }
        );
    }
);

Target(
    "restore-release-notes-txt",
    async () =>
    {
        var version = await GetLatestVersionAsync();

        if (version.IsPrerelease)
        {
            return;
        }

        await File.WriteAllTextAsync(releaseNotes, null);

        var (message, _) = await ReadAsync("git", "status");

        var dirty = !message.Contains(
            "nothing to commit, working tree clean",
            StringComparison.OrdinalIgnoreCase
        );

        if (!dirty)
        {
            Console.WriteLine("No changes made.");

            return;
        }

        var tag = await GetLatestTagAsync();

        await RunAsync("git", $"add {releaseNotes}");
        await RunAsync("git", $"commit -m \"chore: Reset release-notes.txt after release {tag}\"");

        if (runsOnGitHubActions)
        {
            await RunAsync("git", "push");
        }
    }
);

Target(
    "update-README",
    DependsOn("generate-docs"),
    async () =>
    {
        var (message, _) = await ReadAsync("git", "status README.md");

        var dirty = !message.Contains(
            "nothing to commit, working tree clean",
            StringComparison.OrdinalIgnoreCase
        );

        if (!dirty)
        {
            Console.WriteLine("No changes made.");

            return;
        }

        await RunAsync("git", "add README.md");
        await RunAsync("git", "add mdsource");
        await RunAsync("git", "commit -m \"chore: Update README.md\"");

        if (runsOnGitHubActions)
        {
            await RunAsync("git", "push");
        }
    }
);

Target("post-release", DependsOn("restore-release-notes-txt"));
Target("default", DependsOn("test", "restore-tools"));

await RunTargetsAndExitAsync(args);

async Task<NuGetVersion> GetLatestVersionAsync()
{
    var (version, _) = await GetVersionAndTagAsync();

    return version;
}

async Task<string> GetLatestTagAsync()
{
    var (_, tag) = await GetVersionAndTagAsync();

    return tag;
}

async Task<(NuGetVersion NuGetVersion, string TagRef)> GetVersionAndTagAsync()
{
    HashSet<string> tags = new();

    var (output, _) = await ReadAsync("git", "ls-remote --tags --refs origin");

    foreach (var line in output.Split('\n'))
    {
        var tag = Regex.Match(line, @"refs\/tags\/(?<Tag>.*)").Groups["Tag"].Value;

        if (!string.IsNullOrWhiteSpace(tag) && NuGetVersion.TryParse(tag[1..], out _))
        {
            tags.Add(tag);
        }
    }

    return tags.Select(
            x =>
            {
                _ = NuGetVersion.TryParse(x[1..], out var version);

                return (version, x);
            }
        )
        .OrderByDescending(x => x.version)
        .First();
}

static async Task<DirectoryInfo> GetRepoRootDirectoryAsync()
{
    var (stdOutput, stdError) = await ReadAsync("git", "rev-parse --show-toplevel");

    return new DirectoryInfo(stdOutput.Trim());
}

string GetToken()
{
    var githubToken = runsOnGitHubActions
        ? Environment.GetEnvironmentVariable("GITHUB_TOKEN")
        : ((ConfigurationManager)new ConfigurationManager().AddUserSecrets<Program>())
          .GetSection("GitHub")
          .GetValue<string>("PAT");

    if (string.IsNullOrWhiteSpace(githubToken))
    {
        throw new InvalidOperationException(
            "Access token not found. Should be set to environment variable 'GITHUB_TOKEN'"
        );
    }

    return githubToken;
}

IEnumerable<(string Csproj, string PackageId)> GetPackagesInSrc()
{
    foreach (
        var project in Directory.EnumerateFiles(srcDir, "*.csproj", SearchOption.AllDirectories)
    )
    {
        var doc = new XmlDocument { PreserveWhitespace = true, };

        doc.Load(project);

        var packageId = doc.SelectSingleNode("/Project/PropertyGroup/PackageId");

        if (packageId is not null)
        {
            yield return (project, packageId.InnerText);
        }
    }
}
