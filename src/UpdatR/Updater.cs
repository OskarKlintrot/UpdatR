using BuildingBlocks;
using Microsoft.Extensions.Logging;
using NuGet.Configuration;
using NuGet.Credentials;
using NuGet.Frameworks;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using UpdatR.Domain;
using UpdatR.Domain.Utils;
using UpdatR.Internals;

namespace UpdatR;

public sealed partial class Updater(ILogger<Updater>? logger = null)
{
    private readonly ILogger _logger =
        logger ?? new Microsoft.Extensions.Logging.Abstractions.NullLogger<Updater>();

    /// <summary>
    /// Upper bound on the number of concurrent NuGet metadata requests issued against a single
    /// source, so a large solution doesn't fire off hundreds of simultaneous requests against the
    /// same feed.
    /// </summary>
    private const int MaxConcurrentNuGetRequests = 8;

    /// <summary>
    /// Update all packages in solution or project(s).
    /// </summary>
    /// <param name="path">Path to solution or project(s). Leave out if solution or project(s) is in current folder or if project(s) is in subfolders.</param>
    /// <param name="options">
    /// Options controlling what to update and how. Leave out to update every package to the
    /// latest stable version compatible with each project's target framework(s).
    /// </param>
    /// <param name="cancellationToken">
    /// Propagated to every NuGet metadata request and to solution/project file parsing. Does not
    /// interrupt a project's changes from being persisted to disk once an update has already
    /// been decided upon, so a cancelled run cannot leave a file half-written.
    /// </param>
    /// <remarks>
    /// If a <c>.updatrrc</c> JSON file is found - first next to <paramref name="path"/>, then in
    /// the current working directory - its <c>excludePackages</c>, <c>allowedLicenses</c>,
    /// <c>excludeFiles</c>, <c>alignWithTfm</c>, <c>toolPackagePins</c> and
    /// <c>packagePolicies</c> values are merged with <see cref="UpdateOptions.ExcludePackages"/>,
    /// <see cref="UpdateOptions.AllowedLicenses"/>, <see cref="UpdateOptions.ExcludeFiles"/>,
    /// <see cref="UpdateOptions.AlignWithTfm"/>, <see cref="UpdateOptions.ToolPackagePins"/> and
    /// <see cref="UpdateOptions.PackagePolicies"/> respectively - a union for the first four,
    /// per-tool override (config, then caller-supplied entries win over the built-in default) for
    /// <c>toolPackagePins</c>, and a concatenation (caller-supplied entries checked first) for
    /// <c>packagePolicies</c>. Its <c>failOn</c> and <c>failOnIncomplete</c> are used only if
    /// <see cref="UpdateOptions.FailOn"/> and <see cref="UpdateOptions.FailOnIncomplete"/> aren't
    /// given. If <paramref name="path"/> is left out (i.e. it resolves to the
    /// current directory) and the config file has a <c>path</c>, that's used as the target path
    /// instead of the current directory.
    /// </remarks>
    /// <returns><see cref="Summary"/></returns>
    /// <exception cref="InvalidUpdateTargetException"></exception>
    public async Task<Summary> UpdateAsync(
        string? path = null,
        UpdateOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        options ??= new UpdateOptions();

        var tfm = ParseTFM(options.TargetFrameworkMoniker);

        path ??= Directory.GetCurrentDirectory();

        var updatRConfig = UpdatRConfig.Load(path, out var configDirectory);

        var excludePackages = UpdatRConfig.Merge(
            options.ExcludePackages,
            updatRConfig?.ExcludePackages
        );
        var allowedLicenses = UpdatRConfig.Merge(
            options.AllowedLicenses,
            updatRConfig?.AllowedLicenses
        );
        var excludeFiles = UpdatRConfig.Merge(options.ExcludeFiles, updatRConfig?.ExcludeFiles);
        var alignWithTfm = UpdatRConfig.Merge(options.AlignWithTfm, updatRConfig?.AlignWithTfm);
        var failOn =
            options.FailOn ?? UpdatRConfig.ParseFailOn(updatRConfig?.FailOn) ?? FailOn.None;
        var failOnIncomplete = options.FailOnIncomplete ?? updatRConfig?.FailOnIncomplete ?? false;

        // Options-supplied policies are checked first, so they take priority when more than one
        // pattern matches the same package id (see PackageContainer.ResolvePackagePolicyMaxMajor).
        var packagePolicies = (options.PackagePolicies ?? [])
            .Concat(
                updatRConfig?.PackagePolicies?.Select(x => new PackageVersionPolicy(
                    x.Package,
                    x.MaxMajor
                )) ?? []
            )
            .ToArray();

        // dotnet-ef is pinned to Microsoft.EntityFrameworkCore by default; a config file entry
        // for the same tool overrides the default, and a caller-supplied entry overrides both.
        var toolPackagePinsByToolId = new Dictionary<string, ToolPackagePin>(
            StringComparer.OrdinalIgnoreCase
        )
        {
            [ToolPackagePin.EntityFrameworkCore.ToolPackageId] = ToolPackagePin.EntityFrameworkCore,
        };

        if (updatRConfig?.ToolPackagePins is { } toolPackagePinsFromConfig)
        {
            foreach (var pin in toolPackagePinsFromConfig)
            {
                toolPackagePinsByToolId[pin.Tool] = new ToolPackagePin(pin.Tool, pin.Package);
            }
        }

        if (options.ToolPackagePins is not null)
        {
            foreach (var pin in options.ToolPackagePins)
            {
                toolPackagePinsByToolId[pin.ToolPackageId] = pin;
            }
        }

        var effectiveToolPackagePins = toolPackagePinsByToolId.Values.ToArray();

        if (
            !string.IsNullOrWhiteSpace(updatRConfig?.Path)
            && configDirectory is not null
            && PathsAreEqual(path, Directory.GetCurrentDirectory())
        )
        {
            path = UpdatRConfig.ResolvePath(configDirectory, updatRConfig.Path);

            if (!Directory.Exists(path) && !File.Exists(path))
            {
                throw new InvalidUpdateTargetException(
                    $"'{nameof(UpdatRConfig.Path)}' (\"path\") in '{Path.Combine(configDirectory, UpdatRConfig.FileName)}' resolved to '{path}', which does not exist."
                );
            }
        }

        var shouldIncludePackage = CreateSearch(options.Packages, treatNullOrEmptyAs: true);
        var shouldExcludePackage = CreateSearch(excludePackages, treatNullOrEmptyAs: false);

        // Root used to resolve excludeFiles' patterns to a relative path - mirrors how RootDir
        // itself derives its Path (the target directory itself, or the containing directory of a
        // single-file target), computed up front so exclusion can be applied while RootDir.Create
        // is still discovering files, instead of after the fact.
        var exclusionRoot = Directory.Exists(path)
            ? Path.GetFullPath(path)
            : new FileInfo(path).DirectoryName!;

        var shouldExcludeFile = CreateFileExclusionSearch(exclusionRoot, excludeFiles);

        var dir = await RootDir
            .CreateAsync(path, shouldExcludeFile, _logger, cancellationToken)
            .ConfigureAwait(false);

        var result = new Result(path);

        var (nugetPackages, unauthorizedSources) = await GetPackageVersions(
            dir.Csprojs,
            dir.DotnetTools,
            dir.FileBasedApps,
            dir.PropsFiles,
            shouldIncludePackage,
            shouldExcludePackage,
            options.Interactive,
            options.NoCache,
            new NuGetLogger(_logger),
            cancellationToken
        );

        foreach (var unauthorizedSource in unauthorizedSources)
        {
            result.TryAddUnauthorizedSource(unauthorizedSource.Key, unauthorizedSource.Value);
        }

        foreach (var csproj in dir.Csprojs)
        {
            await UpdateAndCollectAsync(
                csproj.UpdatePackagesAsync(
                    nugetPackages,
                    options.DryRun,
                    options.Prerelease,
                    _logger,
                    tfm,
                    allowedLicenses,
                    alignWithTfm,
                    packagePolicies
                )
            );
        }

        foreach (var propsFile in dir.PropsFiles)
        {
            await UpdateAndCollectAsync(
                propsFile.UpdatePackagesAsync(
                    nugetPackages,
                    options.DryRun,
                    options.Prerelease,
                    _logger,
                    tfm,
                    allowedLicenses,
                    alignWithTfm,
                    packagePolicies
                )
            );
        }

        foreach (var config in dir.DotnetTools)
        {
            await UpdateAndCollectAsync(
                config.UpdatePackagesAsync(
                    nugetPackages,
                    options.DryRun,
                    options.Prerelease,
                    _logger,
                    allowedLicenses,
                    alignWithTfm,
                    effectiveToolPackagePins,
                    packagePolicies
                )
            );
        }

        foreach (var fileBasedApp in dir.FileBasedApps)
        {
            await UpdateAndCollectAsync(
                fileBasedApp.UpdatePackagesAsync(
                    nugetPackages,
                    options.DryRun,
                    options.Prerelease,
                    _logger,
                    tfm,
                    allowedLicenses,
                    alignWithTfm,
                    packagePolicies
                )
            );
        }

        return Summary.Create(result, failOn, failOnIncomplete);

        async Task UpdateAndCollectAsync(Task<ProjectWithPackages?> updateTask)
        {
            var project = await updateTask;

            if (project is not null)
            {
                result.TryAddProject(project);
            }
        }
    }

