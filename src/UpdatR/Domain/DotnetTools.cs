using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using NuGet.Frameworks;
using NuGet.Versioning;
using UpdatR.Internals;

namespace UpdatR.Domain;

internal sealed partial class DotnetTools : PackageContainer
{
    private readonly FileInfo _path;
    private readonly IEnumerable<Csproj> _affectedCsprojs;
    private List<string>? _packageIds;
    private string? _rawJson;
    private JsonObject? _tools;
    private IReadOnlyCollection<ToolPackagePin> _toolPackagePins =
    [
        ToolPackagePin.EntityFrameworkCore,
    ];
    private readonly List<(string PackageId, string OldVersion, string NewVersion)> _replacements =
    [];

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

    public override string Name => _path.Name;

    public override string Path => _path.FullName;

    public string Parent => _path.DirectoryName!;

    protected override string ReferenceKind => "tool reference";

    public IEnumerable<string> PackageIds => _packageIds ??= GetPackageIds();

    private NuGetVersion? HighestAllowedVersion(string pinnedPackageIdPattern) =>
        _affectedCsprojs.Min(x => x.GetPinnedVersion(pinnedPackageIdPattern));

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
        IReadOnlyCollection<string>? allowedLicenses = null,
        IReadOnlyCollection<string>? alignWithTfm = null,
        IReadOnlyCollection<ToolPackagePin>? toolPackagePins = null,
        IReadOnlyCollection<PackageVersionPolicy>? packagePolicies = null
    )
    {
        if (toolPackagePins is { Count: > 0 })
        {
            _toolPackagePins = toolPackagePins;
        }

        _rawJson = await File.ReadAllTextAsync(Path).ConfigureAwait(false);
        _tools = JsonSerializer
            .Deserialize<JsonObject>(_rawJson, s_jsonSerializerOptions)
            ?["tools"]?.AsObject();
        _replacements.Clear();

        return await UpdatePackagesCoreAsync(
                packages,
                dryRun,
                usePrerelease,
                logger,
                tfm: null,
                allowedLicenses,
                alignWithTfm,
                packagePolicies
            )
            .ConfigureAwait(false);
    }

    protected override IReadOnlyCollection<NuGetFramework> ResolveTfms(
        NuGetFramework? tfmOverride
    ) => [tfmOverride ?? NuGetFramework.AnyFramework];

    protected override IReadOnlyCollection<NuGetFramework> ResolveAlignmentTfms(
        IReadOnlyCollection<NuGetFramework> candidateTfms
    ) => [.. _affectedCsprojs.SelectMany(x => x.TargetFrameworks)];

    protected override IEnumerable<Candidate> EnumerateCandidates()
    {
        if (_tools is null)
        {
            yield break;
        }

        foreach (var element in _tools)
        {
            var packageId = element.Key;

            if (packageId is null)
            {
                continue;
            }

            var toolObject = element.Value?.AsObject();

            if (toolObject is null)
            {
                continue;
            }

            var versionProperty = toolObject.FirstOrDefault(property =>
                property.Key.Equals("version", StringComparison.OrdinalIgnoreCase)
            );

            var rawVersion = versionProperty.Value?.GetValue<string>();

            if (rawVersion is null || !NuGetVersion.TryParse(rawVersion, out _))
            {
                continue;
            }

            yield return new DotnetToolsCandidate
            {
                PackageId = packageId,
                VersionString = rawVersion,
                SiteText = toolObject.ToJsonString(),
            };
        }
    }

    protected override PackageMetadata AdjustUpdateTarget(
        Candidate candidate,
        NuGetPackage package,
        NuGetVersion currentVersion,
        PackageMetadata updateTo
    )
    {
        // A pinned tool (e.g. dotnet-ef, pinned to Microsoft.EntityFrameworkCore by default) must
        // never move ahead of the highest version of its pinned package any affected project can
        // still resolve, so the tool and the package(s) it drives (e.g. EF migrations) never
        // mismatch.
        var pin = _toolPackagePins.FirstOrDefault(x =>
            x.ToolPackageId.Equals(candidate.PackageId, StringComparison.OrdinalIgnoreCase)
        );

        if (
            pin is not null
            && HighestAllowedVersion(pin.PinnedPackageIdPattern) is { } highestAllowedVersion
            && package.TryGet(highestAllowedVersion, out _)
            && highestAllowedVersion <= updateTo.Version
        )
        {
            return package.Get(highestAllowedVersion);
        }

        return updateTo;
    }

    protected override void ApplyVersionUpdate(Candidate candidate, string newVersionString) =>
        _replacements.Add((candidate.PackageId, candidate.VersionString, newVersionString));

    protected override async Task PersistAsync(bool dryRun)
    {
        if (dryRun || _replacements.Count == 0)
        {
            return;
        }

        var patchedJson = ReplaceVersionsInRawJson(_rawJson!, _replacements);

        await File.WriteAllTextAsync(Path, patchedJson).ConfigureAwait(false);
    }

    private sealed class DotnetToolsCandidate : Candidate;

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
}
