using System.Text.Json;
using System.Text.Json.Serialization;

namespace UpdatR;

/// <summary>
/// Optional JSON config file (<c>.updatrrc</c>) that can be used instead of, or together with,
/// command line arguments / <see cref="Updater.UpdateAsync"/> parameters.
/// </summary>
public sealed record UpdatRConfig(
    [property: JsonPropertyName("excludePackages")] string[]? ExcludePackages,
    [property: JsonPropertyName("allowedLicenses")] string[]? AllowedLicenses
)
{
    /// <summary>
    /// File name of the config file, <c>.updatrrc</c>.
    /// </summary>
    public const string FileName = ".updatrrc";

    private static readonly string[] KnownProperties = ["excludePackages", "allowedLicenses"];

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
    internal static UpdatRConfig? Load(string path)
    {
        var filePath = FindConfigFile(path);

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
    /// Creates a new <c>.updatrrc</c> file containing all known properties, empty.
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

        var json = JsonSerializer.Serialize(new UpdatRConfig([], []), WriteJsonOptions);

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
    /// object, that it doesn't contain unknown properties, and that <c>excludePackages</c> /
    /// <c>allowedLicenses</c> - if present - are arrays of non-empty strings.
    /// </summary>
    /// <returns>
    /// A list of human-readable validation errors. Empty if <paramref name="json"/> is valid.
    /// </returns>
    public static IReadOnlyList<string> Validate(string json)
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
                        $"Unknown property '{property.Name}'. Known properties are: "
                            + string.Join(", ", KnownProperties)
                            + "."
                    );

                    continue;
                }

                ValidateStringArray(property, errors);
            }
        }

        return errors;
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
