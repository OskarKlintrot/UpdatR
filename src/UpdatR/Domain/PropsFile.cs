using System.Xml;
using Microsoft.Extensions.Logging;
using NuGet.Frameworks;
using NuGet.Versioning;
using UpdatR.Domain.Utils;
using UpdatR.Internals;

namespace UpdatR.Domain;

/// <summary>
/// A <c>.props</c> or <c>.targets</c> file - most commonly <c>Directory.Build.props</c> or, for
/// Central Package Management, <c>Directory.Packages.props</c> - that declares
/// <c>PackageReference</c>, <c>PackageVersion</c> and/or <c>GlobalPackageReference</c> items
/// imported by one or more <see cref="Csproj"/>. Unlike a <see cref="Csproj"/>, a props/targets
/// file has no target
/// framework of its own: it can be imported by several projects that each target a different
/// framework, so <see cref="TargetFrameworks"/> tracks every framework of every project that was
/// found to import it (via <see cref="Internals.MsBuildProjectInspector"/>).
/// </summary>
internal sealed partial class PropsFile
{
    private readonly FileInfo _path;
    private readonly XmlDocument _doc;

    private PropsFile(FileInfo path, XmlDocument doc, IReadOnlyCollection<NuGetFramework> tfms)
    {
        _path = path;
        _doc = doc;
        TargetFrameworks = tfms.Count > 0 ? tfms : [NuGetFramework.AnyFramework];
    }

    public string Name => _path.Name;

    public string Path => _path.FullName;

    /// <summary>
    /// Target frameworks of every project known to import this file, deduplicated. Falls back to
    /// <see cref="NuGetFramework.AnyFramework"/> if no importing project could be determined.
    /// </summary>
    public IReadOnlyCollection<NuGetFramework> TargetFrameworks { get; }

    public IDictionary<string, NuGetVersion> Packages => GetPackages();

    public static PropsFile Create(string path, IEnumerable<NuGetFramework>? tfms = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(
                $"'{nameof(path)}' cannot be null or whitespace.",
                nameof(path)
            );
        }

        var file = new FileInfo(System.IO.Path.GetFullPath(path));

        if (!file.Exists)
        {
            throw new ArgumentException($"'{nameof(path)}' does not exist.", nameof(path));
        }

        var doc = new XmlDocument { PreserveWhitespace = true };

        doc.Load(file.FullName);