    private static NuGetFramework? ParseTFM(string? targetFrameworkMoniker)
    {
        var tfm = string.IsNullOrWhiteSpace(targetFrameworkMoniker)
            ? null
            : NuGetFramework.Parse(targetFrameworkMoniker);

        if (tfm == NuGetFramework.UnsupportedFramework)
        {
            throw new ArgumentException(
                $"'{targetFrameworkMoniker}' is not a supported TFM.",
                nameof(targetFrameworkMoniker)
            );
        }

        return tfm;
    }

    private static Func<string, bool> CreateSearch(string[]? strs, bool treatNullOrEmptyAs) =>
        SearchPattern.CreateSearch(strs, treatNullOrEmptyAs);

    /// <summary>
    /// Creates a predicate matching a file's full path against <paramref name="excludeFiles"/>
    /// patterns, evaluated against the file's path relative to <paramref name="root"/> (with
    /// directory separators normalized to <c>/</c>).
    /// </summary>
    private static Func<string, bool> CreateFileExclusionSearch(string root, string[]? excludeFiles)
    {
        if (excludeFiles is null || excludeFiles.Length == 0)
        {
            return _ => false;
        }

        var regexes = excludeFiles
            .Select(x => SearchPattern.ConvertToRegex(x.Replace('\\', '/')))
            .ToList();

        return filePath =>
        {
            var relative = Path.GetRelativePath(root, filePath).Replace('\\', '/');

            return regexes.Any(x => x.IsMatch(relative));
        };
    }

