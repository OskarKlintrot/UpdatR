using System.Text.Json;
using System.Text.Json.Serialization;

namespace UpdatR;

/// <summary>
/// Optional JSON config file (<c>.updatrrc</c>) that can be used instead of, or together with,
/// command line arguments / <see cref="Updater.UpdateAsync"/> parameters.
/// </summary>
/// <param name="ExcludePackages">Packages to exclude. Supports * as wildcard.</param>
/// <param name="AllowedLicenses">
/// Only update to (and warn about) versions whose license contains one of these values.
/// </param>
/// <param name="DefaultTarget">
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
public sealed record UpdatRConfig(
    [property: JsonPropertyName("excludePackages")] string[]? ExcludePackages,
    [property: JsonPropertyName("allowedLicenses")] string[]? AllowedLicenses,
    [property: JsonPropertyName("defaultTarget")] string? DefaultTarget = null,
    [property: JsonPropertyName("excludeFiles")] string[]? ExcludeFiles = null,
    [property: JsonPropertyName("alignWithTfm")] string[]? AlignWithTfm = null
)
{
    /// <summary>
    /// File name of the config file, <c>.updatrrc</c>.
    /// </summary>
    public const string FileName = ".updatrrc";

    private static readonly string[] KnownProperties =
    [
        "excludePackages",
        "allowedLicenses",
        "defaultTarget",
        "excludeFiles",
        "alignWithTfm",
    ];

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly JsonSerializerOptions WriteJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    /// <summary>
    /// Looks for a <c>.updatrrc</c> file, first next to <paramref name="path"/> (i.e. in
    /// <paramref name="path"/> itself if it's a directory, or its parent directory if it's a
    /// file) and, if not found there, in the current working directory.
    /// </summary>
    internal static UpdatRConfig? Load(string path) => Load(path, out _);

    /// <summary>
    /// Same as <see cref="Load(string)"/>, but also returns the directory the config file, if
    /// any, was found in - needed to resolve a relative <see cref="DefaultTarget"/>.
    /// </summary>
    internal static UpdatRConfig? Load(string path, out string? configDirectory)
    {
        var filePath = FindConfigFile(path);

        configDirectory = filePath is null ? null : Path.GetDirectoryName(filePath);

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
        var candidate = Path.Combine(targetDirectory, FileName);

        if (File.Exists(candidate))
        {
            return candidate;
        }

        var currentDirectory = Directory.GetCurrentDirectory();

        if (
            string.Equals(
                Path.GetFullPath(targetDirectory),
                Path.GetFullPath(currentDirectory),
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return null;
        }

        candidate = Path.Combine(currentDirectory, FileName);

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
    /// Resolves <paramref name="defaultTarget"/> (from a <c>.updatrrc</c> file's
    /// <c>defaultTarget</c>) relative to <paramref name="configDirectory"/>, unless it's already
    /// rooted.
    /// </summary>
    internal static string ResolveDefaultTarget(string configDirectory, string defaultTarget)
    {
        var resolved = Path.IsPathRooted(defaultTarget)
            ? defaultTarget
            : Path.Combine(configDirectory, defaultTarget);

        return Path.GetFullPath(resolved);
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
    /// Creates a new <c>.updatrrc</c> file containing all known options, empty.
    /// </summary>
    /// <param name="path">
    /// Path to write the file to. If it's an existing directory, or doesn't exist and doesn't
    /// look like a file path (no extension and doesn't already end with <see cref="FileName"/>),
    /// the file is created as <see cref="FileName"/> inside it. Otherwise <paramref name="path"/>
    /// is used as the file path directly.
    /// </param>
    /// <param name="overwrite">Overwrite the file if it already exists.</param>
    /// <returns>The full path of the created file.</returns>
    /// <exception cref="IOException">
    /// The file already exists and <paramref name="overwrite"/> is <see langword="false"/>.
    /// </exception>
    public static string CreateFile(string path, bool overwrite = false)
    {
        var filePath = ResolveFilePath(path);

        if (!overwrite && File.Exists(filePath))
        {
            throw new IOException($"'{filePath}' already exists.");
        }

        var directory = Path.GetDirectoryName(filePath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(
            new UpdatRConfig(
                ExcludePackages: [],
                AllowedLicenses: [],
                DefaultTarget: null,
                ExcludeFiles: [],
                AlignWithTfm: []
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
            return Path.Combine(path, FileName);
        }

        if (File.Exists(path))
        {
            return path;
        }

        if (
            Path.GetFileName(path).Equals(FileName, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(Path.GetExtension(path))
        )
        {
            return path;
        }

        return Path.Combine(path, FileName);
    }

    /// <summary>
    /// Validates the content of a <c>.updatrrc</c> file: that it's valid JSON containing a JSON
    /// object, that it doesn't contain unknown option, that <c>excludePackages</c> /
    /// <c>allowedLicenses</c> / <c>excludeFiles</c> / <c>alignWithTfm</c> - if present - are
    /// arrays of non-empty strings, and that <c>defaultTarget</c> - if present - is a non-empty
    /// string.
    /// </summary>
    /// <param name="json">The content of the <c>.updatrrc</c> file.</param>
    /// <param name="configDirectory">
    /// If provided, and the config has a <c>defaultTarget</c>, it's resolved relative to this
    /// directory and verified to exist on disk. Left out, <c>defaultTarget</c> is only checked
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
            document = JsonDocument.Parse(json);
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

                if (property.Name.Equals("defaultTarget", StringComparison.OrdinalIgnoreCase))
                {
                    ValidateString(property, errors);

                    if (
                        configDirectory is not null
                        && property.Value.ValueKind is JsonValueKind.String
                        && property.Value.GetString() is { Length: > 0 } defaultTarget
                    )
                    {
                        var resolved = ResolveDefaultTarget(configDirectory, defaultTarget);

                        if (!Directory.Exists(resolved) && !File.Exists(resolved))
                        {
                            errors.Add(
                                $"'defaultTarget' resolved to '{resolved}', which does not exist."
                            );
                        }
                    }
                }
                else
                {
                    ValidateStringArray(property, errors);
                }
            }
        }

        return errors;
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
