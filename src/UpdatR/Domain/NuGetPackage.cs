using System.Diagnostics.CodeAnalysis;
using NuGet.Frameworks;
using NuGet.Versioning;
using UpdatR.Internals;

namespace UpdatR.Domain;

[SuppressMessage("Style", "IDE0022:Use block body for method", Justification = "<Pending>")]
internal record NuGetPackage(string PackageId, IEnumerable<PackageMetadata> PackageMetadatas)
{
    private PackageMetadata? _latest;
    private PackageMetadata? _latestStable;
    private PackageMetadata? _latestPrerelease;
    private CompatibilityProvider? _compatibilityProvider;

    private CompatibilityProvider CompatibilityProvider =>
        _compatibilityProvider ??= new CompatibilityProvider(DefaultFrameworkNameProvider.Instance);

    private PackageMetadata? LatestStable(NuGetFramework targetFramework) =>
        _latestStable ??= Latest(targetFramework, x => !x.Version.IsPrerelease);

    private PackageMetadata? LatestPrerelease(NuGetFramework targetFramework) =>
        _latestPrerelease ??= Latest(targetFramework, x => x.Version.IsPrerelease);

    private PackageMetadata? Latest(NuGetFramework targetFramework) =>
        _latest ??= Latest(targetFramework, _ => true);

    private PackageMetadata? Latest(
        NuGetFramework targetFramework,
        Func<PackageMetadata, bool> predicate
    ) =>
        PackageMetadatas
            .Where(x => predicate(x) && IsCompatibleWithFramework(targetFramework, x)) // Todo: Bodge for tools
            .OrderByDescending(x => x.Version)
            .FirstOrDefault();

    private bool IsCompatibleWithFramework(
        NuGetFramework targetFramework,
        PackageMetadata package
    ) =>
        !package.TargetFrameworks.Any()
        || (
            package.TargetFrameworks.All(x =>
                x.Framework == ".NETStandard" || targetFramework.Framework != x.Framework
            )
                ? package.TargetFrameworks
                : package.TargetFrameworks.Where(x =>
                    targetFramework.Framework == ".NETStandard" || x.Framework != ".NETStandard"
                )
        ).Any(x => CompatibilityProvider.IsCompatible(targetFramework, x));

    /// <summary>
    /// Get latest stable if <paramref name="version"/> is stable and older than <see cref="LatestStable"/>.
    /// If <paramref name="version"/> is prerelase then take latest prerelease unless there is a newer stable version.
    /// </summary>
    /// <param name="version">Current version to compare to.</param>
    /// <param name="package"></param>
    /// <param name="usePrerelease">Use prerelase, even if <paramref name="version"/> is stable.</param>
    /// <returns><see langword="true"/> if a newer version is avalible.</returns>
    public bool TryGetLatestComparedTo(
        NuGetVersion version,
        NuGetFramework targetFramework,
        bool usePrerelease,
        [NotNullWhen(returnValue: true)] out PackageMetadata? package
    )
    {
        if (usePrerelease)
        {
            package = Latest(targetFramework)!;

            return true;
        }
        else if ((LatestStable(targetFramework)?.Version ?? NuGetVersion.Parse("0.0.0")) > version)
        {
            package = LatestStable(targetFramework)!;

            return true;
        }
        else if (
            version.IsPrerelease
            && (LatestPrerelease(targetFramework)?.Version ?? NuGetVersion.Parse("0.0.0")) > version
        )
        {
            package = LatestPrerelease(targetFramework)!;

            return true;
        }

        package = null;

        return false;
    }

    public bool TryGet(
        NuGetVersion version,
        [NotNullWhen(returnValue: true)] out PackageMetadata? package
    )
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
