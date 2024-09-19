using NuGet.Versioning;
using UpdatR.Internals;

namespace UpdatR;

public sealed class Summary(
    IDictionary<string, IEnumerable<string>> unknownPackages,
    IEnumerable<(string Name, string Source)> unauthorizedSources,
    IEnumerable<UpdatedPackage> updatedPackages,
    IEnumerable<DeprecatedPackage> deprecatedPackages,
    IEnumerable<VulnerablePackage> vulnerablePackages
    )
{
    private int? _updatedPackagesCount;

    public int UpdatedPackagesCount => _updatedPackagesCount ??= UpdatedPackages.Count();
    public IEnumerable<UpdatedPackage> UpdatedPackages { get; } = updatedPackages;
    public IEnumerable<DeprecatedPackage> DeprecatedPackages { get; } = deprecatedPackages;
    public IEnumerable<VulnerablePackage> VulnerablePackages { get; } = vulnerablePackages;

    /// <summary>
    /// PackageId as key and projects and value.
    /// </summary>
    public IDictionary<string, IEnumerable<string>> UnknownPackages { get; } = unknownPackages;

    /// <summary>
    /// Sources that failed to use due to 401.
    /// </summary>
    public IEnumerable<(string Name, string Source)> UnauthorizedSources { get; } = unauthorizedSources;

    internal static Summary Create(Result result)
    {
        var updatedPackages = result.Projects
            .SelectMany(x => x.UpdatedPackages.Select(y => (Package: y, Project: x.Path)))
            .GroupBy(x => x.Package.PackageId)
            .Select(
                x =>
                    new UpdatedPackage(
                        PackageId: x.Key,
                        Updates: x.Select(y => (y.Package.From, y.Package.To, y.Project))
                            .OrderBy(x => x.Project)
                    )
            );

        var deprecatedPackages = result.Projects
            .SelectMany(x => x.DeprecatedPackages.Select(y => (Package: y, Project: x.Path)))
            .GroupBy(x => x.Package.PackageId)
            .Select(x => (PackageId: x.Key, Versions: x.GroupBy(y => y.Package.Version)))
            .Select(
                x =>
                    (
                        x.PackageId,
                        Versions: x.Versions.Select(
                            y =>
                                (
                                    y.Key,
                                    y.First().Package.DeprecationMetadata,
                                    Projects: y.Select(z => z.Project)
                                )
                        )
                    )
            )
            .Select(
                x =>
                    new DeprecatedPackage(
                        x.PackageId,
                        x.Versions.Select(
                            y => (new DeprecatedVersion(y.Key, y.DeprecationMetadata), y.Projects)
                        )
                    )
            );

        var vulnerablePackages = result.Projects
            .SelectMany(x => x.VulnerablePackages.Select(y => (Package: y, Project: x.Path)))
            .GroupBy(x => x.Package.PackageId)
            .Select(x => (PackageId: x.Key, Versions: x.GroupBy(y => y.Package.Version)))
            .Select(
                x =>
                    (
                        x.PackageId,
                        Versions: x.Versions.Select(
                            y =>
                                (
                                    y.Key,
                                    y.First().Package.Vulnerabilities,
                                    Projects: y.Select(z => z.Project)
                                )
                        )
                    )
            )
            .Select(
                x =>
                    new VulnerablePackage(
                        x.PackageId,
                        x.Versions.Select(
                            y => (new VulnerableVersion(y.Key, y.Vulnerabilities), y.Projects)
                        )
                    )
            );

        return new Summary(
            result.UnknownPackages,
            result.UnauthorizedSources,
            updatedPackages,
            deprecatedPackages,
            vulnerablePackages
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

public sealed record PackageDeprecationMetadata(
    string Message,
    IEnumerable<string> Reasons,
    AlternatePackageMetadata? AlternatePackage
);

public sealed record AlternatePackageMetadata(string PackageId, VersionRange Range);

public sealed record PackageVulnerabilityMetadata(Uri AdvisoryUrl, int Severity);
