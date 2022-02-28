using System.Diagnostics.CodeAnalysis;
using NuGet.Versioning;
using UpdatR.Update.Internals;

namespace UpdatR.Update.Domain;

internal record NuGetPackage(string PackageId, IEnumerable<PackageMetadata> PackageMetadatas)
{
    private PackageMetadata? _latestStable;
    private PackageMetadata? _latestPrerelease;

    public PackageMetadata? LatestStable => _latestStable ??= PackageMetadatas
        .Where(x => !x.Version.IsPrerelease)
        .OrderByDescending(x => x.Version)
        .FirstOrDefault();

    public PackageMetadata? LatestPrerelease => _latestPrerelease ??= PackageMetadatas
        .Where(x => x.Version.IsPrerelease)
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

    public bool TryGet(NuGetVersion version, [NotNullWhen(returnValue: true)] out PackageMetadata? package)
    {
        package = PackageMetadatas.SingleOrDefault(x => x.Version == version);

        return package != null;
    }
}
