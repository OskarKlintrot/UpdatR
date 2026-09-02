using System.Xml;
using Microsoft.Extensions.Logging;
using NuGet.Frameworks;
using NuGet.Versioning;
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
internal sealed partial class PropsFile : PackageContainer
{
    private readonly FileInfo _path;
    private readonly XmlDocument _doc;

    private PropsFile(FileInfo path, XmlDocument doc, IReadOnlyCollection<NuGetFramework> tfms)
    {
        _path = path;
        _doc = doc;
        TargetFrameworks = tfms.Count > 0 ? tfms : [NuGetFramework.AnyFramework];
    }

    public override string Name => _path.Name;

    public override string Path => _path.FullName;

    protected override string ReferenceKind => "package reference";

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
    ) =>
        UpdatePackagesCoreAsync(packages, dryRun, usePrerelease, logger, tfm, allowedLicenses)
            .GetAwaiter()
            .GetResult();

    protected override IReadOnlyCollection<NuGetFramework> ResolveTfms(
        NuGetFramework? tfmOverride
    ) => tfmOverride is null ? TargetFrameworks : [tfmOverride];

    protected override IEnumerable<Candidate> EnumerateCandidates()
    {
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

            yield return new PropsFileCandidate
            {
                PackageId = packageId,
                VersionString = item.GetAttribute("Version"),
                SiteText = item.OuterXml,
                Element = item,
            };
        }
    }

    protected override void ApplyVersionUpdate(Candidate candidate, string newVersionString) =>
        ((PropsFileCandidate)candidate).Element.SetAttribute("Version", newVersionString);

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

    private sealed class PropsFileCandidate : Candidate
    {
        public required XmlElement Element { get; init; }
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
}
