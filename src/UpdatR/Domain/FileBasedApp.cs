using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using NuGet.Frameworks;
using NuGet.Versioning;
using UpdatR.Domain.Utils;
using UpdatR.Internals;

namespace UpdatR.Domain;

/// <summary>
/// A file-based app, i.e. a single .cs file without a corresponding .csproj, using
/// `#:package` directives to reference NuGet packages.
/// </summary>
/// <seealso href="https://learn.microsoft.com/en-us/dotnet/core/sdk/file-based-apps">File-based apps</seealso>
internal sealed partial class FileBasedApp
{
    [GeneratedRegex(
        @"^\s*#:package\s+(?<id>[^\s@]+)(?:@(?<version>\S+))?\s*$",
        RegexOptions.Multiline
    )]
    private static partial Regex PackageDirectiveRegex();

    [GeneratedRegex(
        @"^\s*#:property\s+TargetFramework\s*=\s*(?<value>\S+)\s*$",
        RegexOptions.Multiline | RegexOptions.IgnoreCase
    )]
    private static partial Regex TargetFrameworkPropertyRegex();

    private readonly FileInfo _path;
    private NuGetFramework? _targetFramework;

    private FileBasedApp(FileInfo path)
    {
        _path = path;
    }

    public string Name => _path.Name;

    public string Path => _path.FullName;

    public string Parent => _path.DirectoryName!;

    public NuGetFramework TargetFramework => _targetFramework ??= GetTargetFramework();

    public IDictionary<string, NuGetVersion> Packages => GetPackages();

    public static FileBasedApp Create(string path)
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

        if (!file.Extension.Equals(".cs", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"'{nameof(path)}' does not have the correct file extension.",
                nameof(path)
            );
        }

        if (!IsFileBasedApp(file.FullName))
        {
            throw new ArgumentException(
                $"'{file.FullName}' does not contain any '#:package' directives.",
                nameof(path)
            );
        }

        return new FileBasedApp(file);
    }

    /// <summary>
    /// Checks if <paramref name="path"/> is a .cs file containing at least one `#:package` directive.
    /// </summary>
    /// <remarks>
    /// The whole file is scanned to be certain no `#:package` directive is missed. Directives are
    /// expected near the top of the file, but nothing prevents a file from having e.g. leading
    /// comments or blank lines before them.
    /// See <see href="https://learn.microsoft.com/en-us/dotnet/core/sdk/file-based-apps">
    /// File-based apps</see> for the directive rules this class relies on.
    /// </remarks>
    public static bool IsFileBasedApp(string path) =>
        path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
        && File.Exists(path)
        && File.ReadLines(path).Any(line => PackageDirectiveRegex().IsMatch(line));

    public async Task<ProjectWithPackages?> UpdatePackagesAsync(
        IDictionary<string, NuGetPackage?> packages,
        bool dryRun,
        bool usePrerelease,
        ILogger logger,
        NuGetFramework? tfm = null,
        IReadOnlyCollection<string>? allowedLicenses = null
    )
    {
        var content = await File.ReadAllTextAsync(Path).ConfigureAwait(false);

        var project = new ProjectWithPackages(Path);

        var replacements = new List<(string OldDirective, string NewDirective)>();

        foreach (Match match in PackageDirectiveRegex().Matches(content))
        {
            var packageId = match.Groups["id"].Value;
            var versionGroup = match.Groups["version"];

            if (
                !versionGroup.Success || !NuGetVersion.TryParse(versionGroup.Value, out var version)
            )
            {
                // No pinned version, e.g. `#:package Foo` or `#:package Foo@*`, nothing to update.
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
                !package.TryGetLatestComparedTo(
                    version,
                    tfm ?? TargetFramework,
                    usePrerelease,
                    out var updateTo,
                    allowedLicenses
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
                    tfm ?? TargetFramework,
                    usePrerelease
                );

                continue;
            }

            var oldDirective = match.Value;
            var newDirective = oldDirective.Replace(
                versionGroup.Value,
                updateTo.Version.ToString(),
                StringComparison.Ordinal
            );

            replacements.Add((oldDirective, newDirective));

            LogUpdateSuccessful(logger, Name, packageId, version, updateTo.Version);

            project.AddUpdatedPackage(new(packageId, version, updateTo.Version));

            CheckForDeprecationAndVulnerabilities(project, packageId, updateTo);
        }

        if (replacements.Count > 0)
        {
            if (!dryRun)
            {
                var updatedContent = content;

                foreach (var (oldDirective, newDirective) in replacements)
                {
                    updatedContent = updatedContent.Replace(
                        oldDirective,
                        newDirective,
                        StringComparison.Ordinal
                    );
                }

                await File.WriteAllTextAsync(Path, updatedContent).ConfigureAwait(false);
            }
        }

        return project.AnyPackages() || project.UnknownPackages.Any() ? project : null;

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
            NuGetFramework targetFramework,
            bool usePrerelease
        )
        {
            if (
                !package.TryGetNewerVersionWithDisallowedLicense(
                    version,
                    targetFramework,
                    usePrerelease,
                    allowedLicenses,
                    out var skipped
                )
            )
            {
                return;
            }

            project.AddLicenseMismatchPackage(
                new(
                    packageId,
                    skipped.Version,
                    skipped.LicenseExpression!,
                    isInstalledVersion: false
                )
            );

            LogSkippedLicenseMismatch(
                logger,
                packageId,
                skipped.Version,
                skipped.LicenseExpression!
            );
        }
    }

    private NuGetFramework GetTargetFramework()
    {
        var content = File.ReadAllText(Path);

        var match = TargetFrameworkPropertyRegex().Match(content);

        var targetFramework = match.Success
            ? match.Groups["value"].Value
            : RetriveTargetFramework.GetTargetFrameworkFromDirectoryBuildProps(new(Parent));

        return targetFramework is null
            ? NuGetFramework.AnyFramework
            : NuGetFramework.Parse(targetFramework);
    }

    private Dictionary<string, NuGetVersion> GetPackages() =>
        PackageDirectiveRegex()
            .Matches(File.ReadAllText(Path))
            .Select(x => (PackageId: x.Groups["id"].Value, Version: x.Groups["version"].Value))
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
    [LoggerMessage(Level = LogLevel.Warning, EventId = 1, Message = "Could not find {PackageId}.")]
    static partial void LogMissingPackage(ILogger logger, string packageId);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 2,
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
        EventId = 3,
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
        EventId = 4,
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
        EventId = 5,
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
        EventId = 6,
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
