using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using NuGet.Frameworks;
using NuGet.Versioning;
using UpdatR.Domain.Utils;
using UpdatR.Internals;

namespace UpdatR.Domain;

internal sealed partial class DotnetTools
{
    private readonly FileInfo _path;
    private readonly IEnumerable<Csproj> _affectedCsprojs;

    // dotnet-tools.json files generated from dotnet new templates may contain
    // template-engine directives such as "//#if (...)" and "//#endif". These
    // are not regular throw-away comments; if we lose them when writing the
    // file back, we break the template. Therefore, comments/trailing commas
    // are only tolerated when *reading* the file to figure out which
    // packages to update. The file is never fully re-serialized; instead,
    // the version strings that changed are patched directly into the
    // original file text, leaving everything else (comments, formatting,
    // whitespace) untouched. See ReplaceVersionsInRawJson.
    private static readonly JsonSerializerOptions s_jsonSerializerOptions = new(
        JsonSerializerDefaults.Web
    )
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonReaderOptions s_jsonReaderOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
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
        ILogger logger,
        IReadOnlyCollection<string>? alignWithTfm = null
    )
    {
        var alignMajor = TfmAlignment.ResolveAlignMajor(
            _affectedCsprojs.SelectMany(x => x.TargetFrameworks).ToList()
        );
        var shouldAlignWithTfm = SearchPattern.CreateSearch(
            alignWithTfm,
            treatNullOrEmptyAs: false
        );

        var rawJson = await File.ReadAllTextAsync(Path);

        var config = JsonSerializer.Deserialize<JsonObject>(rawJson, s_jsonSerializerOptions);

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

        // Version strings that need to be replaced. The file is never fully
        // re-serialized (that would drop comments/formatting); instead the
        // exact "version" values are patched into the original text.
        var replacements = new List<(string PackageId, string OldVersion, string NewVersion)>();

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

            var versionProperty = toolObject.FirstOrDefault(property =>
                property.Key.Equals("version", StringComparison.OrdinalIgnoreCase)
            );

            var rawVersion = versionProperty.Value?.GetValue<string>();

            if (rawVersion is null || !NuGetVersion.TryParse(rawVersion, out var version))
            {
                continue;
            }

            if (!packages.TryGetValue(packageId, out var package))
            {
                project.AddUnknownPackage(packageId);
            }
            else if (package is not null)
            {
                var maxMajor = shouldAlignWithTfm(packageId)
                    ? TfmAlignment.ResolveMaxMajor(alignMajor, version)
                    : null;

                if (
                    package.TryGetLatestComparedTo(
                        version,
                        NuGetFramework.AnyFramework,
                        usePrerelease,
                        out var updateTo,
                        maxMajor: maxMajor
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

                    // EF Bodge
                    if (version != updateTo.Version)
                    {
                        LogUpdateSuccessful(logger, Name, packageId, version, updateTo.Version);

                        project.AddUpdatedPackage(new(packageId, version, updateTo.Version));

                        replacements.Add((packageId, rawVersion, updateTo.Version.ToString()));
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

        if (!dryRun && replacements.Count > 0)
        {
            var patchedJson = ReplaceVersionsInRawJson(rawJson, replacements);

            await File.WriteAllTextAsync(Path, patchedJson);
        }

        return project;
    }

    // Patches only the "version" string values that changed directly into
    // the original file text, leaving comments (e.g. dotnet template-engine
    // directives like "//#if"/"//#endif"), formatting and whitespace
    // untouched. This avoids re-serializing the whole JSON tree, which would
    // otherwise lose comments since System.Text.Json.Nodes does not preserve
    // them.
    private static string ReplaceVersionsInRawJson(
        string rawJson,
        List<(string PackageId, string OldVersion, string NewVersion)> replacements
    )
    {
        if (replacements.Count == 0)
        {
            return rawJson;
        }

        var bytes = Encoding.UTF8.GetBytes(rawJson);
        var reader = new Utf8JsonReader(bytes, s_jsonReaderOptions);

        var spans = new List<(long Start, long Length, string NewValue)>();

        // Path of property names for currently open objects/arrays, used to
        // recognize the "tools" -> "<packageId>" -> "version" shape.
        var propertyPath = new List<string>();
        string? pendingPropertyName = null;

        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.PropertyName:
                    pendingPropertyName = reader.GetString();
                    break;

                case JsonTokenType.StartObject:
                case JsonTokenType.StartArray:
                    propertyPath.Add(pendingPropertyName ?? string.Empty);
                    pendingPropertyName = null;
                    break;

                case JsonTokenType.EndObject:
                case JsonTokenType.EndArray:
                    if (propertyPath.Count > 0)
                    {
                        propertyPath.RemoveAt(propertyPath.Count - 1);
                    }
                    break;

                case JsonTokenType.String:
                    if (
                        pendingPropertyName is not null
                        && propertyPath.Count >= 2
                        && propertyPath[^2].Equals("tools", StringComparison.OrdinalIgnoreCase)
                        && pendingPropertyName.Equals("version", StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        var packageId = propertyPath[^1];
                        var value = reader.GetString();

                        foreach (var replacement in replacements)
                        {
                            if (
                                replacement.PackageId.Equals(
                                    packageId,
                                    StringComparison.OrdinalIgnoreCase
                                )
                                && replacement.OldVersion == value
                            )
                            {
                                spans.Add(
                                    (
                                        reader.TokenStartIndex,
                                        Encoding.UTF8.GetByteCount(replacement.OldVersion) + 2,
                                        replacement.NewVersion
                                    )
                                );

                                break;
                            }
                        }
                    }

                    pendingPropertyName = null;
                    break;

                default:
                    pendingPropertyName = null;
                    break;
            }
        }

        if (spans.Count == 0)
        {
            return rawJson;
        }

        spans.Sort((a, b) => a.Start.CompareTo(b.Start));

        using var stream = new MemoryStream();

        var cursor = 0L;

        foreach (var (start, length, newValue) in spans)
        {
            stream.Write(bytes, (int)cursor, (int)(start - cursor));

            var newValueBytes = Encoding.UTF8.GetBytes($"\"{newValue}\"");

            stream.Write(newValueBytes, 0, newValueBytes.Length);

            cursor = start + length;
        }

        stream.Write(bytes, (int)cursor, (int)(bytes.Length - cursor));

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private List<string> GetPackageIds()
    {
        var tools = new List<string>();

        var json = File.ReadAllText(Path);
        var foo = JsonSerializer.Deserialize<JsonObject>(json, s_jsonSerializerOptions);

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