    private static bool PathsAreEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            PathComparer.Comparison
        );

    private async Task<(
        IDictionary<string, NuGetPackage?> Packages,
        IDictionary<string, string> UnauthorizedSources
    )> GetPackageVersions(
        IEnumerable<Csproj> projects,
        IEnumerable<DotnetTools> dotnetTools,
        IEnumerable<FileBasedApp> fileBasedApps,
        IEnumerable<PropsFile> propsFiles,
        Func<string, bool> shouldIncludePackage,
        Func<string, bool> shouldExcludePackage,
        bool interactive,
        bool noCache,
        NuGet.Common.ILogger nuGetLogger,
        CancellationToken cancellationToken
    )
    {
        DefaultCredentialServiceUtility.SetupDefaultCredentialService(nuGetLogger, !interactive);

        using var cacheContext = new SourceCacheContext
        {
            NoCache = noCache,
            RefreshMemoryCache = noCache,
        };

        Dictionary<string, NuGetPackage?> packageSearchMetadata = new(
            StringComparer.OrdinalIgnoreCase
        );

        Dictionary<string, string> unauthorizedSources = new(StringComparer.OrdinalIgnoreCase);

        var projectsWithPackages = projects
            .Select(x => (x.Path, x.Packages.Keys.AsEnumerable()))
            .Union(dotnetTools.Select(x => (x.Path, x.PackageIds)))
            .Union(fileBasedApps.Select(x => (x.Path, x.Packages.Keys.AsEnumerable())))
            .Union(propsFiles.Select(x => (x.Path, x.Packages.Keys.AsEnumerable())));

        // Settings.LoadDefaultSettings walks up the directory tree from its root argument
        // looking for nuget.config files, so every project under the same directory resolves to
        // the exact same sources - caching the resulting provider per directory avoids repeating
        // that walk (and rebuilding the NuGet plumbing on top of it) for every single project.
        Dictionary<string, SourceRepositoryProvider> sourceRepositoryProvidersByDir = new(
            PathComparer.Comparer
        );

        // Package Source Mapping (nuget.config's <packageSourceMapping>) restricts which
        // source(s) a given packageId may be resolved from. Cached alongside the
        // SourceRepositoryProvider per directory for the same reason - it comes from the same
        // Settings and is expensive to rebuild per project.
        Dictionary<string, PackageSourceMapping> packageSourceMappingsByDir = new(
            PathComparer.Comparer
        );

        // A distinct (source, packageId) work set, built up front across every project, so a
        // packageId referenced by many projects that all resolve to the same source is only
        // queried once instead of once per referencing project - the source of the original
        // O(projects x sources x packages) cost. Keyed by the source's URL (rather than the
        // SourceRepositoryProvider it came from) so the same source configured for two different
        // directories - e.g. nuget.org showing up in two independent nuget.config chains - is
        // still only queried once.
        Dictionary<string, SourceRepository> reposBySourceUrl = new(
            StringComparer.OrdinalIgnoreCase
        );

        Dictionary<string, HashSet<string>> packageIdsBySourceUrl = new(
            StringComparer.OrdinalIgnoreCase
        );

        foreach (var (path, packageIds) in projectsWithPackages)
        {
            var configDir = System.IO.Path.GetDirectoryName(path) ?? path;

            if (
                !sourceRepositoryProvidersByDir.TryGetValue(
                    configDir,
                    out var sourceRepositoryProvider
                )
            )
            {
                var settings = Settings.LoadDefaultSettings(path);

                var packageSourceProvider = new PackageSourceProvider(settings);

                sourceRepositoryProvider = new SourceRepositoryProvider(
                    packageSourceProvider,
                    Repository.Provider.GetCoreV3()
                );

                sourceRepositoryProvidersByDir[configDir] = sourceRepositoryProvider;
                packageSourceMappingsByDir[configDir] =
                    PackageSourceMapping.GetPackageSourceMapping(settings);
            }

            var packageSourceMapping = packageSourceMappingsByDir[configDir];

            foreach (
                var repo in sourceRepositoryProvider
                    .GetRepositories()
                    .Where(x => x.PackageSource.IsEnabled)
            )
            {
                var sourceUrl = repo.PackageSource.Source;

                reposBySourceUrl.TryAdd(sourceUrl, repo);

                if (!packageIdsBySourceUrl.TryGetValue(sourceUrl, out var packageIdSet))
                {
                    packageIdsBySourceUrl[sourceUrl] = packageIdSet = new HashSet<string>(
                        StringComparer.OrdinalIgnoreCase
                    );
                }

                foreach (var packageId in packageIds)
                {
                    if (!shouldIncludePackage(packageId) || shouldExcludePackage(packageId))
                    {
                        packageSearchMetadata[packageId] = null;

                        continue;
                    }

                    // packageSourceMapping.IsEnabled is false when nuget.config has no
                    // <packageSourceMapping> section at all, in which case every source applies
                    // to every package - matching NuGet's own restore/install behaviour.
                    if (
                        packageSourceMapping.IsEnabled
                        && !packageSourceMapping
                            .GetConfiguredPackageSources(packageId)
                            .Contains(repo.PackageSource.Name, StringComparer.OrdinalIgnoreCase)
                    )
                    {
                        continue;
                    }

                    packageIdSet.Add(packageId);
                }
            }
        }

        var packageSearchMetadataLock = new Lock();

        foreach (var (sourceUrl, repo) in reposBySourceUrl)
        {
            if (unauthorizedSources.ContainsKey(repo.PackageSource.Name))
            {
                continue;
            }

            try
            {
                await Parallel.ForEachAsync(
                    packageIdsBySourceUrl[sourceUrl],
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = MaxConcurrentNuGetRequests,
                        CancellationToken = cancellationToken,
                    },
                    async (packageId, nugetCancellationToken) =>
                    {
                        var packageMetadataResource = repo.GetResource<PackageMetadataResource>()!;

                        var searchMetadata = await packageMetadataResource.GetMetadataAsync(
                            packageId,
                            includePrerelease: true,
                            includeUnlisted: false,
                            cacheContext,
                            nuGetLogger,
                            nugetCancellationToken
                        );

                        var metadata = searchMetadata
                            .OfType<IPackageSearchMetadata>()
                            .Where(x => x.Identity.HasVersion)
                            .Select(x => new PackageMetadata(
                                x.Identity.Version,
                                x.DependencySets.Select(x => x.TargetFramework),
                                x is PackageSearchMetadata y && y.DeprecationMetadata is not null
                                    ? new(
                                        y.DeprecationMetadata.Message,
                                        y.DeprecationMetadata.Reasons,
                                        y.DeprecationMetadata.AlternatePackage is null
                                            ? null
                                            : new(
                                                y.DeprecationMetadata.AlternatePackage.PackageId!,
                                                y.DeprecationMetadata.AlternatePackage.Range!
                                            )
                                    )
                                    : null,
                                x.Vulnerabilities?.Select(y => new PackageVulnerabilityMetadata(
                                    y.AdvisoryUrl,
                                    y.Severity
                                )),
                                x.LicenseMetadata?.Type == NuGet.Packaging.LicenseType.Expression
                                    ? x.LicenseMetadata.License
                                    : null
                            ));

                        if (!metadata.Any())
                        {
                            return;
                        }

                        lock (packageSearchMetadataLock)
                        {
                            if (
                                packageSearchMetadata.TryGetValue(packageId, out var package)
                                && package is not null
                            )
                            {
                                packageSearchMetadata[packageId] = package.Merge(metadata);
                            }
                            else
                            {
                                packageSearchMetadata[packageId] = new(packageId, metadata);
                            }
                        }
                    }
                );
            }
            catch (AggregateException exception)
                when (exception.InnerException?.InnerException
                        is HttpRequestException httpRequestException
                    && httpRequestException.StatusCode == System.Net.HttpStatusCode.Unauthorized
                )
            {
                LogSourceFailure(repo.PackageSource.Name, repo.PackageSource.Source);

                unauthorizedSources.Add(repo.PackageSource.Name, repo.PackageSource.Source);

                continue;
            }
            catch (HttpRequestException exception)
                when (exception.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                LogSourceFailure(repo.PackageSource.Name, repo.PackageSource.Source);

                unauthorizedSources.Add(repo.PackageSource.Name, repo.PackageSource.Source);

                continue;
            }
        }

        return (packageSearchMetadata, unauthorizedSources);
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Failed to get package metadata from {Name} ({Source})"
    )]
    partial void LogSourceFailure(string name, string source);
}
