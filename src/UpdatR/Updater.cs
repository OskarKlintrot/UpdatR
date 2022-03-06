using BuildingBlocks;
using Microsoft.Extensions.Logging;
using NuGet.Configuration;
using NuGet.Credentials;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using UpdatR.Domain;
using UpdatR.Internals;

namespace UpdatR;

public sealed partial class Updater
{
    private readonly ILogger _logger;

    public Updater(ILogger<Updater>? logger = null)
    {
        _logger = logger ?? new Microsoft.Extensions.Logging.Abstractions.NullLogger<Updater>();
    }

    /// <summary>
    /// Update all packages in solution or project(s).
    /// </summary>
    /// <param name="path">Path to solution or project(s). Leave out if solution or project(s) is in current folder or if project(s) is in subfolders.</param>
    /// <returns><see cref="Summary"/></returns>
    /// <exception cref="ArgumentException"></exception>
    public async Task<Summary> UpdateAsync(string? path = null, bool dryRun = false, bool interactive = false)
    {
        if (path == null)
        {
            path = Directory.GetCurrentDirectory();
        }

        var dir = RootDir.Create(path);

        var result = new Result(path);

        var (packages, unauthorizedSources) = await GetPackageVersions(
            dir.Csprojs ?? Array.Empty<Csproj>(),
            dir.DotnetTools ?? Array.Empty<DotnetTools>(),
            interactive,
            new NuGetLogger(_logger));

        foreach (var unauthorizedSource in unauthorizedSources)
        {
            result.TryAddUnauthorizedSource(unauthorizedSource.Key, unauthorizedSource.Value);
        }

        foreach (var csproj in dir.Csprojs ?? Array.Empty<Csproj>())
        {
            var project = csproj.UpdatePackages(packages, dryRun, _logger);

            if (project is not null)
            {
                result.TryAddProject(project);
            }
        }

        foreach (var config in dir.DotnetTools ?? Array.Empty<DotnetTools>())
        {
            var project = await config.UpdatePackagesAsync(packages, dryRun, _logger);

            if (project is not null)
            {
                result.TryAddProject(project);
            }
        }

        return Summary.Create(result);
    }

    private async Task<(IEnumerable<NuGetPackage> Packages, IDictionary<string, string> UnauthorizedSources)>
        GetPackageVersions(
            IEnumerable<Csproj> projects,
            IEnumerable<DotnetTools> dotnetTools,
            bool interactive,
            NuGet.Common.ILogger nuGetLogger)
    {
        DefaultCredentialServiceUtility.SetupDefaultCredentialService(nuGetLogger, !interactive);

        using var cacheContext = new SourceCacheContext();

        Dictionary<string, NuGetPackage> packageSearchMetadata = new();

        Dictionary<string, string> unauthorizedSources = new();

        var projectsWithPackages = projects
            .Select(x => (x.Path, x.Packages.Keys.AsEnumerable()))
            .Union(dotnetTools.Select(x => (x.Path, x.PackageIds)));

        foreach (var (path, packageIds) in projectsWithPackages)
        {
            var settings = Settings.LoadDefaultSettings(path);

            var packageSourceProvider = new PackageSourceProvider(settings);

            var sourceRepositoryProvider = new SourceRepositoryProvider(packageSourceProvider, Repository.Provider.GetCoreV3());

            foreach (var repo in sourceRepositoryProvider
                .GetRepositories()
                .Where(x => !unauthorizedSources.ContainsKey(x.PackageSource.Name)))
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
                            nuGetLogger,
                            CancellationToken.None);

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
                                    : new(y.DeprecationMetadata.AlternatePackage.PackageId, y.DeprecationMetadata.AlternatePackage.Range))
                                : null,
                                x.Vulnerabilities?.Select(y => new PackageVulnerabilityMetadata(y.AdvisoryUrl, y.Severity))));

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
        }

        return (packageSearchMetadata.Values, unauthorizedSources);
    }

#pragma warning disable CA1822 // Mark members as static
    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to get package metadata from {Name} ({Source})")]
    partial void LogSourceFailure(string name, string source);
#pragma warning restore CA1822 // Mark members as static
}
