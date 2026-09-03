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
    /// Update all packages in solution or project(s).
    /// </summary>
    /// <param name="path">Path to solution or project(s). Leave out if solution or project(s) is in current folder or if project(s) is in subfolders.</param>
    /// <param name="excludePackages">Packages to exlude. Supports * as wildcard.</param>
    /// <param name="packages">Packages to update. Supports * as wildcard. If <see langword="null"/> or empty then all packages, except <paramref name="excludePackages"/>, will be updated.</param>
    /// <param name="dryRun">Do not save any changes.</param>
    /// <param name="prerelease">Allow prerelease packages to be installed.</param>
    /// <param name="interactive">Interaction with user is possible.</param>
    /// <param name="targetFrameworkMoniker">Lowest Target Framework Moniker to support.</param>
    /// <param name="allowedLicenses">
    /// If specified, a package is only updated to a version whose license expression contains one
    /// of these values (case-insensitive substring match). A warning is logged - and included in
    /// the <see cref="Summary"/> - both when the currently installed version's license isn't
    /// allowed, and when a newer version exists but was skipped because its license isn't
    /// allowed. Packages without any license metadata are always allowed. Leave out or empty to
    /// disable license checking.
    /// </param>
    /// <param name="excludeFiles">
    /// Csproj-, dotnet-tools.json-, props/targets- and file-based app files to exclude from being
    /// processed altogether, matched against each file's path relative to the resolved
    /// <paramref name="path"/>. Supports * as wildcard.
    /// </param>
    /// <param name="alignWithTfm">
    /// Packages to keep aligned with a project's target framework's major version, instead of
    /// updating to a newer version whose major just happens to also be compatible (e.g. a package
    /// that multi-targets both <c>net9.0</c> and <c>net10.0</c> in the same, higher-major,
    /// release). Supports * as wildcard. Only applies to modern (<c>net5.0</c>+) target
    /// frameworks, and only if the currently installed version's major isn't already ahead of the
    /// target framework's - if it is, updates are left unrestricted. Also applies to
    /// <c>dotnet-tools.json</c> entries, aligned with the target framework(s) of the csproj(s)
    /// the manifest applies to (e.g. keeping <c>dotnet-ef</c> in step with
    /// <c>Microsoft.EntityFrameworkCore</c>).
    /// </param>
    /// <remarks>
    /// If a <c>.updatrrc</c> JSON file is found - first next to <paramref name="path"/>, then in
    /// the current working directory - its <c>excludePackages</c>, <c>allowedLicenses</c>,
    /// <c>excludeFiles</c> and <c>alignWithTfm</c> values are merged (union) with
    /// <paramref name="excludePackages"/>, <paramref name="allowedLicenses"/>,
    /// <paramref name="excludeFiles"/> and <paramref name="alignWithTfm"/> respectively. If
    /// <paramref name="path"/> is left out (i.e. it resolves to the current directory) and the
    /// config file has a <c>defaultTarget</c>, that's used as the target path instead of the
    /// current directory.
    /// </remarks>
    /// <returns><see cref="Summary"/></returns>
    /// <exception cref="ArgumentException"></exception>
    public async Task<Summary> UpdateAsync(
        string? path = null,
        string[]? excludePackages = null,
        string[]? packages = null,
        bool dryRun = false,
        bool prerelease = false,
        bool interactive = false,
        string? targetFrameworkMoniker = null,
        string[]? allowedLicenses = null,
        string[]? excludeFiles = null,
        string[]? alignWithTfm = null
    )
    {
        var tfm = ParseTFM(targetFrameworkMoniker);

        path ??= Directory.GetCurrentDirectory();

        var updatRConfig = UpdatRConfig.Load(path, out var configDirectory);

        excludePackages = UpdatRConfig.Merge(excludePackages, updatRConfig?.ExcludePackages);
        allowedLicenses = UpdatRConfig.Merge(allowedLicenses, updatRConfig?.AllowedLicenses);
        excludeFiles = UpdatRConfig.Merge(excludeFiles, updatRConfig?.ExcludeFiles);
        alignWithTfm = UpdatRConfig.Merge(alignWithTfm, updatRConfig?.AlignWithTfm);

        if (
            !string.IsNullOrWhiteSpace(updatRConfig?.DefaultTarget)
            && configDirectory is not null
            && PathsAreEqual(path, Directory.GetCurrentDirectory())
        )
        {
            path = UpdatRConfig.ResolveDefaultTarget(configDirectory, updatRConfig.DefaultTarget);

            if (!Directory.Exists(path) && !File.Exists(path))
            {
                throw new ArgumentException(
                    $"'{nameof(UpdatRConfig.DefaultTarget)}' (\"defaultTarget\") in '{Path.Combine(configDirectory, UpdatRConfig.FileName)}' resolved to '{path}', which does not exist.",
                    nameof(path)
                );
            }
        }

        var shouldIncludePackage = CreateSearch(packages, treatNullOrEmptyAs: true);
        var shouldExcludePackage = CreateSearch(excludePackages, treatNullOrEmptyAs: false);

        var dir = RootDir.Create(path);

        if (excludeFiles is { Length: > 0 })
        {
            var shouldExcludeFile = CreateFileExclusionSearch(dir.Path, excludeFiles);

            RemoveExcludedFiles(dir.Csprojs, x => x.Path, shouldExcludeFile);
            RemoveExcludedFiles(dir.DotnetTools, x => x.Path, shouldExcludeFile);
            RemoveExcludedFiles(dir.FileBasedApps, x => x.Path, shouldExcludeFile);
            RemoveExcludedFiles(dir.PropsFiles, x => x.Path, shouldExcludeFile);
        }

        var result = new Result(path);

        var (nugetPackages, unauthorizedSources) = await GetPackageVersions(
            dir.Csprojs ?? Array.Empty<Csproj>(),
            dir.DotnetTools ?? Array.Empty<DotnetTools>(),
            dir.FileBasedApps ?? Array.Empty<FileBasedApp>(),
            dir.PropsFiles ?? Array.Empty<PropsFile>(),
            shouldIncludePackage,
            shouldExcludePackage,
            interactive,
            new NuGetLogger(_logger)
        );

        foreach (var unauthorizedSource in unauthorizedSources)
        {
            result.TryAddUnauthorizedSource(unauthorizedSource.Key, unauthorizedSource.Value);
        }

        foreach (var csproj in dir.Csprojs ?? Array.Empty<Csproj>())
        {
            var project = csproj.UpdatePackages(
                nugetPackages,
                dryRun,
                prerelease,
                _logger,
                tfm,
                allowedLicenses,
                alignWithTfm
            );

            if (project is not null)
            {
                result.TryAddProject(project);
            }
        }

        foreach (var propsFile in dir.PropsFiles ?? Array.Empty<PropsFile>())
        {
            var project = propsFile.UpdatePackages(
                nugetPackages,
                dryRun,
                prerelease,
                _logger,
                tfm,
                allowedLicenses,
                alignWithTfm
            );

            if (project is not null)
            {
                result.TryAddProject(project);
            }
        }

        foreach (var config in dir.DotnetTools ?? Array.Empty<DotnetTools>())
        {
            var project = await config.UpdatePackagesAsync(
                nugetPackages,
                dryRun,
                prerelease,
                _logger,
                alignWithTfm
            );

            if (project is not null)
            {
                result.TryAddProject(project);
            }
        }

        foreach (var fileBasedApp in dir.FileBasedApps ?? Array.Empty<FileBasedApp>())
        {
            var project = await fileBasedApp.UpdatePackagesAsync(
                nugetPackages,
                dryRun,
                prerelease,
                _logger,
                tfm,
                allowedLicenses,
                alignWithTfm
            );

            if (project is not null)
            {
                result.TryAddProject(project);
            }
        }

        return Summary.Create(result);
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

    private static void RemoveExcludedFiles<T>(
        ICollection<T>? items,
        Func<T, string> pathSelector,
        Func<string, bool> shouldExcludeFile
    )
    {
        if (items is null)
        {
            return;
        }

        foreach (var item in items.Where(x => shouldExcludeFile(pathSelector(x))).ToList())
        {
            items.Remove(item);
        }
    }

    private static bool PathsAreEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase
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
        NuGet.Common.ILogger nuGetLogger
    )
    {
        DefaultCredentialServiceUtility.SetupDefaultCredentialService(nuGetLogger, !interactive);

        using var cacheContext = new SourceCacheContext();

        Dictionary<string, NuGetPackage?> packageSearchMetadata = new(
            StringComparer.OrdinalIgnoreCase
        );

        Dictionary<string, string> unauthorizedSources = new(StringComparer.OrdinalIgnoreCase);

        var projectsWithPackages = projects
            .Select(x => (x.Path, x.Packages.Keys.AsEnumerable()))
            .Union(dotnetTools.Select(x => (x.Path, x.PackageIds)))
            .Union(fileBasedApps.Select(x => (x.Path, x.Packages.Keys.AsEnumerable())))
            .Union(propsFiles.Select(x => (x.Path, x.Packages.Keys.AsEnumerable())));

        foreach (var (path, packageIds) in projectsWithPackages)
        {
            var settings = Settings.LoadDefaultSettings(path);

            var packageSourceProvider = new PackageSourceProvider(settings);

            var sourceRepositoryProvider = new SourceRepositoryProvider(
                packageSourceProvider,
                Repository.Provider.GetCoreV3()
            );

            foreach (
                var repo in sourceRepositoryProvider
                    .GetRepositories()
                    .Where(x => x.PackageSource.IsEnabled)
                    .Where(x => !unauthorizedSources.ContainsKey(x.PackageSource.Name))
            )
            {
                try
                {
                    foreach (var packageId in packageIds)
                    {
                        if (!shouldIncludePackage(packageId) || shouldExcludePackage(packageId))
                        {
                            packageSearchMetadata[packageId] = null;

                            continue;
                        }

                        var packageMetadataResource = repo.GetResource<PackageMetadataResource>()!;

                        var searchMetadata = await packageMetadataResource.GetMetadataAsync(
                            packageId,
                            includePrerelease: true,
                            includeUnlisted: false,
                            cacheContext,
                            nuGetLogger,
                            CancellationToken.None
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
                            continue;
                        }

                        if (
                            packageSearchMetadata.TryGetValue(packageId, out var package)
                            && package is not null
                        )
                        {
                            packageSearchMetadata[packageId] = package with
                            {
                                PackageMetadatas = package
                                    .PackageMetadatas.Union(metadata)
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
                catch (Exception)
                {
                    throw;
                }
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
