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
    private Dictionary<string, PropsFile> _importedPropsFilesByPath = [];
    private readonly Dictionary<string, string> _updatedVersionsByKey = new(StringComparer.Ordinal);

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
    /// package is referenced. Includes package references whose version comes from an imported
    /// file - e.g. a version-less <c>PackageReference</c> resolved through a <c>PackageVersion</c>
    /// in <c>Directory.Packages.props</c> (Central Package Management) - as identified by MSBuild
    /// evaluation. Always reflects the current, possibly already-updated (also during a dry run),
    /// versions of this csproj and of the props files linked via
    /// <see cref="LinkImportedPropsFiles"/>.
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

        NuGetVersion? pinnedVersion = null;

        // A multi-targeted project may legitimately resolve a different version per target
        // framework (e.g. via alignWithTfm): only matches within one framework must agree, and
        // the tool is pinned to the lowest of them so it works for every framework.
        foreach (var matches in ResolveEffectivePinnedVersionsPerTfm(isMatch.IsMatch))
        {
            if (matches.Count == 0)
            {
                continue;
            }

            var distinctVersions = matches.Values.Distinct().ToArray();

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

            if (pinnedVersion is null || distinctVersions[0] < pinnedVersion)
            {
                pinnedVersion = distinctVersions[0];
            }
        }

        return pinnedVersion;
    }

    /// <summary>
    /// Links the in-memory <see cref="PropsFile"/>s discovered for this run, so
    /// <see cref="GetPinnedVersion"/> sees versions they have updated (also during a dry run)
    /// rather than the versions MSBuild evaluated when the project was loaded.
    /// </summary>
    public void LinkImportedPropsFiles(IEnumerable<PropsFile> propsFiles) =>
        _importedPropsFilesByPath = propsFiles
            .DistinctBy(x => x.Path, PathComparer.Comparer)
            .ToDictionary(x => x.Path, PathComparer.Comparer);

    private IEnumerable<Dictionary<string, NuGetVersion>> ResolveEffectivePinnedVersionsPerTfm(
        Func<string, bool> isMatch
    )
    {
        // Multi-targeted projects: the outer evaluation leaves $(TargetFramework) empty, so
        // framework-conditioned items are only correct in the per-framework evaluations.
        IEnumerable<IReadOnlyList<PackageItemSource>> evaluations =
            Evaluation.PackageItemSourcesByTfm.Count > 0
                ? Evaluation.PackageItemSourcesByTfm.Values
                : [Evaluation.PackageItemSources];

        foreach (var sources in evaluations)
        {
            Dictionary<string, NuGetVersion> matches = new(StringComparer.OrdinalIgnoreCase);

            foreach (
                var reference in sources.Where(x =>
                    x.ItemType is "PackageReference" or "GlobalPackageReference"
                    && isMatch(x.PackageId)
                    && !matches.ContainsKey(x.PackageId)
                )
            )
            {
                if (ResolveReferenceVersion(reference, sources) is { } version)
                {
                    matches[reference.PackageId] = version;
                }
            }

            yield return matches;
        }
    }

    private NuGetVersion? ResolveReferenceVersion(
        PackageItemSource reference,
        IReadOnlyList<PackageItemSource> sources
    )
    {
        if (reference.Version is not null)
        {
            return ResolveRepresentativeVersion(GetCurrentVersion(reference));
        }

        // Central Package Management: the version comes from the PackageVersion item MSBuild
        // evaluated for this project (the last one wins, same as MSBuild).
        var packageVersion = sources.LastOrDefault(x =>
            x.ItemType == "PackageVersion"
            && x.Version is not null
            && string.Equals(x.PackageId, reference.PackageId, StringComparison.OrdinalIgnoreCase)
        );

        if (packageVersion is not null)
        {
            return ResolveRepresentativeVersion(GetCurrentVersion(packageVersion));
        }

        return null;
    }

    private string GetCurrentVersion(PackageItemSource source)
    {
        if (
            string.Equals(source.SourceFile, Path, PathComparer.Comparison)
            && _updatedVersionsByKey.TryGetValue(
                CandidateTfmKey.Create(source.ItemType, source.PackageId, source.Version!),
                out var updatedVersion
            )
        )
        {
            return updatedVersion;
        }

        return _importedPropsFilesByPath.TryGetValue(source.SourceFile, out var propsFile)
            ? propsFile.GetCurrentVersion(source.ItemType, source.PackageId, source.Version!)
            : source.Version!;
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

        // MSBuild sources retain the original version after the in-memory document changes.
        _updatedVersionsByKey[
            CandidateTfmKey.Create("PackageReference", candidate.PackageId, candidate.VersionString)
        ] = newVersionString;

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
