using System.Diagnostics.CodeAnalysis;
using NuGet.Frameworks;
using NuGet.Versioning;
using UpdatR.Internals;

namespace UpdatR.Domain.Utils;

/// <summary>
/// Shared logic for resolving package updates that must be compatible with every target
/// framework of a multi-targeted <see cref="Csproj"/>, or every project that imports a shared
/// <see cref="PropsFile"/>.
/// </summary>
internal static class TargetFrameworkCompatibility
{
    /// <summary>
    /// Finds the latest version of <paramref name="package"/>, newer than <paramref name="from"/>,
    /// that every framework in <paramref name="tfms"/> can use. If any framework has no valid
    /// update (i.e. is already on the newest version it supports), no update is returned at all,
    /// since a version incompatible with even one of the target frameworks could break the build.
    /// Otherwise, the lowest of the per-framework results is returned, since that's guaranteed to
    /// be compatible with every framework in <paramref name="tfms"/>.
    /// </summary>
    public static bool TryGetLatestCompatibleWithAllTfms(
        NuGetPackage package,
        NuGetVersion from,
        IReadOnlyCollection<NuGetFramework> tfms,
        bool usePrerelease,
        IReadOnlyCollection<string>? allowedLicenses,
        [NotNullWhen(returnValue: true)] out PackageMetadata? updateTo
    )
    {
        PackageMetadata? lowestCommonUpdate = null;

        foreach (var candidateTfm in tfms)
        {
            if (
                !package.TryGetLatestComparedTo(
                    from,
                    candidateTfm,
                    usePrerelease,
                    out var updateToForTfm,
                    allowedLicenses
                )
            )
            {
                updateTo = null;

                return false;
            }

            if (lowestCommonUpdate is null || updateToForTfm.Version < lowestCommonUpdate.Version)
            {
                lowestCommonUpdate = updateToForTfm;
            }
        }

        updateTo = lowestCommonUpdate;

        return updateTo is not null;
    }
}
