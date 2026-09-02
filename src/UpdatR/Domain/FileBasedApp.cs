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
internal sealed partial class FileBasedApp : PackageContainer
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
    private string? _content;
    private readonly List<(string OldDirective, string NewDirective)> _replacements = [];

    private FileBasedApp(FileInfo path)
    {
        _path = path;
    }

    public override string Name => _path.Name;

    public override string Path => _path.FullName;

    public string Parent => _path.DirectoryName!;

    protected override string ReferenceKind => "package directive";

    protected override bool IncludeUnknownOnlyProjects => true;

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
        IReadOnlyCollection<string>? allowedLicenses = null,
        IReadOnlyCollection<string>? alignWithTfm = null
    )
    {
        _content = await File.ReadAllTextAsync(Path).ConfigureAwait(false);
        _replacements.Clear();

        return await UpdatePackagesCoreAsync(
                packages,
                dryRun,
                usePrerelease,
                logger,
                tfm,
                allowedLicenses,
                alignWithTfm
            )
            .ConfigureAwait(false);
    }

    protected override IReadOnlyCollection<NuGetFramework> ResolveTfms(
        NuGetFramework? tfmOverride
    ) => [tfmOverride ?? TargetFramework];

    protected override IEnumerable<Candidate> EnumerateCandidates()
    {
        foreach (Match match in PackageDirectiveRegex().Matches(_content!))
        {
            var packageId = match.Groups["id"].Value;
            var versionGroup = match.Groups["version"];

            if (!versionGroup.Success)
            {
                // No version at all, e.g. `#:package Foo` - NuGet resolves it automatically on
                // restore, nothing to update.
                continue;
            }

            yield return new FileBasedAppCandidate
            {
                PackageId = packageId,
                VersionString = versionGroup.Value,
                SiteText = match.Value,
                Match = match,
            };
        }
    }

    protected override void ApplyVersionUpdate(Candidate candidate, string newVersionString)
    {
        var fileBasedAppCandidate = (FileBasedAppCandidate)candidate;

        var oldDirective = fileBasedAppCandidate.Match.Value;
        var newDirective = oldDirective.Replace(
            candidate.VersionString,
            newVersionString,
            StringComparison.Ordinal
        );

        _replacements.Add((oldDirective, newDirective));
    }

    protected override async Task PersistAsync(bool dryRun)
    {
        if (dryRun)
        {
            return;
        }

        var updatedContent = _content!;

        foreach (var (oldDirective, newDirective) in _replacements)
        {
            updatedContent = updatedContent.Replace(
                oldDirective,
                newDirective,
                StringComparison.Ordinal
            );
        }

        await File.WriteAllTextAsync(Path, updatedContent).ConfigureAwait(false);
    }

    private sealed class FileBasedAppCandidate : Candidate
    {
        public required Match Match { get; init; }
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
            .Where(x => !string.IsNullOrWhiteSpace(x.PackageId))
            .Select(x => (x.PackageId, Version: ResolveRepresentativeVersion(x.Version)))
            .Where(x => x.Version is not null)
            .DistinctBy(x => x.PackageId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.PackageId, x => x.Version!, StringComparer.OrdinalIgnoreCase);
}
