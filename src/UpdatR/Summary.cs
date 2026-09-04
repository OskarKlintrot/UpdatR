using NuGet.Versioning;
using UpdatR.Internals;

namespace UpdatR;

public sealed class Summary(
    IDictionary<string, IEnumerable<string>> unknownPackages,
    IEnumerable<(string Name, string Source)> unauthorizedSources,
    IEnumerable<UpdatedPackage> updatedPackages,
    IEnumerable<DeprecatedPackage> deprecatedPackages,
    IEnumerable<VulnerablePackage> vulnerablePackages,
    IEnumerable<LicenseMismatchPackage> licenseMismatchPackages,
    IEnumerable<UnsupportedRangePackage> unsupportedRangePackages,
    IEnumerable<SkippedUpdatePackage> skippedUpdatePackages,
    FailOn failOn = FailOn.None,
    bool failOnIncomplete = false
)
{
    private int? _updatedPackagesCount;

    public int UpdatedPackagesCount => _updatedPackagesCount ??= UpdatedPackages.Count();
    public IEnumerable<UpdatedPackage> UpdatedPackages { get; } = [.. updatedPackages];
    public IEnumerable<DeprecatedPackage> DeprecatedPackages { get; } = [.. deprecatedPackages];
    public IEnumerable<VulnerablePackage> VulnerablePackages { get; } = [.. vulnerablePackages];
    public IEnumerable<LicenseMismatchPackage> LicenseMismatchPackages { get; } =
    [.. licenseMismatchPackages];

    /// <summary>
    /// The <see cref="UpdatR.FailOn"/> threshold this summary was created with, see
    /// <see cref="ShouldFail"/>.
    /// </summary>
    public FailOn FailOn { get; } = failOn;

    /// <summary>
    /// Whether an incomplete run - one with <see cref="UnauthorizedSources"/> or
    /// <see cref="UnknownPackages"/> - should also make <see cref="ShouldFail"/>
    /// <see langword="true"/>. Independent of <see cref="FailOn"/>, since "I couldn't check" is a
    /// different signal than "I checked and found something".
    /// </summary>
    public bool FailOnIncomplete { get; } = failOnIncomplete;

    /// <summary>
    /// <see langword="true"/> if <see cref="FailOn"/> is set to a severity that this summary's
    /// findings meet or exceed - e.g. any <see cref="VulnerablePackages"/> satisfies
    /// <see cref="UpdatR.FailOn.Outdated"/>, <see cref="UpdatR.FailOn.Deprecated"/> and
    /// <see cref="UpdatR.FailOn.Vulnerable"/> alike - or if <see cref="FailOnIncomplete"/> is set
    /// and the run was incomplete. Intended for CI gating: the run itself succeeded, but found
    /// something the caller asked to fail on.
    /// </summary>
    public bool ShouldFail =>
        (FailOnIncomplete && (UnauthorizedSources.Any() || UnknownPackages.Count > 0))
        || FailOn switch
        {
            FailOn.Vulnerable => VulnerablePackages.Any(),
            FailOn.Deprecated => VulnerablePackages.Any() || DeprecatedPackages.Any(),
            FailOn.Outdated => VulnerablePackages.Any()
                || DeprecatedPackages.Any()
                || UpdatedPackagesCount > 0,
            _ => false,
        };

    /// <summary>
    /// Version ranges and floating versions that UpdatR doesn't know how to safely rewrite
    /// (e.g. fixed ranges like "[1.0,2.0)", or prerelease floats like "1.0.*-*"), even though a
    /// newer version may be available. These are left untouched and must be updated manually.
    /// </summary>
    public IEnumerable<UnsupportedRangePackage> UnsupportedRangePackages { get; } =
    [.. unsupportedRangePackages];

    /// <summary>
    /// Updates that exist on the package source(s) but weren't applied because they're capped by
    /// <c>alignWithTfm</c>, or because they're incompatible with one of the affected project(s)'
    /// target framework(s) - see <see cref="SkippedUpdateReason"/>. Unlike
    /// <see cref="LicenseMismatchPackages"/> and <see cref="UnsupportedRangePackages"/>, these
    /// updates aren't blocked by anything UpdatR can rewrite or that a caller can allow via
    /// configuration - they're informational only.
    /// </summary>
    public IEnumerable<SkippedUpdatePackage> SkippedUpdatePackages { get; } =
    [.. skippedUpdatePackages];

    /// <summary>
    /// PackageId as key and projects and value.
    /// </summary>
    public IDictionary<string, IEnumerable<string>> UnknownPackages { get; } = unknownPackages;

    /// <summary>
    /// Sources that failed to use due to 401.
    /// </summary>
    public IEnumerable<(string Name, string Source)> UnauthorizedSources { get; } =
    [.. unauthorizedSources];

    internal static Summary Create(
        Result result,
        FailOn failOn = FailOn.None,
        bool failOnIncomplete = false
    )
    {
        var updatedPackages = result
            .Projects.SelectMany(x => x.UpdatedPackages.Select(y => (Package: y, Project: x.Path)))
            .GroupBy(x => x.Package.PackageId)
            .Select(x => new UpdatedPackage(
                PackageId: x.Key,
                Updates: x.Select(y => (y.Package.From, y.Package.To, y.Project))
                    .OrderBy(x => x.Project)
            ));

        var deprecatedPackages = result
            .Projects.SelectMany(x =>
                x.DeprecatedPackages.Select(y => (Package: y, Project: x.Path))
            )
            .GroupBy(x => x.Package.PackageId)
            .Select(x => (PackageId: x.Key, Versions: x.GroupBy(y => y.Package.Version)))
            .Select(x =>
                (
                    x.PackageId,
                    Versions: x.Versions.Select(y =>
                        (
                            y.Key,
                            y.First().Package.DeprecationMetadata,
                            Projects: y.Select(z => z.Project)
                        )
                    )
                )
            )
            .Select(x => new DeprecatedPackage(
                x.PackageId,
                x.Versions.Select(y =>
                    (new DeprecatedVersion(y.Key, y.DeprecationMetadata), y.Projects)
                )
            ));

        var vulnerablePackages = result
            .Projects.SelectMany(x =>
                x.VulnerablePackages.Select(y => (Package: y, Project: x.Path))
            )
            .GroupBy(x => x.Package.PackageId)
            .Select(x => (PackageId: x.Key, Versions: x.GroupBy(y => y.Package.Version)))
            .Select(x =>
                (
                    x.PackageId,
                    Versions: x.Versions.Select(y =>
                        (
                            y.Key,
                            y.First().Package.Vulnerabilities,
                            Projects: y.Select(z => z.Project)
                        )
                    )
                )
            )
            .Select(x => new VulnerablePackage(
                x.PackageId,
                x.Versions.Select(y =>
                    (new VulnerableVersion(y.Key, y.Vulnerabilities), y.Projects)
                )
            ));

        var licenseMismatchPackages = result
            .Projects.SelectMany(x =>
                x.LicenseMismatchPackages.Select(y => (Package: y, Project: x.Path))
            )
            .GroupBy(x => x.Package.PackageId)
            .Select(x =>
                (
                    PackageId: x.Key,
                    Versions: x.GroupBy(y => (y.Package.Version, y.Package.IsInstalledVersion))
                )
            )
            .Select(x =>
                (
                    x.PackageId,
                    Versions: x.Versions.Select(y =>
                        (
                            y.Key.Version,
                            y.First().Package.License,
                            y.Key.IsInstalledVersion,
                            Projects: y.Select(z => z.Project)
                        )
                    )
                )
            )
            .Select(x => new LicenseMismatchPackage(
                x.PackageId,
                x.Versions.Select(y =>
                    (
                        new LicenseMismatchVersion(y.Version, y.License, y.IsInstalledVersion),
                        y.Projects
                    )
                )
            ));

        var unsupportedRangePackages = result
            .Projects.SelectMany(x =>
                x.UnsupportedRangePackages.Select(y => (Package: y, Project: x.Path))
            )
            .GroupBy(x => x.Package.PackageId)
            .Select(x => (PackageId: x.Key, Ranges: x.GroupBy(y => y.Package.VersionRange)))
            .Select(x => new UnsupportedRangePackage(
                x.PackageId,
                x.Ranges.Select(y => (y.Key, Projects: y.Select(z => z.Project)))
            ));

        var skippedUpdatePackages = result
            .Projects.SelectMany(x =>
                x.SkippedUpdatePackages.Select(y => (Package: y, Project: x.Path))
            )
            .GroupBy(x => x.Package.PackageId)
            .Select(x =>
                (PackageId: x.Key, Versions: x.GroupBy(y => (y.Package.Version, y.Package.Reason)))
            )
            .Select(x => new SkippedUpdatePackage(
                x.PackageId,
                x.Versions.Select(y =>
                    (
                        new SkippedUpdateVersion(y.Key.Version, y.Key.Reason),
                        Projects: y.Select(z => z.Project)
                    )
                )
            ));

        return new Summary(
            result.UnknownPackages,
            result.UnauthorizedSources,
            updatedPackages,
            deprecatedPackages,
            vulnerablePackages,
            licenseMismatchPackages,
            unsupportedRangePackages,
            skippedUpdatePackages,
            failOn,
            failOnIncomplete
        );
    }
}

