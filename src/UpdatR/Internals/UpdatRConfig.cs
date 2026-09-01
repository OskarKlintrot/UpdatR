using System.Text.Json;
using System.Text.Json.Serialization;

namespace UpdatR.Internals;

/// <summary>
/// Optional JSON config file (<c>.updatrrc</c>) that can be used instead of, or together with,
/// command line arguments / <see cref="Updater.UpdateAsync"/> parameters.
/// </summary>
internal sealed record UpdatRConfig(
    [property: JsonPropertyName("excludePackages")] string[]? ExcludePackages,
    [property: JsonPropertyName("allowedLicenses")] string[]? AllowedLicenses
)
{
    internal const string FileName = ".updatrrc";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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
        var candidate = System.IO.Path.Combine(targetDirectory, FileName);

        if (File.Exists(candidate))
        {
            return candidate;
        }

        var currentDirectory = Directory.GetCurrentDirectory();

        if (
            string.Equals(
                System.IO.Path.GetFullPath(targetDirectory),
                System.IO.Path.GetFullPath(currentDirectory),
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return null;
        }

        candidate = System.IO.Path.Combine(currentDirectory, FileName);

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
}
