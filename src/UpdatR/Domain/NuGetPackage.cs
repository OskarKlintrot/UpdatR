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

    private PackageMetadata? LatestStable(
        NuGetFramework targetFramework,
        IReadOnlyCollection<string>? allowedLicenses = null
    )
    {
        if (allowedLicenses is null || allowedLicenses.Count == 0)
        {
            return _latestStable ??= Latest(targetFramework, x => !x.Version.IsPrerelease);
        }

        return Latest(
            targetFramework,
            x => !x.Version.IsPrerelease && IsLicenseAllowed(x, allowedLicenses)
        );
    }

    private PackageMetadata? LatestPrerelease(
        NuGetFramework targetFramework,
        IReadOnlyCollection<string>? allowedLicenses = null
    )
    {
        if (allowedLicenses is null || allowedLicenses.Count == 0)
        {
            return _latestPrerelease ??= Latest(targetFramework, x => x.Version.IsPrerelease);
        }

        return Latest(
            targetFramework,
            x => x.Version.IsPrerelease && IsLicenseAllowed(x, allowedLicenses)
        );
    }

    private PackageMetadata? Latest(
        NuGetFramework targetFramework,
        IReadOnlyCollection<string>? allowedLicenses = null
    )
    {
        if (allowedLicenses is null || allowedLicenses.Count == 0)
        {
            return _latest ??= Latest(targetFramework, _ => true);
        }

        return Latest(targetFramework, x => IsLicenseAllowed(x, allowedLicenses));
    }

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
    /// <param name="allowedLicenses">
    /// If specified, only versions whose license expression contains one of these values
    /// (case-insensitive substring match) are considered. Versions without any license
    /// information are always allowed.
    /// </param>
    /// <returns><see langword="true"/> if a newer version is avalible.</returns>
    public bool TryGetLatestComparedTo(
        NuGetVersion version,
        NuGetFramework targetFramework,
        bool usePrerelease,
        [NotNullWhen(returnValue: true)] out PackageMetadata? package,
        IReadOnlyCollection<string>? allowedLicenses = null
    )
    {
        if (usePrerelease)
        {
            package = Latest(targetFramework, allowedLicenses)!;

            return package is not null;
        }
        else if (
            (LatestStable(targetFramework, allowedLicenses)?.Version ?? NuGetVersion.Parse("0.0.0"))
            > version
        )
        {
            package = LatestStable(targetFramework, allowedLicenses)!;

            return true;
        }
        else if (
            version.IsPrerelease
            && (
                LatestPrerelease(targetFramework, allowedLicenses)?.Version
                ?? NuGetVersion.Parse("0.0.0")
            ) > version
        )
        {
            package = LatestPrerelease(targetFramework, allowedLicenses)!;

            return true;
        }

        package = null;

        return false;
    }

    /// <summary>
    /// Checks if the license of the currently installed <paramref name="version"/> is allowed.
    /// Always <see langword="true"/> if <paramref name="allowedLicenses"/> is <see langword="null"/>
    /// or empty, or if <paramref name="version"/>'s license is unknown.
    /// </summary>
    public bool IsLicenseAllowed(
        NuGetVersion version,
        IReadOnlyCollection<string>? allowedLicenses
    ) =>
        allowedLicenses is not { Count: > 0 }
        || !TryGet(version, out var metadata)
        || IsLicenseAllowed(metadata, allowedLicenses);

    /// <summary>
    /// Checks if <paramref name="package"/>'s license expression contains one of
    /// <paramref name="allowedLicenses"/> (case-insensitive substring match). Always
    /// <see langword="true"/> if <paramref name="allowedLicenses"/> is <see langword="null"/> or
    /// empty, or if <paramref name="package"/> has no license expression.
    /// </summary>
    public static bool IsLicenseAllowed(
        PackageMetadata package,
        IReadOnlyCollection<string>? allowedLicenses
    ) =>
        allowedLicenses is not { Count: > 0 }
        || string.IsNullOrWhiteSpace(package.LicenseExpression)
        || allowedLicenses.Any(allowed =>
            package.LicenseExpression.Contains(allowed, StringComparison.OrdinalIgnoreCase)
        );

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
