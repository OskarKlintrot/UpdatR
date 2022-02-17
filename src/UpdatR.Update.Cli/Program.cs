using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml;
using NuGet.Configuration;
using NuGet.Credentials;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using BuildingBlocks;

namespace UpdatR.Update.Cli;

internal static partial class Program
{
    private static NuGetLogger _nuGetLogger = null!;

    /// <summary>
    /// Update all packages in solution or project(s).
    /// </summary>
    /// <param name="path">Path to solution or project(s). Leave out if solution or project(s) is in current folder or if project(s) is in subfolders.</param>
    /// <param name="verbosity"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    internal static async Task Main(string? path = null, LogLevel verbosity = LogLevel.Minimal)
    {
        var sw = Stopwatch.StartNew();

        _nuGetLogger = new NuGetLogger(verbosity);

        Console.WriteLine($"Verbosity: {verbosity}");

        var (solution, projects) = await GetProjectsAsync(path);

        Dictionary<FileInfo, IEnumerable<string>> projectsWithPackages = new();

        foreach (var project in projects)
        {
            projectsWithPackages[project] = GetPackageIds(project);
        }

        List<string>? solutionTools = null;

        if (solution is not null)
        {
            solutionTools = GetDotnetToolPackageIds(solution.Directory!);
        }

        DefaultCredentialServiceUtility.SetupDefaultCredentialService(_nuGetLogger, true);

        var packages = await GetPackageVersions(solution, projectsWithPackages, solutionTools);

        if (solution is not null)
        {
            await UpdateDotnetToolsAsync(solution.Directory!, packages);
        }

        foreach (var project in projectsWithPackages.Where(x => x.Value.Any()))
        {
            await UpdateDotnetToolsAsync(project.Key.Directory!, packages);

            var doc = new XmlDocument
            {
                PreserveWhitespace = true,
            };

            doc.Load(project.Key.FullName);

            var changed = false;

            void handler(object sender, XmlNodeChangedEventArgs e) => changed = true;
            doc.NodeChanged += handler;
            doc.NodeInserted += handler;
            doc.NodeRemoved += handler;

            var packageReferences = doc
                .SelectNodes("/Project/ItemGroup/PackageReference")!
                .OfType<XmlElement>();

            foreach (var packageReference in packageReferences)
            {
                var packageId = packageReference.GetAttribute("Include");
                var versionStr = packageReference.GetAttribute("Version");

                if (!NuGetVersion.TryParse(versionStr, out var version))
                {
                    // Todo: Log failure
                    continue;
                }

                if (!packages.TryGetValue(packageId, out var package))
                {
                    // Todo: Log failure
                    continue;
                }

                if (!package.TryGetLatestComparedTo(version, out var updateTo))
                {
                    LogWarningsIfAny(
                        packageId,
                        package.PackageMetadatas.SingleOrDefault(x => x.Version == version));

                    continue;
                }

                packageReference.SetAttribute("Version", updateTo.Version.ToString());

                Console.WriteLine($"{project.Key.Name}: Updated {packageId} from {version} to {updateTo.Version}");

                // Todo: Log update

                LogWarningsIfAny(packageId, updateTo);
            }

            if (changed)
            {
                doc.Save(project.Key.FullName);
            }
        }

        Console.WriteLine($"Finished after {sw.Elapsed:hh\\:mm\\:ss\\.fff}.");
    }

    private static void LogWarningsIfAny(string packageId, PackageMetadata? packageMetadata)
    {
        if (packageMetadata is null)
        {
            return;
        }

        if (packageMetadata.DeprecationMetadata is not null)
        {
            // Todo: Log warning

            Console.WriteLine($"warn: Package {packageId} version {packageMetadata.Version} is deprecated: {string.Join(", ", packageMetadata.DeprecationMetadata.Reasons)}");

            foreach (var line in packageMetadata.DeprecationMetadata.Message.ReplaceLineEndings().Split(Environment.NewLine))
            {
                Console.WriteLine($"warn: {line}");
            }

            Console.WriteLine();
        }

        if (packageMetadata.Vulnerabilities?.Any() == true)
        {
            // Todo: Log warning

            Console.WriteLine($"warn: Package {packageId} version {packageMetadata.Version} has {packageMetadata.Vulnerabilities.Count()} vulnerabilities");
        }
    }