        return new PropsFile(file, doc, tfms?.Distinct().ToArray() ?? []);
    }

    /// <summary>
    /// Updates every <c>PackageReference</c>, <c>PackageVersion</c> and
    /// <c>GlobalPackageReference</c> item in this file.
    /// </summary>
    /// <param name="tfm">
    /// Overrides <see cref="TargetFrameworks"/> when supplied. Otherwise, a package is only
    /// updated to a version compatible with every framework in <see cref="TargetFrameworks"/>,
    /// i.e. the update is skipped if any importing project can't use a newer version.
    /// </param>
    public ProjectWithPackages? UpdatePackages(
        IDictionary<string, NuGetPackage?> packages,
        bool dryRun,
        bool usePrerelease,
        ILogger logger,
        NuGetFramework? tfm = null,
        IReadOnlyCollection<string>? allowedLicenses = null
    )
    {
        var tfms = tfm is null ? TargetFrameworks : [tfm];

        var project = new ProjectWithPackages(Path);

        var changed = false;

        void handler(object sender, XmlNodeChangedEventArgs e) => changed = true;
        _doc.NodeChanged += handler;
        _doc.NodeInserted += handler;
        _doc.NodeRemoved += handler;

        var items = _doc.SelectNodes(
                    "/Project/ItemGroup/PackageReference|/Project/ItemGroup/PackageVersion|/Project/ItemGroup/GlobalPackageReference"
                )!
            .OfType<XmlElement>();

        foreach (var item in items)
        {
            var packageId = item.HasAttribute("Include")
                ? item.GetAttribute("Include")
                : item.GetAttribute("Update");

            if (!item.HasAttribute("Version"))
            {
                // No Version attribute means there's nothing to update, e.g. a PackageReference
                // using Update to only override metadata (such as PrivateAssets).
                continue;
            }

            var versionStr = item.GetAttribute("Version");

            VersionRange? versionRange = null;
            NuGetVersion? version;

            if (NuGetVersion.TryParse(versionStr, out var parsedVersion))
            {
                version = parsedVersion;
            }
            else if (VersionRange.TryParse(versionStr, out versionRange))
            {
                // A floating version, e.g. "4.8.*", or a version range, e.g. "[1.0,2.0)".
                // The concrete version is resolved further down, once the package's available
                // versions are known.
                version = null;
            }
            else
            {
                LogParseError(logger, versionStr, item.OuterXml);

                continue;
            }

            if (!packages.TryGetValue(packageId, out var package))
            {
                LogMissingPackage(logger, packageId);

                project.AddUnknownPackage(packageId);

                continue;
            }
            else if (package is null)
            {
                // Ignore package

                continue;
            }

            if (versionRange is not null)
            {
                // Resolve the version NuGet would currently pick for the floating version/range,
                // so it can be used the same way an exact <c>Version</c> would be below.
                var resolved = package
                    .PackageMetadatas.Where(x => Satisfies(versionRange, x.Version))
                    .OrderByDescending(x => x.Version)
                    .FirstOrDefault();

                if (resolved is null)
                {
                    LogFloatingVersionSkipped(logger, versionStr, item.OuterXml);

                    continue;
                }

                version = resolved.Version;

                CheckForDeprecationAndVulnerabilities(project, packageId, resolved);
                CheckForLicenseMismatch(project, packageId, version, resolved);
            }
            else if (package.TryGet(version!, out var metadata))
            {
                CheckForDeprecationAndVulnerabilities(project, packageId, metadata);
                CheckForLicenseMismatch(project, packageId, version!, metadata);
            }

            if (
                !TargetFrameworkCompatibility.TryGetLatestCompatibleWithAllTfms(
                    package,
                    version!,
                    tfms,
                    usePrerelease,
                    allowedLicenses,
                    out var updateTo
                )
            )
            {
                if (versionRange is not null)
                {
                    // Nothing newer than what the floating version/range already resolves to.
                    LogFloatingVersionSkipped(logger, versionStr, item.OuterXml);

                    continue;
                }

                CheckForDeprecationAndVulnerabilities(
                    project,
                    packageId,
                    package.PackageMetadatas.SingleOrDefault(x => x.Version == version)
                );

                CheckForSkippedLicenseMismatch(
                    project,
                    package,
                    packageId,
                    version!,
                    tfms,
                    usePrerelease
                );

                continue;
            }

            if (versionRange is not null)
            {
                if (Satisfies(versionRange, updateTo.Version))
                {
                    // The newer version is already covered by the existing floating version/range
                    // - NuGet already resolves to it automatically, nothing to write.
                    continue;
                }

                var newVersionStr = BuildFloatingVersionString(versionRange, updateTo.Version);

                if (newVersionStr is null)
                {
                    // Don't know how to safely rewrite this kind of floating version/range (e.g.
                    // a prerelease float, or a fixed range like "[1.0,2.0)") - leave it as-is,
                    // but call it out clearly since a newer version is available.
                    LogUnsupportedVersionRange(logger, versionStr, item.OuterXml);

                    project.AddUnsupportedRangePackage(new(packageId, versionStr));

                    continue;
                }

                item.SetAttribute("Version", newVersionStr);

                LogUpdateSuccessful(logger, Name, packageId, version!, updateTo.Version);

                project.AddUpdatedPackage(new(packageId, version!, updateTo.Version));

                CheckForDeprecationAndVulnerabilities(project, packageId, updateTo);

                continue;
            }

            item.SetAttribute("Version", updateTo.Version.ToString());

            LogUpdateSuccessful(logger, Name, packageId, version!, updateTo.Version);

            project.AddUpdatedPackage(new(packageId, version!, updateTo.Version));

            CheckForDeprecationAndVulnerabilities(project, packageId, updateTo);
        }

        _doc.NodeChanged -= handler;
        _doc.NodeInserted -= handler;
        _doc.NodeRemoved -= handler;

        if (changed && !dryRun)
        {
            _doc.Save(Path);
        }

        return project.AnyPackages() ? project : null;

        void CheckForDeprecationAndVulnerabilities(
            ProjectWithPackages project,
            string packageId,
            PackageMetadata? packageMetadata
        )
        {
            if (packageMetadata is null)
            {
                return;
            }

            if (packageMetadata.DeprecationMetadata is not null)
            {
                project.AddDeprecatedPackage(
                    new(packageId, packageMetadata.Version, packageMetadata.DeprecationMetadata)
                );

                LogDeprecatedPackage(
                    logger,
                    packageId,
                    packageMetadata.Version,
                    string.Join(", ", packageMetadata.DeprecationMetadata.Reasons)
                );
            }

            if (packageMetadata.Vulnerabilities?.Any() == true)
            {
                project.AddVulnerablePackage(
                    new(packageId, packageMetadata.Version, packageMetadata.Vulnerabilities)
                );

                LogVulnerablePackage(
                    logger,
                    packageId,
                    packageMetadata.Version,
                    packageMetadata.Vulnerabilities.Count()
                );
            }
        }

        void CheckForLicenseMismatch(
            ProjectWithPackages project,
            string packageId,
            NuGetVersion version,
            PackageMetadata packageMetadata
        )
        {
            if (
                allowedLicenses is not { Count: > 0 }
                || NuGetPackage.IsLicenseAllowed(packageMetadata, allowedLicenses)
            )
            {
                return;
            }

            project.AddLicenseMismatchPackage(
                new(
                    packageId,
                    version,
                    packageMetadata.LicenseExpression!,
                    isInstalledVersion: true
                )
            );

            LogLicenseMismatch(logger, packageId, version, packageMetadata.LicenseExpression!);
        }

        void CheckForSkippedLicenseMismatch(
            ProjectWithPackages project,
            NuGetPackage package,
            string packageId,
            NuGetVersion version,
            IReadOnlyCollection<NuGetFramework> tfms,
            bool usePrerelease
        )
        {
            if (
                allowedLicenses is not { Count: > 0 }
                || !TargetFrameworkCompatibility.TryGetLatestCompatibleWithAllTfms(
                    package,
                    version,
                    tfms,
                    usePrerelease,
                    allowedLicenses: null,
                    out var candidate
                )
                || NuGetPackage.IsLicenseAllowed(candidate, allowedLicenses)
            )
            {
                return;
            }

            project.AddLicenseMismatchPackage(
                new(
                    packageId,
                    candidate.Version,
                    candidate.LicenseExpression!,
                    isInstalledVersion: false
                )
            );

            LogSkippedLicenseMismatch(
                logger,
                packageId,
                candidate.Version,
                candidate.LicenseExpression!
            );
        }
    }

    private Dictionary<string, NuGetVersion> GetPackages() =>
        _doc.SelectNodes(
                    "/Project/ItemGroup/PackageReference|/Project/ItemGroup/PackageVersion|/Project/ItemGroup/GlobalPackageReference"
                )!
            .OfType<XmlElement>()
            .Select(x =>
                (
                    PackageId: x!.HasAttribute("Include")
                        ? x!.GetAttribute("Include")
                        : x!.GetAttribute("Update"),
                    Version: x!.GetAttribute("Version")
                )
            )
            .Where(x => !string.IsNullOrWhiteSpace(x.PackageId))
            .Select(x => (x.PackageId, Version: ResolveRepresentativeVersion(x.Version)))
            .Where(x => x.Version is not null)
            .DistinctBy(x => x.PackageId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.PackageId, x => x.Version!, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves a <see cref="NuGetVersion"/> that represents <paramref name="versionStr"/> well
    /// enough to be used as a lookup key, i.e. to ensure the package is queried for on NuGet even
    /// though it can't be parsed as an exact version. Returns the lower bound of the version
    /// range for a floating version (e.g. "4.8.*") or a version range (e.g. "[1.0,2.0)"), or
    /// <see langword="null"/> if <paramref name="versionStr"/> can't be parsed at all.
    /// </summary>
    private static NuGetVersion? ResolveRepresentativeVersion(string versionStr) =>
        NuGetVersion.TryParse(versionStr, out var version) ? version
        : VersionRange.TryParse(versionStr, out var versionRange)
            ? versionRange.MinVersion ?? versionRange.Float?.MinVersion
        : null;

    /// <summary>
    /// Checks if <paramref name="version"/> matches <paramref name="range"/>, respecting its
    /// floating segment (if any). <see cref="VersionRange.Satisfies(NuGetVersion)"/> alone
    /// ignores the floating segment, e.g. it considers "2.0.0" to satisfy "1.*" even though "1.*"
    /// is restricted to the 1.x series.
    /// </summary>
    private static bool Satisfies(VersionRange range, NuGetVersion version) =>
        range.Float?.Satisfies(version) ?? range.Satisfies(version);

    /// <summary>
    /// Builds a new floating version string with the same floating segment as
    /// <paramref name="range"/>, but with its fixed prefix updated to match
    /// <paramref name="newVersion"/> - e.g. bumps "4.8.*" to "4.9.*" if <paramref name="newVersion"/>
    /// is "4.9.2". Returns <see langword="null"/> for floating segments that aren't safe to
    /// rewrite this way (prerelease floats), or for a plain version range without a floating
    /// segment (e.g. "[1.0,2.0)"), since rewriting those could silently change their meaning.
    /// </summary>
    private static string? BuildFloatingVersionString(
        VersionRange range,
        NuGetVersion newVersion
    ) =>
        range.Float?.FloatBehavior switch
        {
            NuGetVersionFloatBehavior.Major => "*",
            NuGetVersionFloatBehavior.Minor => $"{newVersion.Major}.*",
            NuGetVersionFloatBehavior.Patch => $"{newVersion.Major}.{newVersion.Minor}.*",
            NuGetVersionFloatBehavior.Revision =>
                $"{newVersion.Major}.{newVersion.Minor}.{newVersion.Patch}.*",
            _ => null,
        };

    #region LogMessages
    [LoggerMessage(
        Level = LogLevel.Warning,
        EventId = 1,
        Message = "Could not parse {Version} to NuGetVersion for package reference {PackageReference}."
    )]
    static partial void LogParseError(ILogger logger, string version, string packageReference);

    [LoggerMessage(
        Level = LogLevel.Debug,
        EventId = 8,
        Message = "Skipping automatic update of floating version {Version} for package reference {PackageReference} since NuGet already resolves it to the latest matching version."
    )]
    static partial void LogFloatingVersionSkipped(
        ILogger logger,
        string version,
        string packageReference
    );

    [LoggerMessage(
        Level = LogLevel.Warning,
        EventId = 9,
        Message = "Could not automatically update version range {VersionRange} for package reference {PackageReference} - UpdatR doesn't know how to safely rewrite this kind of version range (e.g. a fixed range like \"[1.0,2.0)\", or a prerelease float). A newer version may be available; update it manually if needed."
    )]
    static partial void LogUnsupportedVersionRange(
        ILogger logger,
        string versionRange,
        string packageReference
    );

    [LoggerMessage(Level = LogLevel.Warning, EventId = 2, Message = "Could not find {PackageId}.")]
    static partial void LogMissingPackage(ILogger logger, string packageId);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 3,
        Message = "{Name}: Updated {PackageId} from {FromVersion} to {ToVersion}"
    )]
    static partial void LogUpdateSuccessful(
        ILogger logger,
        string name,
        string packageId,
        NuGetVersion fromVersion,
        NuGetVersion toVersion
    );

    [LoggerMessage(
        Level = LogLevel.Warning,
        EventId = 4,
        Message = "Package {PackageId} version {Version} is deprecated with reasons: {Reasons}"
    )]
    static partial void LogDeprecatedPackage(
        ILogger logger,
        string packageId,
        NuGetVersion version,
        string reasons
    );

    [LoggerMessage(
        Level = LogLevel.Warning,
        EventId = 5,
        Message = "Package {PackageId} version {Version} has {Vulnerabilities} vulnerabilities"
    )]
    static partial void LogVulnerablePackage(
        ILogger logger,
        string packageId,
        NuGetVersion version,
        int vulnerabilities
    );

    [LoggerMessage(
        Level = LogLevel.Warning,
        EventId = 6,
        Message = "Package {PackageId} version {Version} has a license that isn't allowed: {License}"
    )]
    static partial void LogLicenseMismatch(
        ILogger logger,
        string packageId,
        NuGetVersion version,
        string license
    );

    [LoggerMessage(
        Level = LogLevel.Warning,
        EventId = 7,
        Message = "Package {PackageId} has a newer version {Version} available, but it was skipped because its license isn't allowed: {License}"
    )]
    static partial void LogSkippedLicenseMismatch(
        ILogger logger,
        string packageId,
        NuGetVersion version,
        string license
    );
    #endregion
}
