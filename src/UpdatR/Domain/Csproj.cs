using System.Xml;
using Microsoft.Extensions.Logging;
using NuGet.Frameworks;
using NuGet.Versioning;
using UpdatR.Domain.Utils;
using UpdatR.Internals;

namespace UpdatR.Domain;

internal sealed partial class Csproj : PackageContainer
{
    /// <summary>
    /// File extensions recognized as MSBuild "project files" that use the same SDK-style
    /// <c>&lt;PackageReference&gt;</c> item shape - <c>.csproj</c>, <c>.fsproj</c>, and
    /// <c>.vbproj</c>. UpdatR's project handling has no C#-specific logic, so all three share this
    /// one implementation.
    /// </summary>
    public static readonly IReadOnlyCollection<string> SupportedExtensions =
    [
        ".csproj",
        ".fsproj",
        ".vbproj",
    ];

    private readonly FileInfo _path;
    private readonly XmlDocument _doc;
    private IReadOnlyDictionary<string, IReadOnlyCollection<NuGetFramework>>? _candidateTfmsByKey;
    private Dictionary<string, NuGetVersion>? _packages;

    private Csproj(FileInfo path, XmlDocument doc, MsBuildProjectEvaluation evaluation)
    {
        _path = path;
        _doc = doc;
        Evaluation = evaluation;
        TargetFrameworks =
        [
            .. evaluation
                .TargetFrameworks.Select(NuGetFramework.Parse)
                .DefaultIfEmpty(NuGetFramework.AnyFramework),
        ];
    }

    public override string Name => _path.Name;

    public override string Path => _path.FullName;

    protected override string ReferenceKind => "package reference";

    public string Parent => _path.DirectoryName!;

    /// <summary>
    /// Every target framework resolved by MSBuild evaluation, including imported properties
    /// and per-framework evaluation of multi-targeted projects. Falls back to
    /// <see cref="NuGetFramework.AnyFramework"/> if no target framework is defined.
    /// </summary>
    public IReadOnlyList<NuGetFramework> TargetFrameworks { get; }

    public MsBuildProjectEvaluation Evaluation { get; }

    /// <summary>
    /// The version every referenced package whose id matches <paramref name="packageIdPattern"/>
    /// is pinned to - the same <c>*</c>-wildcard matching used for e.g. <c>alignWithTfm</c>
    /// (case-insensitive, matched against the whole package id) - e.g.
    /// <c>Microsoft.EntityFrameworkCore*</c> to match the whole Entity Framework Core package
    /// family. Used by <see cref="ToolPackagePin"/> to keep a dotnet tool (e.g. <c>dotnet-ef</c>)
    /// from moving ahead of the package version(s) it drives. <see langword="null"/> if no such
    /// package is referenced. Always reflects the current, possibly already-updated,
    /// <see cref="Packages"/>.
    /// </summary>
    /// <exception cref="AmbiguousToolPackagePinException">
    /// More than one referenced package matches <paramref name="packageIdPattern"/>, and they are
    /// not all pinned to the same version - e.g. a too-broad pattern like <c>xunit*</c> matching
    /// both <c>xunit.v3</c> and an unrelated, differently-versioned <c>xunit.runner.visualstudio</c>.
    /// There is then no single unambiguous version to pin a tool to.
    /// </exception>
    public NuGetVersion? GetPinnedVersion(string packageIdPattern)
    {
        var isMatch = SearchPattern.ConvertToRegex(packageIdPattern);

        var matches = Packages.Where(x => isMatch.IsMatch(x.Key)).ToArray();

        if (matches.Length == 0)
        {
            return null;
        }

        var distinctVersions = matches.Select(x => x.Value).Distinct().ToArray();

        if (distinctVersions.Length > 1)
        {
            throw new AmbiguousToolPackagePinException(
                $"'{Path}' references multiple packages matching '{packageIdPattern}' pinned to "
                    + "different versions: "
                    + string.Join(
                        ", ",
                        matches
                            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                            .Select(x => $"{x.Key} {x.Value}")
                    )
                    + ". Narrow the pattern so it only matches packages meant to move in "
                    + "lockstep, or align their versions."
            );
        }

        return distinctVersions[0];
    }

    public IDictionary<string, NuGetVersion> Packages => _packages ??= GetPackages();

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

        if (!SupportedExtensions.Contains(file.Extension, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"'{nameof(path)}' does not have the correct file extension.",
                nameof(path)
            );
        }

        var evaluation = MsBuildProjectInspector.Evaluate(file.FullName);
        var doc = new XmlDocument { PreserveWhitespace = true };

        doc.Load(file.FullName);