public sealed record UpdatedPackage(
    string PackageId,
    IEnumerable<(NuGetVersion From, NuGetVersion To, string Project)> Updates
);

public sealed record DeprecatedPackage(
    string PackageId,
    IEnumerable<(DeprecatedVersion Version, IEnumerable<string> Projects)> Versions
);

public sealed record DeprecatedVersion(
    NuGetVersion NuGetVersion,
    PackageDeprecationMetadata DeprecationMetadata
);

public sealed record VulnerablePackage(
    string PackageId,
    IEnumerable<(VulnerableVersion Version, IEnumerable<string> Projects)> Versions
);

public sealed record VulnerableVersion(
    NuGetVersion Version,
    IEnumerable<PackageVulnerabilityMetadata> Vulnerabilities
);

public sealed record LicenseMismatchPackage(
    string PackageId,
    IEnumerable<(LicenseMismatchVersion Version, IEnumerable<string> Projects)> Versions
);

public sealed record LicenseMismatchVersion(
    NuGetVersion NuGetVersion,
    string License,
    bool IsInstalledVersion
);

public sealed record UnsupportedRangePackage(
    string PackageId,
    IEnumerable<(string VersionRange, IEnumerable<string> Projects)> Ranges
);

public sealed record SkippedUpdatePackage(
    string PackageId,
    IEnumerable<(SkippedUpdateVersion Version, IEnumerable<string> Projects)> Versions
);

public sealed record SkippedUpdateVersion(NuGetVersion NuGetVersion, SkippedUpdateReason Reason);

public sealed record PackageDeprecationMetadata(
    string? Message,
    IEnumerable<string> Reasons,
    AlternatePackageMetadata? AlternatePackage
);

public sealed record AlternatePackageMetadata(string PackageId, VersionRange Range);

public sealed record PackageVulnerabilityMetadata(Uri AdvisoryUrl, int Severity);
