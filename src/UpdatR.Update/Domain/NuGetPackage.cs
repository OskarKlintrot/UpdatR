using System.Diagnostics.CodeAnalysis;
using NuGet.Frameworks;
using NuGet.Versioning;
using UpdatR.Update.Internals;

namespace UpdatR.Update.Domain;

internal record NuGetPackage(string PackageId, IEnumerable<PackageMetadata> PackageMetadatas)
{
    private PackageMetadata? _latestStable;
    private PackageMetadata? _latestPrerelease;
    private CompatibilityProvider? _compatibilityProvider;

    private CompatibilityProvider CompatibilityProvider
        => _compatibilityProvider ??= new CompatibilityProvider(DefaultFrameworkNameProvider.Instance);

    private PackageMetadata? LatestStable(NuGetFramework targetFramework) => _latestStable ??= PackageMetadatas
        .Where(x => !x.Version.IsPrerelease
            && (!x.TargetFrameworks.Any() || x.TargetFrameworks.Any(y => CompatibilityProvider.IsCompatible(targetFramework, y)))) // Todo: Bodge for tools
        .OrderByDescending(x => x.Version)
        .FirstOrDefault();

    private PackageMetadata? LatestPrerelease(NuGetFramework targetFramework) => _latestPrerelease ??= PackageMetadatas
        .Where(x => x.Version.IsPrerelease
            && (!x.TargetFrameworks.Any() || x.TargetFrameworks.Any(y => CompatibilityProvider.IsCompatible(targetFramework, y)))) // Todo: Bodge for tools
        .OrderByDescending(x => x.Version)
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
        NuGetFramework targetFramework,
        [NotNullWhen(returnValue: true)] out PackageMetadata? package)
    {
        if (LatestStable(targetFramework)?.Version > version)
        {
            package = LatestStable(targetFramework)!;

            return true;
        }
        else if (version.IsPrerelease && LatestPrerelease(targetFramework)?.Version > version)
        {
            package = LatestPrerelease(targetFramework)!;

            return true;
        }

        package = null;

        return false;
    }

    public bool TryGet(NuGetVersion version, [NotNullWhen(returnValue: true)] out PackageMetadata? package)
    {
        package = PackageMetadatas.SingleOrDefault(x => x.Version == version);

        return package != null;
    }

    public PackageMetadata Get(NuGetVersion version)
    {
        if (TryGet(version, out var metadata))
        {
            return metadata;
        }

        throw new InvalidOperationException($"Could not find version {version}.");
    }
}
