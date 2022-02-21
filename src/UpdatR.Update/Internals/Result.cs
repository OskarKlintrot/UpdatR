using NuGet.Protocol;
using NuGet.Versioning;

namespace UpdatR.Update.Internals;

internal sealed class Result
{
    private readonly List<(string Name, string Source)> _unauthorizedSources = new();
    private readonly Dictionary<string, HashSet<string>> _unknownPackages = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _rootPath;

    private Dictionary<string, ProjectWithPackages> _projects { get; } = new(StringComparer.OrdinalIgnoreCase);

    internal Result(string rootPath)
    {
        if (!Directory.Exists(rootPath))
        {
            throw new ArgumentException("Root does not exist.", nameof(rootPath));
        }

        _rootPath = rootPath;
    }

    internal IDictionary<string, IEnumerable<string>> UnknownPackages =>
        _unknownPackages.ToDictionary(x => x.Key, x => x.Value.AsEnumerable());

    internal IEnumerable<ProjectWithPackages> Projects => _projects.Values;

    internal IEnumerable<(string Name, string Source)> UnauthorizedSources => _unauthorizedSources;

    internal bool TryAddProject(ProjectWithPackages project)
    {
        project = project with
        {
            Path = Path.GetRelativePath(_rootPath, project.Path)
        };

        if (_projects.ContainsKey(project.Path))
        {
            return false;
        }
        else
        {
            _projects[project.Path] = project;

            return true;
        }
    }

    internal bool TryAddUnauthorizedSource(string name, string source)
    {
        if (_unauthorizedSources.Any(x => x.Name.Equals(name, StringComparison.Ordinal)))
        {
            return false;
        }
        else
        {
            _unauthorizedSources.Add((name, source));

            return true;
        }
    }

    internal bool TryAddUnknownPackage(string packageId, string project)
    {
        if (_unknownPackages.TryGetValue(packageId, out var projects))
        {
            projects.Add(project);
        }
        else
        {
            _unknownPackages[packageId] = new HashSet<string> { project };
        }
        return true;
    }
}

internal sealed record ProjectWithPackages
{
    private readonly HashSet<string> _unknownPackages = new();
    private readonly List<UpdatedPackage> _updatedPackages = new();
    private readonly List<DeprecatedPackage> _deprecatedPackages = new();
    private readonly List<VulnerablePackage> _vulnerablePackages = new();

    public string Path { get; init; }
    public IEnumerable<string> UnknownPackages => _unknownPackages;
    public IEnumerable<UpdatedPackage> UpdatedPackages => _updatedPackages;
    public IEnumerable<DeprecatedPackage> DeprecatedPackages => _deprecatedPackages;
    public IEnumerable<VulnerablePackage> VulnerablePackages => _vulnerablePackages;

    public ProjectWithPackages(string path)
    {
        Path = path;
    }

    public void AddUnknownPackage(string packageId)
    {
        _unknownPackages.Add(packageId);
    }

    public void AddUpdatedPackage(UpdatedPackage package)
    {
        _updatedPackages.Add(package);
    }

    public void AddDeprecatedPackage(DeprecatedPackage package)
    {
        _deprecatedPackages.Add(package);
    }

    public void AddVulnerablePackage(VulnerablePackage package)
    {
        _vulnerablePackages.Add(package);
    }

    public bool AnyPackages()
        => _updatedPackages.Count > 0
            || _deprecatedPackages.Count > 0
            || _vulnerablePackages.Count > 0;
}

internal sealed class UpdatedPackage
{
    public string PackageId { get; }
    public NuGetVersion From { get; }
    public NuGetVersion To { get; }

    public UpdatedPackage(string packageId, NuGetVersion from, NuGetVersion to)
    {
        PackageId = packageId;
        From = from;
        To = to;
    }
}

internal sealed class DeprecatedPackage
{
    public string PackageId { get; }
    public NuGetVersion Version { get; }
    public PackageDeprecationMetadata DeprecationMetadata { get; }

    public DeprecatedPackage(string packageId, NuGetVersion version, PackageDeprecationMetadata deprecationMetadata)
    {
        PackageId = packageId;
        Version = version;
        DeprecationMetadata = deprecationMetadata;
    }
}

internal sealed class VulnerablePackage
{
    public string PackageId { get; }
    public NuGetVersion Version { get; }
    public IEnumerable<PackageVulnerabilityMetadata> Vulnerabilities { get; }

    public VulnerablePackage(string packageId, NuGetVersion version, IEnumerable<PackageVulnerabilityMetadata> vulnerabilities)
    {
        PackageId = packageId;
        Version = version;
        Vulnerabilities = vulnerabilities;
    }
}

internal record PackageMetadata(
    NuGetVersion Version,
    PackageDeprecationMetadata? DeprecationMetadata,
    IEnumerable<PackageVulnerabilityMetadata>? Vulnerabilities);
