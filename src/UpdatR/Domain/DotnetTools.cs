using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using NuGet.Frameworks;
using NuGet.Versioning;
using UpdatR.Internals;

namespace UpdatR.Domain;

internal sealed partial class DotnetTools
{
    private readonly FileInfo _path;
    private readonly IEnumerable<Csproj> _affectedCsprojs;
    private static readonly JsonSerializerOptions s_jsonSerializerOptions = new(
        JsonSerializerDefaults.Web
    )
    {
        WriteIndented = true,
    };

    private DotnetTools(FileInfo path, IEnumerable<Csproj> affectedCsprojs)
    {
        _path = path;
        _affectedCsprojs = affectedCsprojs;
    }

    public string Name => _path.Name;

    public string Path => _path.FullName;

    public string Parent => _path.DirectoryName!;

    public IEnumerable<string> PackageIds => GetPackageIds();

    private NuGetVersion? HighestAllowedDotnetEf() =>
        _affectedCsprojs.Min(x => x.EntityFrameworkVersion);

    public static DotnetTools Create(string path, IEnumerable<Csproj> affectedCsprojs)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(
                $"'{nameof(path)}' cannot be null or whitespace.",
                nameof(path)
            );
        }

        var file = new FileInfo(path);

        if (!file.Exists)
        {
            throw new ArgumentException($"'{nameof(path)}' does not exist.", nameof(path));
        }

        if (!file.Name.Equals("dotnet-tools.json", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"'{nameof(path)}' is not named dotnet-tools.json.",
                nameof(path)
            );
        }

        return new DotnetTools(new(System.IO.Path.GetFullPath(path)), affectedCsprojs);
    }

    public async Task<ProjectWithPackages?> UpdatePackagesAsync(
        IDictionary<string, NuGetPackage?> packages,
        bool dryRun,
        bool usePrerelease,
        ILogger logger
    )
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

                if (
                    property.Key.Equals("version", StringComparison.OrdinalIgnoreCase)
                    && NuGetVersion.TryParse(property.Value?.GetValue<string>(), out var version)
                )
                {
                    if (!packages.TryGetValue(packageId, out var package))
                    {
                        project.AddUnknownPackage(packageId);
                    }
                    else if (package is not null)
                    {
                        if (
                            package.TryGetLatestComparedTo(
                                version,
                                NuGetFramework.AnyFramework,
                                usePrerelease,
                                out var updateTo
                            )
                        )
                        {
                            // EF Bodge
                            if (
                                packageId.Equals("dotnet-ef", StringComparison.OrdinalIgnoreCase)
                                && HighestAllowedDotnetEf() is { } highestAllowedDotnetEf
                                && package.TryGet(highestAllowedDotnetEf, out _)
                                && highestAllowedDotnetEf <= updateTo.Version
                            )
                            {
                                updateTo = package.Get(highestAllowedDotnetEf);
                            }

                            toolObject.Add(property.Key, updateTo.Version.ToString());

                            // EF Bodge
                            if (version != updateTo.Version)
                            {
                                LogUpdateSuccessful(
                                    logger,
                                    Name,
                                    packageId,
                                    version,
                                    updateTo.Version
                                );

                                project.AddUpdatedPackage(
                                    new(packageId, version, updateTo.Version)
                                );
                            }
                        }
                        else
                        {
                            if (package.TryGet(version, out var packageMetadata))
                            {
                                if (packageMetadata.DeprecationMetadata is not null)
                                {
                                    project.AddDeprecatedPackage(
                                        new(packageId, version, packageMetadata.DeprecationMetadata)
                                    );
                                }

                                if (packageMetadata.Vulnerabilities?.Any() == true)
                                {
                                    project.AddVulnerablePackage(
                                        new(packageId, version, packageMetadata.Vulnerabilities)
                                    );
                                }
                            }
                        }
                    }
                }

                // Add it back if needed
                toolObject.TryAdd(property.Key, property.Value);
            }
        }

        if (!dryRun && project.UpdatedPackages.Any())
        {
            var json = JsonSerializer.Serialize(config, s_jsonSerializerOptions);

            await File.WriteAllTextAsync(Path, json + Environment.NewLine);
        }

        return project;
    }

    private List<string> GetPackageIds()
    {
        var tools = new List<string>();

        var json = File.ReadAllText(Path);
        var foo = JsonSerializer.Deserialize<JsonObject>(json);

        var packageIds = foo
            ?["tools"]?.AsObject()
            .Select(x => (PackageId: x.Key, Version: x.Value?["version"]?.GetValue<string>()))
            .Where(x => NuGetVersion.TryParse(x.Version, out _))
            .Select(x => x.PackageId);

        if (packageIds?.Any() == true)
        {
            tools.AddRange(packageIds);
        }

        return tools;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Tool object in {Path} was null.")]
    static partial void LogToolObjectNull(ILogger logger, string path);

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
}
