using System.Text.Json;
using System.Text.Json.Serialization;
using SysPath = System.IO.Path;

namespace UpdatR;

/// <summary>
/// Optional JSON config file (<c>.updatrrc</c>) that can be used instead of, or together with,
/// command line arguments / <see cref="Updater.UpdateAsync"/> parameters. Both <c>//</c> line
/// comments and <c>/* */</c> block comments, as well as trailing commas, are allowed.
/// </summary>
/// <param name="ExcludePackages">Packages to exclude. Supports * as wildcard.</param>
/// <param name="AllowedLicenses">
/// Only update to (and warn about) versions whose license contains one of these values.
/// </param>
/// <param name="Path">
/// Path to a solution or project(s), relative to the directory this config file is in. Used
/// instead of the current directory when no target path is explicitly given (i.e. the resolved
/// target path is the current directory).
/// </param>
/// <param name="ExcludeFiles">
/// Files to exclude, relative to the resolved target path. Supports * as wildcard.
/// </param>
/// <param name="AlignWithTfm">
/// Packages to keep aligned with a project's target framework's major version, instead of
/// updating to a newer version whose major just happens to also be compatible (e.g. a package
/// that multi-targets both <c>net9.0</c> and <c>net10.0</c> in the same, higher-major, release).
/// Supports * as wildcard. Only applies to modern (<c>net5.0</c>+) target frameworks, and only if
/// the currently installed version's major isn't already ahead of the target framework's - if it
/// is, updates are left unrestricted. Also applies to <c>dotnet-tools.json</c> entries, aligned
/// with the target framework(s) of the csproj(s) the manifest applies to (e.g. keeping
/// <c>dotnet-ef</c> in step with <c>Microsoft.EntityFrameworkCore</c>).
/// </param>
/// <param name="ToolPackagePins">
/// Extra tool-to-package pin rules for <c>dotnet-tools.json</c> entries, on top of the built-in
/// default that pins <c>dotnet-ef</c> to <c>Microsoft.EntityFrameworkCore*</c>. An entry here for
/// <c>dotnet-ef</c> overrides the default instead of adding to it.
/// </param>
/// <param name="PackagePolicies">
/// Per-package (or wildcard-matched) fixed major-version caps - see
/// <see cref="UpdatR.PackageVersionPolicy"/>. Merged with
/// <see cref="UpdateOptions.PackagePolicies"/> (that collection first).
/// </param>
/// <param name="FailOn">
/// Minimum severity of finding - <c>"outdated"</c>, <c>"deprecated"</c> or <c>"vulnerable"</c> -
/// that should make <see cref="Summary.ShouldFail"/> true. Overridden by
/// <see cref="UpdateOptions.FailOn"/> if given. Defaults to <see cref="UpdatR.FailOn.None"/>.
/// </param>
/// <param name="FailOnIncomplete">
/// Also make <see cref="Summary.ShouldFail"/> true when the run was incomplete - i.e. it hit an
/// unauthorized package source, or couldn't resolve a package on any source. Overridden by
/// <see cref="UpdateOptions.FailOnIncomplete"/> if given. Defaults to <see langword="false"/>.
/// </param>
/// <param name="Schema">
/// Optional JSON Schema URI used by editors to provide completion and validation. It does not
/// affect UpdatR's behavior.
/// </param>
public sealed record UpdatRConfig(
    [property: JsonPropertyName("excludePackages")] string[]? ExcludePackages,
    [property: JsonPropertyName("allowedLicenses")] string[]? AllowedLicenses,
    [property: JsonPropertyName("path")] string? Path = null,
    [property: JsonPropertyName("excludeFiles")] string[]? ExcludeFiles = null,
    [property: JsonPropertyName("alignWithTfm")] string[]? AlignWithTfm = null,
    [property: JsonPropertyName("toolPackagePins")] ToolPackagePinConfig[]? ToolPackagePins = null,
    [property: JsonPropertyName("packagePolicies")] PackagePolicyConfig[]? PackagePolicies = null,
    [property: JsonPropertyName("failOn")] string? FailOn = null,
    [property: JsonPropertyName("failOnIncomplete")] bool? FailOnIncomplete = null,
    [property: JsonPropertyName("$schema"), JsonPropertyOrder(-1)] string? Schema = null
)
{
    /// <summary>
    /// File name of the config file, <c>.updatrrc</c>.
    /// </summary>
    public const string FileName = ".updatrrc";

    internal const string SchemaUrl =
        "https://raw.githubusercontent.com/OskarKlintrot/UpdatR/main/schemas/updatrrc.schema.json";

    private static readonly string[] KnownProperties =
    [
        "excludePackages",
        "allowedLicenses",
        "path",
        "excludeFiles",
        "alignWithTfm",
        "toolPackagePins",
        "packagePolicies",
        "failOn",
        "failOnIncomplete",
        "$schema",
    ];

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions WriteJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private static readonly JsonDocumentOptions JsonDocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Looks for a <c>.updatrrc</c> file, first next to <paramref name="path"/> (i.e. in
    /// <paramref name="path"/> itself if it's a directory, or its parent directory if it's a
    /// file) and, if not found there, in the current working directory.
    /// </summary>
    internal static UpdatRConfig? Load(string path) => Load(path, out _);

    /// <summary>
    /// Same as <see cref="Load(string)"/>, but also returns the directory the config file, if
    /// any, was found in - needed to resolve a relative <see cref="Path"/>.
    /// </summary>
    internal static UpdatRConfig? Load(string path, out string? configDirectory)
    {
        var filePath = FindConfigFile(path);

        configDirectory = filePath is null ? null : SysPath.GetDirectoryName(filePath);

        if (filePath is null)
        {
            return null;
        }

        var json = File.ReadAllText(filePath);

        return JsonSerializer.Deserialize<UpdatRConfig>(json, JsonOptions);
    }

    private static string? FindConfigFile(string path)
    {
        var targetDirectory = ResolveDirectory(path);
        var candidate = SysPath.Combine(targetDirectory, FileName);

        if (File.Exists(candidate))
        {
            return candidate;
        }

        var currentDirectory = Directory.GetCurrentDirectory();

        if (
            string.Equals(
                SysPath.GetFullPath(targetDirectory),
                SysPath.GetFullPath(currentDirectory),
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return null;
        }

        candidate = SysPath.Combine(currentDirectory, FileName);

        return File.Exists(candidate) ? candidate : null;
    }

    private static string ResolveDirectory(string path)
    {
        if (File.Exists(path))
        {
            return new FileInfo(path).DirectoryName ?? Directory.GetCurrentDirectory();
        }

        return Directory.Exists(path) ? path : Directory.GetCurrentDirectory();
    }

    /// <summary>
    /// Resolves <paramref name="path"/> (from a <c>.updatrrc</c> file's <c>path</c>) relative to
    /// <paramref name="configDirectory"/>, unless it's already rooted.
    /// </summary>
    internal static string ResolvePath(string configDirectory, string path)
    {
        var resolved = SysPath.IsPathRooted(path) ? path : SysPath.Combine(configDirectory, path);

        return SysPath.GetFullPath(resolved);
    }

    /// <summary>
    /// Merges CLI/parameter values with config file values (union, case-insensitive,
    /// order-preserving, CLI/parameter values first).
    /// </summary>
    internal static string[]? Merge(string[]? fromArgs, string[]? fromConfig)
    {
        if (fromArgs is null || fromArgs.Length == 0)
        {
            return fromConfig;
        }

        if (fromConfig is null || fromConfig.Length == 0)
        {
            return fromArgs;
        }

        return fromArgs.Concat(fromConfig).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>
    /// An example <c>.updatrrc</c>, meant as a realistic starting point rather than an
    /// exhaustive reference: it excludes the Roslyn compiler packages (which projects rarely
    /// intend to update directly), pins <c>dotnet-ef</c> to <c>Microsoft.EntityFrameworkCore*</c>
    /// explicitly (redundant with the built-in default, but shown here for discoverability), and
    /// keeps Entity Framework Core and <c>Microsoft.Extensions.*</c> - both of which commonly
    /// ship versions that multi-target a newer TFM than the project actually targets - aligned
    /// with the project's target framework.
    /// </summary>
    private const string ExampleJson = """
        {
          "$schema": "https://raw.githubusercontent.com/OskarKlintrot/UpdatR/main/schemas/updatrrc.schema.json",
          "excludePackages": [
            "Microsoft.CodeAnalysis.*"
          ],
          "toolPackagePins": [
            {
              "tool": "dotnet-ef",
              "package": "Microsoft.EntityFrameworkCore*"
            }
          ],
          "alignWithTfm": [
            "Microsoft.EntityFrameworkCore",
            "Microsoft.EntityFrameworkCore.*",
            "Microsoft.Extensions.*",
            "System.Net.Http.Json"
          ]
        }
        """;

    /// <summary>
    /// Creates a new <c>.updatrrc</c> file, either containing all known options, empty, or - if
    /// <paramref name="example"/> is <see langword="true"/> - a realistic, populated example.
    /// </summary>
    /// <param name="path">
    /// Path to write the file to. If it's an existing directory, or doesn't exist and doesn't
    /// look like a file path (no extension and doesn't already end with <see cref="FileName"/>),
    /// the file is created as <see cref="FileName"/> inside it. Otherwise <paramref name="path"/>
    /// is used as the file path directly.
    /// </param>
    /// <param name="overwrite">Overwrite the file if it already exists.</param>
    /// <param name="example">
    /// Write a populated, realistic example instead of all options present but empty.
    /// </param>
    /// <returns>The full path of the created file.</returns>
    /// <exception cref="IOException">
    /// The file already exists and <paramref name="overwrite"/> is <see langword="false"/>.
    /// </exception>
    public static string CreateFile(string path, bool overwrite = false, bool example = false)
    {
        var filePath = ResolveFilePath(path);

        if (!overwrite && File.Exists(filePath))
        {
            throw new IOException($"'{filePath}' already exists.");
        }

        var directory = SysPath.GetDirectoryName(filePath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = example
            ? ExampleJson
            : JsonSerializer.Serialize(
                new UpdatRConfig(
                    ExcludePackages: [],
                    AllowedLicenses: [],
                    Path: null,
                    ExcludeFiles: [],
                    AlignWithTfm: [],
                    ToolPackagePins: [],
                    PackagePolicies: [],
                    FailOn: null,
                    FailOnIncomplete: null,
                    Schema: SchemaUrl
                ),
                WriteJsonOptions
            );

        File.WriteAllText(filePath, json);

        return filePath;
    }

    private static string ResolveFilePath(string path)
    {
        if (Directory.Exists(path))
        {
            return SysPath.Combine(path, FileName);
        }

        if (File.Exists(path))
        {
            return path;
        }

        if (
            SysPath.GetFileName(path).Equals(FileName, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(SysPath.GetExtension(path))
        )
        {
            return path;
        }

        return SysPath.Combine(path, FileName);
    }

    /// <summary>
    /// Validates the content of a <c>.updatrrc</c> file: that it's valid JSON (comments and
    /// trailing commas are allowed) containing a JSON object, that it doesn't contain unknown
    /// option, that <c>excludePackages</c> / <c>allowedLicenses</c> / <c>excludeFiles</c> /
    /// <c>alignWithTfm</c> - if present - are arrays of non-empty strings, and that
    /// <c>path</c> and <c>$schema</c> - if present - are non-empty strings.
    /// </summary>
    /// <param name="json">The content of the <c>.updatrrc</c> file.</param>
    /// <param name="configDirectory">
    /// If provided, and the config has a <c>path</c>, it's resolved relative to this
    /// directory and verified to exist on disk. Left out, <c>path</c> is only checked
    /// for being a non-empty string.
    /// </param>
    /// <returns>
    /// A list of human-readable validation errors. Empty if <paramref name="json"/> is valid.
    /// </returns>
    public static IReadOnlyList<string> Validate(string json, string? configDirectory = null)
    {
        List<string> errors = [];

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(json, JsonDocumentOptions);
        }
        catch (JsonException exception)
        {
            errors.Add($"'{FileName}' is not valid JSON: {exception.Message}");

            return errors;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                errors.Add($"'{FileName}' must contain a JSON object.");

                return errors;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!KnownProperties.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
                {
                    errors.Add(
                        $"Unknown option '{property.Name}'. Known options are: "
                            + string.Join(", ", KnownProperties)
                            + "."
                    );

                    continue;
                }

                if (
                    property.Name.Equals("path", StringComparison.OrdinalIgnoreCase)
                    || property.Name.Equals("$schema", StringComparison.OrdinalIgnoreCase)
                )
                {
                    ValidateString(property, errors);

                    if (
                        property.Name.Equals("path", StringComparison.OrdinalIgnoreCase)
                        && configDirectory is not null
                        && property.Value.ValueKind is JsonValueKind.String
                        && property.Value.GetString() is { Length: > 0 } path
                    )
                    {
                        var resolved = ResolvePath(configDirectory, path);

                        if (!Directory.Exists(resolved) && !File.Exists(resolved))
                        {
                            errors.Add($"'path' resolved to '{resolved}', which does not exist.");
                        }
                    }
                }
                else if (
                    property.Name.Equals("toolPackagePins", StringComparison.OrdinalIgnoreCase)
                )
                {
                    ValidateToolPackagePins(property, errors);
                }
                else if (
                    property.Name.Equals("packagePolicies", StringComparison.OrdinalIgnoreCase)
                )
                {
                    ValidatePackagePolicies(property, errors);
                }
                else if (property.Name.Equals("failOn", StringComparison.OrdinalIgnoreCase))
                {
                    ValidateFailOn(property, errors);
                }
                else if (
                    property.Name.Equals("failOnIncomplete", StringComparison.OrdinalIgnoreCase)
                )
                {
                    ValidateBoolean(property, errors);
                }
                else
                {
                    ValidateStringArray(property, errors);
                }
            }
        }

        return errors;
    }

    /// <summary>
    /// Parses a <c>.updatrrc</c> <c>failOn</c> value (case-insensitive), or <see langword="null"/>
    /// if <paramref name="value"/> is <see langword="null"/> or empty.
    /// </summary>
    /// <exception cref="UpdatRException">
    /// <paramref name="value"/> isn't a recognized <see cref="UpdatR.FailOn"/> name.
    /// </exception>
    internal static FailOn? ParseFailOn(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (Enum.TryParse<FailOn>(value, ignoreCase: true, out var failOn))
        {
            return failOn;
        }

        throw new UpdatRException(
            $"'{value}' is not a valid 'failOn' value. Valid values are: "
                + string.Join(", ", Enum.GetNames<FailOn>())
                + "."
        );
    }

    private static void ValidateBoolean(JsonProperty property, List<string> errors)
    {
        if (
            property.Value.ValueKind
            is not (JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null)
        )
        {
            errors.Add($"'{property.Name}' must be a boolean");
        }
    }

    private static void ValidateFailOn(JsonProperty property, List<string> errors)
    {
        if (property.Value.ValueKind is JsonValueKind.Null)
        {
            return;
        }

        if (
            property.Value.ValueKind is not JsonValueKind.String
            || !Enum.TryParse<FailOn>(property.Value.GetString(), ignoreCase: true, out _)
        )
        {
            errors.Add(
                $"'{property.Name}' must be one of: " + string.Join(", ", Enum.GetNames<FailOn>())
            );
        }
    }

    private static void ValidateToolPackagePins(JsonProperty property, List<string> errors)
    {
        if (property.Value.ValueKind is JsonValueKind.Null)
        {
            return;
        }

        if (property.Value.ValueKind is not JsonValueKind.Array)
        {
            errors.Add($"'{property.Name}' must be an array of objects.");

            return;
        }

        var index = 0;

        foreach (var item in property.Value.EnumerateArray())
        {
            if (item.ValueKind is not JsonValueKind.Object)
            {
                errors.Add($"'{property.Name}[{index}]' must be an object.");

                index++;

                continue;
            }

            foreach (var key in new[] { "tool", "package" })
            {
                if (
                    !item.TryGetProperty(key, out var value)
                    || value.ValueKind is not JsonValueKind.String
                    || string.IsNullOrWhiteSpace(value.GetString())
                )
                {
                    errors.Add($"'{property.Name}[{index}].{key}' must be a non-empty string.");
                }
            }

            index++;
        }
    }

    private static void ValidatePackagePolicies(JsonProperty property, List<string> errors)
    {
        if (property.Value.ValueKind is JsonValueKind.Null)
        {
            return;
        }

        if (property.Value.ValueKind is not JsonValueKind.Array)
        {
            errors.Add($"'{property.Name}' must be an array of objects.");

            return;
        }

        var index = 0;

        foreach (var item in property.Value.EnumerateArray())
        {
            if (item.ValueKind is not JsonValueKind.Object)
            {
                errors.Add($"'{property.Name}[{index}]' must be an object.");

                index++;

                continue;
            }

            if (
                !item.TryGetProperty("package", out var packageValue)
                || packageValue.ValueKind is not JsonValueKind.String
                || string.IsNullOrWhiteSpace(packageValue.GetString())
            )
            {
                errors.Add($"'{property.Name}[{index}].package' must be a non-empty string.");
            }

            if (
                !item.TryGetProperty("maxMajor", out var maxMajorValue)
                || maxMajorValue.ValueKind is not JsonValueKind.Number
                || !maxMajorValue.TryGetInt32(out var maxMajor)
                || maxMajor < 0
            )
            {
                errors.Add($"'{property.Name}[{index}].maxMajor' must be a non-negative integer.");
            }

            index++;
        }
    }

    private static void ValidateString(JsonProperty property, List<string> errors)
    {
        if (property.Value.ValueKind is JsonValueKind.Null)
        {
            return;
        }

        if (
            property.Value.ValueKind is not JsonValueKind.String
            || string.IsNullOrWhiteSpace(property.Value.GetString())
        )
        {
            errors.Add($"'{property.Name}' must be a non-empty string.");
        }
    }

    private static void ValidateStringArray(JsonProperty property, List<string> errors)
    {
        if (property.Value.ValueKind is JsonValueKind.Null)
        {
            return;
        }

        if (property.Value.ValueKind is not JsonValueKind.Array)
        {
            errors.Add($"'{property.Name}' must be an array of strings.");

            return;
        }

        var index = 0;

        foreach (var item in property.Value.EnumerateArray())
        {
            if (
                item.ValueKind is not JsonValueKind.String
                || string.IsNullOrWhiteSpace(item.GetString())
            )
            {
                errors.Add($"'{property.Name}[{index}]' must be a non-empty string.");
            }

            index++;
        }
    }
}

/// <summary>
/// A <c>.updatrrc</c>-declared tool-to-package pin rule; see <see cref="ToolPackagePin"/> for what
/// it does. Deserialized separately from <see cref="ToolPackagePin"/> since JSON property names
/// (<c>tool</c>/<c>package</c>) are shorter than what would otherwise be idiomatic public API
/// property names.
/// </summary>
/// <param name="Tool">The dotnet tool's package id, e.g. <c>dotnet-ef</c>.</param>
/// <param name="Package">
/// Package id pattern (case-insensitive) matched against package ids referenced by an affected
/// project to find the package this tool is pinned to - the same <c>*</c>-wildcard matching used
/// for e.g. <c>alignWithTfm</c>, matched against the whole package id, e.g.
/// <c>Microsoft.EntityFrameworkCore*</c> to also match <c>Microsoft.EntityFrameworkCore.Sqlite</c>
/// and other packages in the same family.
/// </param>
public sealed record ToolPackagePinConfig(
    [property: JsonPropertyName("tool")] string Tool,
    [property: JsonPropertyName("package")] string Package
);

/// <summary>
/// A <c>.updatrrc</c>-declared package version policy; see <see cref="PackageVersionPolicy"/> for
/// what it does. Deserialized separately since JSON property names (<c>package</c>/
/// <c>maxMajor</c>) are shorter than what would otherwise be idiomatic public API property names.
/// </summary>
/// <param name="Package">
/// Package id pattern to match, supports <c>*</c> as wildcard - e.g. <c>Serilog*</c>.
/// </param>
/// <param name="MaxMajor">The highest major version an update may move to.</param>
public sealed record PackagePolicyConfig(
    [property: JsonPropertyName("package")] string Package,
    [property: JsonPropertyName("maxMajor")] int MaxMajor
);
