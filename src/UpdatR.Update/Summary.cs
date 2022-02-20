using NuGet.Protocol;
using NuGet.Versioning;

namespace UpdatR.Update;

public sealed class Summary
{
    private readonly List<(string Name, string Source)> _failedSources = new();
    private readonly string _rootPath;

    private Dictionary<string, Project> _projects { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Summary(string rootPath)
    {
        if (!Directory.Exists(rootPath))
        {
            throw new ArgumentException("Root does not exist.", nameof(rootPath));
        }

        _rootPath = rootPath;
    }

    public IEnumerable<Project> Projects => _projects.Values;

    public IEnumerable<(string Name, string Source)> FailedSources => _failedSources;

    public int UpdatedPackages => Projects
        .SelectMany(x => x.UpdatedPackages)
        .DistinctBy(x => x.PackageId)
        .Count();

    public bool TryAddProject(Project project)
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

    public bool TryAddFailedSource(string name, string source)
    {
        if (_failedSources.Any(x => x.Name.Equals(name, StringComparison.Ordinal)))
        {
            return false;
        }
        else
        {
            _failedSources.Add((name, source));

            return true;
        }
    }
}

public sealed record Project
{
    private readonly List<UpdatedPackage> _updatedPackages = new();
    private readonly List<DeprecatedPackage> _deprecatedPackages = new();
    private readonly List<VulnerablePackage> _vulnerablePackages = new();

    public string Path { get; init; }
    public IEnumerable<UpdatedPackage> UpdatedPackages => _updatedPackages;
    public IEnumerable<DeprecatedPackage> DeprecatedPackages => _deprecatedPackages;
    public IEnumerable<VulnerablePackage> VulnerablePackages => _vulnerablePackages;

    public Project(string path)
    {
        Path = path;
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

public sealed class UpdatedPackage
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

public sealed class DeprecatedPackage
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

public sealed class VulnerablePackage
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

public record PackageMetadata(
    NuGetVersion Version,
    PackageDeprecationMetadata? DeprecationMetadata,
    IEnumerable<PackageVulnerabilityMetadata>? Vulnerabilities);
