using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using NuGet.Frameworks;
using NuGet.Versioning;
using UpdatR.Update.Domain.Utils;
using UpdatR.Update.Internals;
using static UpdatR.Update.Domain.Utils.RetriveTargetFramework;

namespace UpdatR.Update.Domain;

internal sealed partial class DotnetTools
{
    private readonly FileInfo _path;
    private NuGetFramework? _targetFramework;

    private DotnetTools(FileInfo path, NuGetFramework? targetFramework)
    {
        _path = path;
        _targetFramework = targetFramework;
    }

    public string Name => _path.Name;

    public string Path => _path.FullName;

    public string Parent => _path.DirectoryName!;

    public NuGetFramework TargetFramework => _targetFramework ??= GetTargetFramework(Parent);

    public IEnumerable<string> PackageIds => GetPackageIds();

    public static DotnetTools Create(string path, NuGetFramework? targetFramework)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException($"'{nameof(path)}' cannot be null or whitespace.", nameof(path));
        }

        var file = new FileInfo(path);

        if (!file.Exists)
        {
            throw new ArgumentException($"'{nameof(path)}' does not exist.", nameof(path));
        }

        if (!file.Name.Equals("dotnet-tools.json", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"'{nameof(path)}' is not named dotnet-tools.json.", nameof(path));
        }

        return new DotnetTools(new(System.IO.Path.GetFullPath(path)), targetFramework);
    }

    public async Task<ProjectWithPackages?> UpdatePackagesAsync(IEnumerable<NuGetPackage> packages, bool dryRun, ILogger logger)
    {
        var config = JsonSerializer.Deserialize<JsonObject>(await File.ReadAllTextAsync(Path));

        if (config is null)
        {
            return null;
        }

        var tools = config["tools"]?.AsObject();

        if (tools is null)
        {
            return null;
        }

        var project = new ProjectWithPackages(Path);

        var packagesDict = packages.ToDictionary(x => x.PackageId, x => x, StringComparer.OrdinalIgnoreCase);

        foreach (var element in tools)
        {
            var packageId = element.Key;

            if (packageId is null)
            {
                continue;
            }

            var toolObject = element.Value?.AsObject();

            if (toolObject is null)
            {
                LogToolObjectNull(logger, Path);

                continue;
            }

            foreach (var property in toolObject.ToList())
            {
                toolObject.Remove(property.Key);
                if (property.Key.Equals("version", StringComparison.OrdinalIgnoreCase)
                    && NuGetVersion.TryParse(property.Value?.GetValue<string>(), out var version))
                {
                    if (packagesDict.TryGetValue(packageId, out var package))
                    {
                        if (package.TryGetLatestComparedTo(version, TargetFramework, out var updateTo))
                        {
                            toolObject.Add(property.Key, updateTo.Version.ToString());

                            project.AddUpdatedPackage(new(packageId, version, updateTo.Version));
                        }
                        else
                        {
                            if (package.TryGet(version, out var packageMetadata))
                            {
                                if (packageMetadata.DeprecationMetadata is not null)
                                {
                                    project.AddDeprecatedPackage(new(packageId, version, packageMetadata.DeprecationMetadata));
                                }

                                if (packageMetadata.Vulnerabilities?.Any() == true)
                                {
                                    project.AddVulnerablePackage(new(packageId, version, packageMetadata.Vulnerabilities));
                                }
                            }
                        }
                    }
                    else
                    {
                        project.AddUnknownPackage(packageId);
                    }
                }
                else
                {
                    toolObject.Add(property);
                }
            }
        }

        if (!dryRun && project.UpdatedPackages.Any())
        {
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true,
            });

            await File.WriteAllTextAsync(Path, json + Environment.NewLine);
        }

        return project;
    }

    private IEnumerable<string> GetPackageIds()
    {
        var tools = new List<string>();

        var json = File.ReadAllText(Path);
        var foo = JsonSerializer.Deserialize<JsonObject>(json);

        var packageIds = foo?["tools"]?
            .AsObject()
            .Select(x => (PackageId: x.Key, Version: x.Value?["version"]?.GetValue<string>()))
            .Where(x => NuGetVersion.TryParse(x.Version, out _))
            .Select(x => x.PackageId);

        if (packageIds?.Any() == true)
        {
            tools.AddRange(packageIds);
        }

        return tools;
    }

    private static NuGetFramework GetTargetFramework(string path)
    {
        var parent = Directory.GetParent(path)!;

        var csproj = parent
            .GetFiles("*.csproj", new EnumerationOptions
            {
                MatchCasing = MatchCasing.CaseInsensitive,
                RecurseSubdirectories = true,
                MaxRecursionDepth = 1,
            })
            .FirstOrDefault();

        var targetFramework = csproj is not null
            ? RetriveTargetFramework.GetTargetFramework(csproj.FullName)
            : GetTargetFrameworkFromDirectoryBuildProps(new(parent.FullName));

        return targetFramework is null
            ? NuGetFramework.AnyFramework
            : NuGetFramework.Parse(targetFramework);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Tool object in {Path} was null.")]
    static partial void LogToolObjectNull(ILogger logger, string path);
}
