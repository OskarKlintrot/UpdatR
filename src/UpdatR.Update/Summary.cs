using NuGet.Protocol;
using NuGet.Versioning;
using UpdatR.Update.Internals;

namespace UpdatR.Update;

public sealed class Summary
{
    public Summary(
        IEnumerable<UpdatedPackage> updatedPackages,
        IEnumerable<DeprecatedPackage> deprecatedPackages,
        IEnumerable<VulnerablePackage> vulnerablePackages)
    {
        UpdatedPackages = updatedPackages;
        DeprecatedPackages = deprecatedPackages;
        VulnerablePackages = vulnerablePackages;
    }

    public IEnumerable<UpdatedPackage> UpdatedPackages { get; }
    public IEnumerable<DeprecatedPackage> DeprecatedPackages { get; }
    public IEnumerable<VulnerablePackage> VulnerablePackages { get; }

    internal static Summary Create(Result summary)
    {
        var updatedPackagesCount = summary.Projects
            .SelectMany(x => x.UpdatedPackages)
            .DistinctBy(x => x.PackageId)
            .Count();

        var deprecatedPackages = summary.Projects
            .SelectMany(x => x.DeprecatedPackages.Select(y => (Package: y, Project: x.Path)))
            .GroupBy(x => x.Package.PackageId)
            .Select(x => (PackageId: x.Key, Versions: x.GroupBy(y => y.Package.Version)))
            .Select(x => (
                x.PackageId,
                Versions: x.Versions.Select(y => (
                    y.Key,
                    y.First().Package.DeprecationMetadata,
                    Projects: y.Select(z => z.Project)))))
            .Select(x =>
                new DeprecatedPackage(
                    x.PackageId,
                    x.Versions.Select(y => (
                        new DeprecatedVersion(y.Key, y.DeprecationMetadata),
                        y.Projects))));

        var vulnerablePackages = summary.Projects
            .SelectMany(x => x.VulnerablePackages.Select(y => (Package: y, Project: x.Path)))
            .GroupBy(x => x.Package.PackageId)
            .Select(x => (PackageId: x.Key, Versions: x.GroupBy(y => y.Package.Version)))
            .Select(x => (
                x.PackageId,
                Versions: x.Versions.Select(y => (
                    y.Key,
                    y.First().Package.Vulnerabilities,
                    Projects: y.Select(z => z.Project)))))
            .Select(x =>
                new VulnerablePackage(
                    x.PackageId,
                    x.Versions.Select(y => (
                        new VulnerableVersion(y.Key, y.Vulnerabilities),
                        y.Projects))));

        var updatedPackages = summary.Projects
            .SelectMany(x => x.UpdatedPackages.Select(y => (Package: y, Project: x.Path)))
            .GroupBy(x => x.Package.PackageId)
            .Select(x => new UpdatedPackage(PackageId: x.Key, Updates: x.Select(y => (y.Package.From, y.Package.To, y.Project))));

        return new Summary(updatedPackages, deprecatedPackages, vulnerablePackages);
    }
}

public sealed record UpdatedPackage(
    string PackageId,
    IEnumerable<(NuGetVersion From, NuGetVersion To, string Project)> Updates);

public sealed record DeprecatedPackage(
    string PackageId,
    IEnumerable<(DeprecatedVersion Version, IEnumerable<string> Projects)> Versions);
public sealed record DeprecatedVersion(NuGetVersion NuGetVersion, PackageDeprecationMetadata DeprecationMetadata);

public sealed record VulnerablePackage(
    string PackageId,
    IEnumerable<(VulnerableVersion Version, IEnumerable<string> Projects)> Versions);
public sealed record VulnerableVersion(NuGetVersion Version, IEnumerable<PackageVulnerabilityMetadata> Vulnerabilities);
