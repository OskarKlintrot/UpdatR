using System.Text.Json;
using System.Text.Json.Serialization;

namespace UpdatR.Formatters;

/// <summary>
/// Renders a <see cref="Summary"/> as machine-readable JSON, e.g. for <c>--output json</c> or
/// <c>--output-path out.json</c> consumption in CI.
/// </summary>
public static class JsonFormatter
{
    /// <summary>
    /// Version of the JSON shape produced by <see cref="Generate"/>. Bumped whenever the output
    /// changes in a way that could break a consumer, so CI scripts can detect it.
    /// </summary>
    public const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string Generate(Summary summary)
    {
        var dto = new JsonSummary(
            SchemaVersion: SchemaVersion,
            ShouldFail: summary.ShouldFail,
            FailOn: summary.FailOn,
            FailOnIncomplete: summary.FailOnIncomplete,
            UpdatedPackagesCount: summary.UpdatedPackagesCount,
            UpdatedPackages: summary
                .UpdatedPackages.Select(package => new JsonUpdatedPackage(
                    package.PackageId,
                    package
                        .Updates.Select(update => new JsonUpdatedPackageUpdate(
                            update.From.ToString(),
                            update.To.ToString(),
                            update.Project
                        ))
                        .ToArray()
                ))
                .ToArray(),
            DeprecatedPackages: summary
                .DeprecatedPackages.Select(package => new JsonDeprecatedPackage(
                    package.PackageId,
                    package
                        .Versions.Select(version => new JsonDeprecatedPackageVersion(
                            version.Version.NuGetVersion.ToString(),
                            version.Version.DeprecationMetadata.Message,
                            version.Version.DeprecationMetadata.Reasons.ToArray(),
                            version.Version.DeprecationMetadata.AlternatePackage is { } alt
                                ? new JsonAlternatePackage(alt.PackageId, alt.Range.ToString())
                                : null,
                            version.Projects.ToArray()
                        ))
                        .ToArray()
                ))
                .ToArray(),
            VulnerablePackages: summary
                .VulnerablePackages.Select(package => new JsonVulnerablePackage(
                    package.PackageId,
                    package
                        .Versions.Select(version => new JsonVulnerablePackageVersion(
                            version.Version.Version.ToString(),
                            version
                                .Version.Vulnerabilities.Select(
                                    vulnerability => new JsonVulnerability(
                                        vulnerability.AdvisoryUrl.ToString(),
                                        vulnerability.Severity
                                    )
                                )
                                .ToArray(),
                            version.Projects.ToArray()
                        ))
                        .ToArray()
                ))
                .ToArray(),
            LicenseMismatchPackages: summary
                .LicenseMismatchPackages.Select(package => new JsonLicenseMismatchPackage(
                    package.PackageId,
                    package
                        .Versions.Select(version => new JsonLicenseMismatchPackageVersion(
                            version.Version.NuGetVersion.ToString(),
                            version.Version.License,
                            version.Version.IsInstalledVersion,
                            version.Projects.ToArray()
                        ))
                        .ToArray()
                ))
                .ToArray(),
            UnsupportedRangePackages: summary
                .UnsupportedRangePackages.Select(package => new JsonUnsupportedRangePackage(
                    package.PackageId,
                    package
                        .Ranges.Select(range => new JsonUnsupportedRangePackageRange(
                            range.VersionRange,
                            range.Projects.ToArray()
                        ))
                        .ToArray()
                ))
                .ToArray(),
            SkippedUpdatePackages: summary
                .SkippedUpdatePackages.Select(package => new JsonSkippedUpdatePackage(
                    package.PackageId,
                    package
                        .Versions.Select(version => new JsonSkippedUpdatePackageVersion(
                            version.Version.NuGetVersion.ToString(),
                            version.Version.Reason,
                            version.Projects.ToArray()
                        ))
                        .ToArray()
                ))
                .ToArray(),
            UnknownPackages: summary.UnknownPackages.ToDictionary(
                x => x.Key,
                x => x.Value.ToArray()
            ),
            UnauthorizedSources: summary
                .UnauthorizedSources.Select(source => new JsonUnauthorizedSource(
                    source.Name,
                    source.Source
                ))
                .ToArray()
        );

        return JsonSerializer.Serialize(dto, Options);
    }

    private sealed record JsonSummary(
        int SchemaVersion,
        bool ShouldFail,
        FailOn FailOn,
        bool FailOnIncomplete,
        int UpdatedPackagesCount,
        IReadOnlyList<JsonUpdatedPackage> UpdatedPackages,
        IReadOnlyList<JsonDeprecatedPackage> DeprecatedPackages,
        IReadOnlyList<JsonVulnerablePackage> VulnerablePackages,
        IReadOnlyList<JsonLicenseMismatchPackage> LicenseMismatchPackages,
        IReadOnlyList<JsonUnsupportedRangePackage> UnsupportedRangePackages,
        IReadOnlyList<JsonSkippedUpdatePackage> SkippedUpdatePackages,
        IReadOnlyDictionary<string, string[]> UnknownPackages,
        IReadOnlyList<JsonUnauthorizedSource> UnauthorizedSources
    );

    private sealed record JsonUpdatedPackage(
        string PackageId,
        IReadOnlyList<JsonUpdatedPackageUpdate> Updates
    );

    private sealed record JsonUpdatedPackageUpdate(string From, string To, string Project);

    private sealed record JsonDeprecatedPackage(
        string PackageId,
        IReadOnlyList<JsonDeprecatedPackageVersion> Versions
    );

    private sealed record JsonDeprecatedPackageVersion(
        string Version,
        string? Message,
        IReadOnlyList<string> Reasons,
        JsonAlternatePackage? AlternatePackage,
        IReadOnlyList<string> Projects
    );

    private sealed record JsonAlternatePackage(string PackageId, string Range);

    private sealed record JsonVulnerablePackage(
        string PackageId,
        IReadOnlyList<JsonVulnerablePackageVersion> Versions
    );

    private sealed record JsonVulnerablePackageVersion(
        string Version,
        IReadOnlyList<JsonVulnerability> Vulnerabilities,
        IReadOnlyList<string> Projects
    );

    private sealed record JsonVulnerability(string AdvisoryUrl, int Severity);

    private sealed record JsonLicenseMismatchPackage(
        string PackageId,
        IReadOnlyList<JsonLicenseMismatchPackageVersion> Versions
    );

    private sealed record JsonLicenseMismatchPackageVersion(
        string Version,
        string License,
        bool IsInstalledVersion,
        IReadOnlyList<string> Projects
    );

    private sealed record JsonUnsupportedRangePackage(
        string PackageId,
        IReadOnlyList<JsonUnsupportedRangePackageRange> Ranges
    );

    private sealed record JsonUnsupportedRangePackageRange(
        string VersionRange,
        IReadOnlyList<string> Projects
    );

    private sealed record JsonSkippedUpdatePackage(
        string PackageId,
        IReadOnlyList<JsonSkippedUpdatePackageVersion> Versions
    );

    private sealed record JsonSkippedUpdatePackageVersion(
        string Version,
        SkippedUpdateReason Reason,
        IReadOnlyList<string> Projects
    );

    private sealed record JsonUnauthorizedSource(string Name, string Source);
}