        return new Csproj(file, doc, evaluation);
    }

    public Task<ProjectWithPackages?> UpdatePackagesAsync(
        IDictionary<string, NuGetPackage?> packages,
        bool dryRun,
        bool usePrerelease,
        ILogger logger,
        NuGetFramework? tfm = null,
        IReadOnlyCollection<string>? allowedLicenses = null,
        IReadOnlyCollection<string>? alignWithTfm = null,
        IReadOnlyCollection<PackageVersionPolicy>? packagePolicies = null
    ) =>
        UpdatePackagesCoreAsync(
            packages,
            dryRun,
            usePrerelease,
            logger,
            tfm,
            allowedLicenses,
            alignWithTfm,
            packagePolicies
        );

    protected override IReadOnlyCollection<NuGetFramework> ResolveTfms(
        NuGetFramework? tfmOverride
    ) => tfmOverride is null ? TargetFrameworks : [tfmOverride];

    protected override IEnumerable<Candidate> EnumerateCandidates()
    {
        var candidateTfmsByKey = GetCandidateTfmsByKey();

        var packageReferences = _doc.SelectNodes("/Project/ItemGroup/PackageReference")!
            .OfType<XmlElement>();

        foreach (var packageReference in packageReferences)
        {
            var packageId = packageReference.HasAttribute("Include")
                ? packageReference.GetAttribute("Include")
                : packageReference.GetAttribute("Update");

            // With Central Package Management, a project overrides the centrally managed
            // version for a single PackageReference using VersionOverride instead of Version.
            var versionAttributeName =
                packageReference.HasAttribute("Version") ? "Version"
                : packageReference.HasAttribute("VersionOverride") ? "VersionOverride"
                : null;

            if (versionAttributeName is null)
            {
                // Neither Version nor VersionOverride means there's nothing to update, e.g. a
                // PackageReference using Update to only override metadata (such as
                // PrivateAssets) for a package already referenced via Directory.Build.props.
                continue;
            }

            var versionString = packageReference.GetAttribute(versionAttributeName);

            candidateTfmsByKey.TryGetValue(
                CandidateTfmKey.Create("PackageReference", packageId, versionString),
                out var applicableTfms
            );

            yield return new CsprojCandidate
            {
                PackageId = packageId,
                VersionString = versionString,
                SiteText = packageReference.OuterXml,
                Element = packageReference,
                AttributeName = versionAttributeName,
                ApplicableTfms = applicableTfms,
            };
        }
    }

    protected override void ApplyVersionUpdate(Candidate candidate, string newVersionString)
    {
        var csprojCandidate = (CsprojCandidate)candidate;

        csprojCandidate.Element.SetAttribute(csprojCandidate.AttributeName, newVersionString);

        // The XML was just mutated - the cached Packages snapshot (read by
        // UpdateEntityFrameworkVersion via OnChangesApplied, among others) must be recomputed
        // from the updated document rather than keep serving pre-update data.
        _packages = null;
    }

    protected override Task PersistAsync(bool dryRun)
    {
        if (!dryRun)
        {
            _doc.Save(Path);
        }

        return Task.CompletedTask;
    }

    protected override void OnUnparseableVersion(Candidate candidate, ILogger logger) =>
        LogParseError(logger, candidate.VersionString, ReferenceKind, candidate.SiteText);

    private sealed class CsprojCandidate : Candidate
    {
        public required XmlElement Element { get; init; }

        public required string AttributeName { get; init; }
    }

    /// <summary>
    /// Maps each distinct (item type, package id, version string) occurrence declared directly
    /// in this csproj to the target framework(s) it applies to, using the evaluation captured
    /// when the project was loaded. An empty map means candidates use <see cref="TargetFrameworks"/>.
    /// </summary>
    private IReadOnlyDictionary<string, IReadOnlyCollection<NuGetFramework>> GetCandidateTfmsByKey()
    {
        if (_candidateTfmsByKey is not null)
        {
            return _candidateTfmsByKey;
        }

        Dictionary<string, HashSet<NuGetFramework>> map = new(StringComparer.Ordinal);

        foreach (var (tfmString, sources) in Evaluation.PackageItemSourcesByTfm)
        {
            var tfm = NuGetFramework.Parse(tfmString);

            foreach (
                var source in sources.Where(x =>
                    x.Version is not null
                    && string.Equals(x.SourceFile, Path, PathComparer.Comparison)
                )
            )
            {
                var key = CandidateTfmKey.Create(
                    source.ItemType,
                    source.PackageId,
                    source.Version!
                );

                if (!map.TryGetValue(key, out var tfms))
                {
                    map[key] = tfms = [];
                }

                tfms.Add(tfm);
            }
        }

        return _candidateTfmsByKey = map.ToDictionary(
            x => x.Key,
            x => (IReadOnlyCollection<NuGetFramework>)x.Value,
            StringComparer.Ordinal
        );
    }

    private Dictionary<string, NuGetVersion> GetPackages() =>
        _doc.SelectNodes("/Project/ItemGroup/PackageReference")!
            .OfType<XmlElement>()
            .Select(x =>
                (
                    PackageId: x!.HasAttribute("Include")
                        ? x!.GetAttribute("Include")
                        : x!.GetAttribute("Update"),
                    Version: x!.HasAttribute("Version")
                        ? x!.GetAttribute("Version")
                        : x!.GetAttribute("VersionOverride")
                )
            )
            .Where(x => !string.IsNullOrWhiteSpace(x.PackageId))
            .Select(x => (x.PackageId, Version: ResolveRepresentativeVersion(x.Version)))
            .Where(x => x.Version is not null)
            .DistinctBy(x => x.PackageId)
            .ToDictionary(x => x.PackageId, x => x.Version!, StringComparer.OrdinalIgnoreCase);
}
