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
/// <c>PackageReference</c> and/or <c>PackageVersion</c> items imported by one or more
/// <see cref="Csproj"/>. Unlike a <see cref="Csproj"/>, a props/targets file has no target
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
    /// Updates every <c>PackageReference</c> and <c>PackageVersion</c> item in this file.
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
                    "/Project/ItemGroup/PackageReference|/Project/ItemGroup/PackageVersion"
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

            if (!NuGetVersion.TryParse(versionStr, out var version))
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
            else if (package.TryGet(version, out var metadata))
            {
                CheckForDeprecationAndVulnerabilities(project, packageId, metadata);
                CheckForLicenseMismatch(project, packageId, version, metadata);
            }

            if (
                !TargetFrameworkCompatibility.TryGetLatestCompatibleWithAllTfms(
                    package,
                    version,
                    tfms,
                    usePrerelease,
                    allowedLicenses,
                    out var updateTo
                )
            )
            {
                CheckForDeprecationAndVulnerabilities(
                    project,
                    packageId,
                    package.PackageMetadatas.SingleOrDefault(x => x.Version == version)
                );

                CheckForSkippedLicenseMismatch(
                    project,
                    package,
                    packageId,
                    version,
                    tfms,
                    usePrerelease
                );

                continue;
            }

            item.SetAttribute("Version", updateTo.Version.ToString());

            LogUpdateSuccessful(logger, Name, packageId, version, updateTo.Version);

            project.AddUpdatedPackage(new(packageId, version, updateTo.Version));

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
        _doc.SelectNodes("/Project/ItemGroup/PackageReference|/Project/ItemGroup/PackageVersion")!
            .OfType<XmlElement>()
            .Select(x =>
                (
                    PackageId: x!.HasAttribute("Include")
                        ? x!.GetAttribute("Include")
                        : x!.GetAttribute("Update"),
                    Version: x!.GetAttribute("Version")
                )
            )
            .Where(x =>
                !string.IsNullOrWhiteSpace(x.PackageId) && NuGetVersion.TryParse(x.Version, out _)
            )
            .DistinctBy(x => x.PackageId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.PackageId,
                x => NuGetVersion.Parse(x.Version),
                StringComparer.OrdinalIgnoreCase
            );

    #region LogMessages
    [LoggerMessage(
        Level = LogLevel.Warning,
        EventId = 1,
        Message = "Could not parse {Version} to NuGetVersion for package reference {PackageReference}."
    )]
    static partial void LogParseError(ILogger logger, string version, string packageReference);

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
