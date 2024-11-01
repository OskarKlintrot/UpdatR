using System.Xml;
using Microsoft.Extensions.Logging;
using NuGet.Frameworks;
using NuGet.Versioning;
using UpdatR.Domain.Utils;
using UpdatR.Internals;
using static UpdatR.Domain.Utils.RetriveTargetFramework;

namespace UpdatR.Domain;

internal sealed partial class Csproj
{
    private readonly FileInfo _path;
    private readonly XmlDocument _doc;
    private NuGetFramework? _targetFramework;
    private NuGetVersion? _entityFrameworkVersion;
    private bool _entityFrameworkVersionLoaded;

    private Csproj(FileInfo path, XmlDocument doc)
    {
        _path = path;
        _doc = doc;
    }

    public string Name => _path.Name;

    public string Path => _path.FullName;

    public string Parent => _path.DirectoryName!;

    public NuGetFramework TargetFramework => _targetFramework ??= GetTargetFramework();

    public NuGetVersion? EntityFrameworkVersion =>
        _entityFrameworkVersionLoaded
            ? _entityFrameworkVersion
            : _entityFrameworkVersion ??= GetEntityFrameworkVersion();

    public IDictionary<string, NuGetVersion> Packages => GetPackages();

    public static Csproj Create(string path)
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

        if (!file.Extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"'{nameof(path)}' does not have the correct file extension.",
                nameof(path)
            );
        }

        var doc = new XmlDocument { PreserveWhitespace = true };

        doc.Load(file.FullName);

        return new Csproj(file, doc);
    }

    public ProjectWithPackages? UpdatePackages(
        IDictionary<string, NuGetPackage?> packages,
        bool dryRun,
        ILogger logger,
        NuGetFramework? tfm = null
    )
    {
        var project = new ProjectWithPackages(Path);

        var changed = false;

        void handler(object sender, XmlNodeChangedEventArgs e) => changed = true;
        _doc.NodeChanged += handler;
        _doc.NodeInserted += handler;
        _doc.NodeRemoved += handler;

        var packageReferences = _doc.SelectNodes("/Project/ItemGroup/PackageReference")!
            .OfType<XmlElement>();

        foreach (var packageReference in packageReferences)
        {
            var packageId = packageReference.GetAttribute("Include");
            var versionStr = packageReference.GetAttribute("Version");

            if (!NuGetVersion.TryParse(versionStr, out var version))
            {
                LogParseError(logger, versionStr, packageReference.ToString() ?? string.Empty);

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
            }

            if (!package.TryGetLatestComparedTo(version, tfm ?? TargetFramework, out var updateTo))
            {
                CheckForDeprecationAndVulnerabilities(
                    project,
                    packageId,
                    package.PackageMetadatas.SingleOrDefault(x => x.Version == version)
                );

                continue;
            }

            packageReference.SetAttribute("Version", updateTo.Version.ToString());

            LogUpdateSuccessful(logger, Name, packageId, version, updateTo.Version);

            project.AddUpdatedPackage(new(packageId, version, updateTo.Version));

            CheckForDeprecationAndVulnerabilities(project, packageId, updateTo);
        }

        _doc.NodeChanged -= handler;
        _doc.NodeInserted -= handler;
        _doc.NodeRemoved -= handler;

        if (changed)
        {
            if (!dryRun)
            {
                _doc.Save(Path);
            }

            UpdateEntityFrameworkVersion();
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
    }

    private NuGetFramework GetTargetFramework()
    {
        var targetFramework =
            RetriveTargetFramework.GetTargetFramework(Path)
            ?? GetTargetFrameworkFromDirectoryBuildProps(new(Parent));

        return targetFramework is null
            ? NuGetFramework.AnyFramework
            : NuGetFramework.Parse(targetFramework);
    }

    private void UpdateEntityFrameworkVersion()
    {
        foreach (var (packageId, version) in Packages)
        {
            if (
                packageId.StartsWith(
                    "Microsoft.EntityFrameworkCore",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                _entityFrameworkVersion = version;
            }
        }
    }

    private NuGetVersion? GetEntityFrameworkVersion()
    {
        foreach (var (packageId, version) in Packages)
        {
            if (
                packageId.StartsWith(
                    "Microsoft.EntityFrameworkCore",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                _entityFrameworkVersionLoaded = true;

                return version;
            }
        }

        _entityFrameworkVersionLoaded = true;

        return null;
    }

    private Dictionary<string, NuGetVersion> GetPackages() =>
        _doc.SelectNodes("/Project/ItemGroup/PackageReference")!
            .OfType<XmlElement>()
            .Select(x =>
                (PackageId: x!.GetAttribute("Include"), Version: x!.GetAttribute("Version"))
            )
            .Where(x =>
                !string.IsNullOrWhiteSpace(x.PackageId) && NuGetVersion.TryParse(x.Version, out _)
            )
            .DistinctBy(x => x.PackageId)
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
    #endregion
}
