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
        [NotNullWhen(returnValue: true)] out PackageMetadata? updateTo,
        int? maxMajor = null
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
                    allowedLicenses,
                    maxMajor
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

    /// <summary>
    /// Finds the latest version of <paramref name="package"/>, newer than <paramref name="from"/>,
    /// ignoring target framework compatibility entirely - only <paramref name="allowedLicenses"/>
    /// and <paramref name="maxMajor"/> are honoured. Used to tell whether a version that
    /// <see cref="TryGetLatestCompatibleWithAllTfms"/> skipped exists at all (and would've been
    /// picked if it weren't incompatible with one of the target frameworks), as opposed to no
    /// newer version existing in the first place.
    /// </summary>
    public static bool TryGetLatestIgnoringTfmCompatibility(
        NuGetPackage package,
        NuGetVersion from,
        bool usePrerelease,
        IReadOnlyCollection<string>? allowedLicenses,
        int? maxMajor,
        [NotNullWhen(returnValue: true)] out PackageMetadata? updateTo
    )
    {
        bool MatchesConstraints(PackageMetadata x) =>
            (maxMajor is null || x.Version.Major <= maxMajor)
            && NuGetPackage.IsLicenseAllowed(x, allowedLicenses);

        if (usePrerelease)
        {
            updateTo = package
                .PackageMetadatas.Where(MatchesConstraints)
                .OrderByDescending(x => x.Version)
                .FirstOrDefault();

            return updateTo is not null && updateTo.Version > from;
        }

        var latestStable = package
            .PackageMetadatas.Where(x => !x.Version.IsPrerelease && MatchesConstraints(x))
            .OrderByDescending(x => x.Version)
            .FirstOrDefault();

        if (latestStable is not null && latestStable.Version > from)
        {
            updateTo = latestStable;

            return true;
        }

        if (from.IsPrerelease)
        {
            var latestPrerelease = package
                .PackageMetadatas.Where(x => x.Version.IsPrerelease && MatchesConstraints(x))
                .OrderByDescending(x => x.Version)
                .FirstOrDefault();

            if (latestPrerelease is not null && latestPrerelease.Version > from)
            {
                updateTo = latestPrerelease;

                return true;
            }
        }

        updateTo = null;

        return false;
    }
}