    private static async Task UpdateDotnetToolsAsync(DirectoryInfo directory, IDictionary<string, NuGetPackage> packages)
    {
        var dotnetTools = GetDotnetToolsConfigFileInfo(directory);

        if (!dotnetTools.Exists)
        {
            return;
        }

        var config = JsonSerializer.Deserialize<JsonObject>(await File.ReadAllTextAsync(dotnetTools.FullName));

        if (config is null)
        {
            return;
        }

        var tools = config["tools"]?.AsObject();

        if (tools is null)
        {
            return;
        }

        var changed = false;

        foreach (var element in tools)
        {
            var packageId = element.Key;

            if (packageId is null)
            {
                continue;
            }

            var toolObject = element.Value?.AsObject();

            if (toolObject is null)
            {
                // Todo: Log warn

                continue;
            }

            foreach (var property in toolObject.ToList())
            {
                toolObject.Remove(property.Key);
                if (property.Key.Equals("version", StringComparison.OrdinalIgnoreCase)
                    && NuGetVersion.TryParse(property.Value?.GetValue<string>(), out var version)
                    && packages.TryGetValue(packageId, out var package)
                    && package.TryGetLatestComparedTo(version, out var updateTo))
                {
                    toolObject.Add(property.Key, updateTo.Version.ToString());

                    changed = true;
                }
                else
                {
                    toolObject.Add(property);
                }
            }
        }

        if (!changed)
        {
            return;
        }

        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
        });

        if (json is null)
        {
            // Todo: Log warning

            return;
        }

        await File.WriteAllTextAsync(dotnetTools.FullName, json);
    }

    private static async Task<IDictionary<string, NuGetPackage>> GetPackageVersions(
        FileInfo? solution,
        IReadOnlyDictionary<FileInfo, IEnumerable<string>> projectsWithPackages,
        IEnumerable<string>? solutionTools)
    {
        using var cacheContext = new SourceCacheContext();

        Dictionary<string, NuGetPackage> packageSearchMetadata = new();

        HashSet<string> failedRepos = new();

        var projectsWithPackagesTemp = new Dictionary<FileInfo, IEnumerable<string>>(projectsWithPackages);

        if (solutionTools is not null)
        {
            projectsWithPackagesTemp[solution!] = solutionTools;
        }

        foreach (var (project, packageIds) in projectsWithPackagesTemp)
        {
            var settings = Settings.LoadDefaultSettings(solution?.DirectoryName ?? project.DirectoryName);

            var packageSourceProvider = new PackageSourceProvider(settings);

            var sourceRepositoryProvider = new SourceRepositoryProvider(packageSourceProvider, Repository.Provider.GetCoreV3());

            foreach (var repo in sourceRepositoryProvider
                .GetRepositories()
                .Where(x => !failedRepos.Contains(x.PackageSource.Name)))
            {
                try
                {
                    foreach (var packageId in packageIds)
                    {
                        var packageMetadataResource = repo.GetResource<PackageMetadataResource>();

                        var searchMetadata = await packageMetadataResource.GetMetadataAsync(
                            packageId,
                            includePrerelease: true,
                            includeUnlisted: false,
                            cacheContext,
                            _nuGetLogger,
                            CancellationToken.None);

                        var metadata = searchMetadata
                            .OfType<PackageSearchMetadataRegistration>()
                            .Select(x => new PackageMetadata(x.Version, x.DeprecationMetadata, x.Vulnerabilities));

                        if (!metadata.Any())
                        {
                            continue;
                        }

                        if (packageSearchMetadata.TryGetValue(packageId, out var package))
                        {
                            packageSearchMetadata[packageId] = package with
                            {
                                PackageMetadatas = package.PackageMetadatas
                                    .Union(metadata)
                                    .DistinctBy(x => x.Version)
                                    .OrderByDescending(x => x.Version),
                            };
                        }
                        else
                        {
                            packageSearchMetadata[packageId] = new(packageId, metadata);
                        }
                    }
                }
                catch (AggregateException exception)
                when (exception.InnerException?.InnerException is HttpRequestException httpRequestException
                    && httpRequestException.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    // Todo: Log

                    failedRepos.Add(repo.PackageSource.Name);

                    continue;
                }
                catch (HttpRequestException exception)
                when (exception.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    // Todo: Log

                    failedRepos.Add(repo.PackageSource.Name);

                    continue;
                }
            }
        }

        return packageSearchMetadata;
    }

    private static async Task<(FileInfo? Solution, IEnumerable<FileInfo> Projects)> GetProjectsAsync(string? path)
    {
        if (path == null)
        {
            path = Directory.GetCurrentDirectory();
        }

        if (File.Exists(path) && path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
        {
            var solution = new FileInfo(path);

            return (solution, await GetProjectsFromSolutionAsync(solution));
        }

        if (!Directory.Exists(path))
        {
            throw new ArgumentException("Path does not exist.", nameof(path));
        }

        var solutions = Directory
               .EnumerateFiles(path, "*.sln", new EnumerationOptions
               {
                   MatchCasing = MatchCasing.CaseInsensitive,
                   RecurseSubdirectories = false
               })
               .ToList();

        if (solutions.Count > 1)
        {
            throw new ArgumentException("Found more than one solution.", nameof(path));
        }

        if (solutions.Count == 1)
        {
            var solution = new FileInfo(solutions[0]);

            return (solution, await GetProjectsFromSolutionAsync(solution));
        }

        var projects = Directory
            .EnumerateFiles(path, "*.csproj", SearchOption.AllDirectories)
            .Select(x => new FileInfo(x));

        if (!projects.Any())
        {
            throw new ArgumentException("Path contains no projects.", nameof(path));
        }

        return (null, projects);
    }

    private static async Task<IEnumerable<FileInfo>> GetProjectsFromSolutionAsync(FileInfo solution)
    {
        var content = await File.ReadAllTextAsync(solution.FullName);

        return Regex
            .Matches(content, @"(Project).*(?<="")(?<Project>\S*\.csproj)(?="")", RegexOptions.Multiline)
            .Select(x => Path.Combine(x.Groups["Project"].Value.Split('\\'))) // sln has windows-style paths, will not work on linux
            .Select(x => Path.Combine(solution.DirectoryName!, x))
            .Select(x => new FileInfo(x))
            .Where(x => x.Exists);
    }

    private static IEnumerable<string> GetPackageIds(FileInfo project)
    {
        var tools = GetDotnetToolPackageIds(project.Directory!);

        var doc = new XmlDocument();

        doc.Load(project.FullName);

        return doc
            .SelectNodes("/Project/ItemGroup/PackageReference")!
            .OfType<XmlElement>()
            .Select(x => (PackageId: x!.GetAttribute("Include"), Version: x!.GetAttribute("Version")))
            .Where(x => !string.IsNullOrWhiteSpace(x.PackageId) && NuGetVersion.TryParse(x.Version, out _))
            .Select(x => x.PackageId)
            .Union(tools);
    }

    private static List<string> GetDotnetToolPackageIds(DirectoryInfo path)
    {
        var dotnetTools = GetDotnetToolsConfigFileInfo(path);

        var tools = new List<string>();

        if (dotnetTools.Exists)
        {
            var json = File.ReadAllText(dotnetTools.FullName);
            var foo = JsonSerializer.Deserialize<JsonObject>(json);

            var packageIds = foo?["tools"]?
                .AsObject()
                .Select(x => (PackageId: x.Key, Version: x.Value?["version"]?.GetValue<string>()))
                .Where(x => NuGetVersion.TryParse(x.Version, out _))
                .Select(x => x.PackageId);

            if (packageIds?.Any() == true)
            {
                tools.AddRange(packageIds);
            }
        }

        return tools;
    }

    private static FileInfo GetDotnetToolsConfigFileInfo(DirectoryInfo path)
    {
        return new FileInfo(Path.Combine(path.FullName, ".config", "dotnet-tools.json"));
    }

    private record NuGetPackage(string PackageId, IEnumerable<PackageMetadata> PackageMetadatas)
    {
        private PackageMetadata? _latestStable;
        private PackageMetadata? _latestPrerelease;

        public PackageMetadata? LatestStable => _latestStable ??= PackageMetadatas
            .OrderByDescending(x => x.Version)
            .Where(x => !x.Version.IsPrerelease)
            .FirstOrDefault();

        public PackageMetadata? LatestPrerelease => _latestPrerelease ??= PackageMetadatas
            .OrderByDescending(x => x.Version)
            .Where(x => x.Version.IsPrerelease)
            .FirstOrDefault();

        /// <summary>
        /// Get latest stable if <paramref name="version"/> is stable and older than <see cref="LatestStable"/>.
        /// If <paramref name="version"/> is prerelase then take latest prerelease unless there is a newer stable version.
        /// </summary>
        /// <param name="version">Current version to compare to.</param>
        /// <param name="package"></param>
        /// <returns></returns>
        public bool TryGetLatestComparedTo(
            NuGetVersion version,
            [NotNullWhen(returnValue: true)] out PackageMetadata? package)
        {
            if (LatestStable?.Version > version)
            {
                package = LatestStable;

                return true;
            }
            else if (version.IsPrerelease && LatestPrerelease?.Version > version)
            {
                package = LatestPrerelease;

                return true;
            }

            package = null;

            return false;
        }
    }

    private record PackageMetadata(NuGetVersion Version, PackageDeprecationMetadata? DeprecationMetadata, IEnumerable<PackageVulnerabilityMetadata>? Vulnerabilities);
}
