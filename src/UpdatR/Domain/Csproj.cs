using System.Xml;
using Microsoft.Extensions.Logging;
using NuGet.Frameworks;
using NuGet.Versioning;
using UpdatR.Domain.Utils;
using UpdatR.Internals;
using static UpdatR.Domain.Utils.RetriveTargetFramework;

namespace UpdatR.Domain;

internal sealed partial class Csproj : PackageContainer
{
    private readonly FileInfo _path;
    private readonly XmlDocument _doc;
    private IReadOnlyList<NuGetFramework>? _targetFrameworks;
    private NuGetVersion? _entityFrameworkVersion;
    private bool _entityFrameworkVersionLoaded;
    private IReadOnlyDictionary<string, IReadOnlyCollection<NuGetFramework>>? _candidateTfmsByKey;

    private Csproj(FileInfo path, XmlDocument doc)
    {
        _path = path;
        _doc = doc;
    }

    public override string Name => _path.Name;

    public override string Path => _path.FullName;

    protected override string ReferenceKind => "package reference";

    public string Parent => _path.DirectoryName!;

    /// <summary>
    /// Every target framework declared by this project - either via a single
    /// <c>TargetFramework</c>, or a <c>;</c>-separated <c>TargetFrameworks</c> for a
    /// multi-targeted project. Falls back to whatever is declared in an imported
    /// <c>Directory.Build.props</c>, or <see cref="NuGetFramework.AnyFramework"/> if none is
    /// found.
    /// </summary>
    public IReadOnlyList<NuGetFramework> TargetFrameworks =>
        _targetFrameworks ??= GetTargetFrameworks();

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
        bool usePrerelease,
        ILogger logger,
        NuGetFramework? tfm = null,
        IReadOnlyCollection<string>? allowedLicenses = null,
        IReadOnlyCollection<string>? alignWithTfm = null
    ) =>
        UpdatePackagesCoreAsync(
                packages,
                dryRun,
                usePrerelease,
                logger,
                tfm,
                allowedLicenses,
                alignWithTfm
            )
            .GetAwaiter()
            .GetResult();

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
    }

    protected override Task PersistAsync(bool dryRun)
    {
        if (!dryRun)
        {
            _doc.Save(Path);
        }

        return Task.CompletedTask;
    }

    protected override void OnChangesApplied() => UpdateEntityFrameworkVersion();

    protected override void OnUnparseableVersion(Candidate candidate, ILogger logger) =>
        LogParseError(logger, candidate.VersionString, ReferenceKind, candidate.SiteText);

    private sealed class CsprojCandidate : Candidate
    {
        public required XmlElement Element { get; init; }

        public required string AttributeName { get; init; }
    }

    /// <summary>
    /// Maps each distinct (item type, package id, version string) occurrence declared directly
    /// in this csproj to the target framework(s) - resolved via a real per-framework MSBuild
    /// evaluation - it actually applies to. Only attempted for a genuinely multi-targeted project
    /// (a single/no <c>TargetFramework</c> is already evaluated correctly by a plain evaluation,
    /// so there's nothing more precise to learn); falls back to an empty map - meaning every
    /// candidate uses <see cref="TargetFrameworks"/> as before - if evaluation isn't attempted or
    /// fails for any reason (e.g. a malformed project, or a missing SDK).
    /// </summary>
    private IReadOnlyDictionary<string, IReadOnlyCollection<NuGetFramework>> GetCandidateTfmsByKey()
    {
        if (_candidateTfmsByKey is not null)
        {
            return _candidateTfmsByKey;
        }

        Dictionary<string, HashSet<NuGetFramework>> map = new(StringComparer.Ordinal);

        if (TargetFrameworks.Count > 1 && !TargetFrameworks.Contains(NuGetFramework.AnyFramework))
        {
            try
            {
                var byTfm = MsBuildProjectInspector.GetPackageItemSourcesByTfm(
                    Path,
                    [.. TargetFrameworks.Select(x => x.GetShortFolderName())]
                );

                foreach (var (tfmString, sources) in byTfm)
                {
                    var tfm = NuGetFramework.Parse(tfmString);

                    foreach (
                        var source in sources.Where(x =>
                            x.Version is not null
                            && string.Equals(x.SourceFile, Path, StringComparison.OrdinalIgnoreCase)
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
            }
            catch (Exception)
            {
                // Fall back to no per-candidate information at all - every candidate then uses
                // TargetFrameworks, same as before this per-framework evaluation existed.
                map.Clear();
            }
        }

        return _candidateTfmsByKey = map.ToDictionary(
            x => x.Key,
            x => (IReadOnlyCollection<NuGetFramework>)x.Value,
            StringComparer.Ordinal
        );
    }

    private NuGetFramework[] GetTargetFrameworks()
    {
        var targetFrameworks =
            RetriveTargetFramework.GetTargetFrameworks(Path)
            ?? GetTargetFrameworksFromDirectoryBuildProps(new(Parent));

        return targetFrameworks is null
            ? [NuGetFramework.AnyFramework]
            : targetFrameworks.Select(NuGetFramework.Parse).ToArray();
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
