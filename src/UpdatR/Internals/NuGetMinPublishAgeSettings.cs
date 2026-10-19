using System.Globalization;
using System.Xml.Linq;
using NuGet.Configuration;

namespace UpdatR.Internals;

/// <summary>
/// Reads NuGet's minimum package publication age ("cooldown") configuration from
/// <c>nuget.config</c>: the <c>minPublishAgeHours</c> attribute on <c>packageSources/add</c>
/// and the <c>minPublishAgeExceptions</c> section.
/// </summary>
/// <remarks>
/// NuGet.Configuration 8.0 (shipped with the .NET 11 SDK) exposes this as
/// <c>PackageSource.MinPublishAge</c> and <c>MinPublishAgeExceptions</c>, but the NuGet packages
/// UpdatR references don't have those APIs yet - their settings model drops the unknown
/// attribute and doesn't parse the new section correctly. This class mirrors the upstream
/// semantics using only APIs available in both versions, so it can be replaced by the official
/// APIs once UpdatR references NuGet 8 without changing behavior: NuGet still decides which
/// config files apply and which <c>add</c> item wins for a source, and only the missing
/// attribute/section is read from that file.
/// </remarks>
internal static class NuGetMinPublishAgeSettings
{
    public const string MinPublishAgeHoursAttribute = "minPublishAgeHours";
    public const string MinPublishAgeExceptionsSection = "minPublishAgeExceptions";

    private const string PackageSourcesSection = "packageSources";

    /// <summary>
    /// Mirrors <c>PackageSource.MinPublishAge</c> for every configured package source that sets
    /// <c>minPublishAgeHours</c>, keyed by source name. Sources without the attribute are left
    /// out, i.e. have no minimum publish age.
    /// </summary>
    /// <exception cref="NuGetConfigurationException">
    /// A <c>minPublishAgeHours</c> value isn't a non-negative whole number of hours.
    /// </exception>
    public static IReadOnlyDictionary<string, TimeSpan> GetMinPublishAgeBySourceName(
        ISettings settings
    )
    {
        ArgumentNullException.ThrowIfNull(settings);

        var configFilePaths = settings.GetConfigFilePaths();
        var documents = new Dictionary<string, XDocument?>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase);

        // Same ordering as NuGet's PackageSourceProvider: closer to the user first. A source
        // redefined closer to the user replaces the whole item - including its attributes - so
        // only the first (closest) item per key is considered.
        var items = (
            settings.GetSection(PackageSourcesSection)?.Items.OfType<SourceItem>() ?? []
        ).OrderBy(x => IndexOf(configFilePaths, x.ConfigPath));

        foreach (var item in items)
        {
            if (!seen.Add(item.Key) || item.ConfigPath is not { } configFilePath)
            {
                continue;
            }

            var value = Load(documents, configFilePath)
                ?.Root?.Element(PackageSourcesSection)
                ?.Elements("add")
                .LastOrDefault(x =>
                    string.Equals(
                        (string?)x.Attribute("key"),
                        item.Key,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                ?.Attribute(MinPublishAgeHoursAttribute)
                ?.Value;

            if (value is null)
            {
                continue;
            }

            result[item.Key] = ParseMinPublishAgeHours(item.Key, value, configFilePath);
        }

        return result;
    }

    /// <summary>
    /// Mirrors <c>MinPublishAgeExceptions.GetMinPublishAgeExceptions(ISettings)</c>: the patterns
    /// of the <c>minPublishAgeExceptions</c> section closest to the user. A closer section
    /// replaces sections further away, and an empty closer section clears them.
    /// </summary>
    /// <exception cref="NuGetConfigurationException">
    /// A <c>package</c> element lacks a non-empty <c>pattern</c>.
    /// </exception>
    public static MinPublishAgeExceptionPatterns GetMinPublishAgeExceptions(ISettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var documents = new Dictionary<string, XDocument?>(StringComparer.OrdinalIgnoreCase);

        foreach (var configFilePath in settings.GetConfigFilePaths())
        {
            var sections = Load(documents, configFilePath)
                ?.Root?.Elements(MinPublishAgeExceptionsSection)
                .ToList();

            if (sections is not { Count: > 0 })
            {
                continue;
            }

            var patterns = new List<string>();

            foreach (var element in sections.SelectMany(x => x.Elements()))
            {
                if (element.Name.LocalName == "clear")
                {
                    patterns.Clear();

                    continue;
                }

                if (element.Name.LocalName != "package")
                {
                    continue;
                }

                var pattern = (string?)element.Attribute("pattern");

                if (string.IsNullOrWhiteSpace(pattern))
                {
                    throw new NuGetConfigurationException(
                        $"A 'package' element in the '{MinPublishAgeExceptionsSection}' section is missing a 'pattern'. Path: '{configFilePath}'."
                    );
                }

                patterns.Add(pattern.Trim());
            }

            return new MinPublishAgeExceptionPatterns(patterns);
        }

        return MinPublishAgeExceptionPatterns.Empty;
    }

    internal static TimeSpan ParseMinPublishAgeHours(
        string sourceName,
        string value,
        string configFilePath
    )
    {
        var expanded = Environment.ExpandEnvironmentVariables(value);

        if (
            uint.TryParse(expanded, NumberStyles.None, CultureInfo.InvariantCulture, out var hours)
            && hours <= TimeSpan.MaxValue.TotalHours
        )
        {
            return TimeSpan.FromHours(hours);
        }

        throw new NuGetConfigurationException(
            $"The value '{value}' of '{sourceName}:{MinPublishAgeHoursAttribute}' in the '{PackageSourcesSection}' section is not allowed - it must be a non-negative whole number of hours. Path: '{configFilePath}'."
        );
    }

    private static int IndexOf(IList<string> configFilePaths, string? configFilePath)
    {
        for (var i = 0; i < configFilePaths.Count; i++)
        {
            if (
                string.Equals(
                    configFilePaths[i],
                    configFilePath,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return i;
            }
        }

        return -1;
    }

    private static XDocument? Load(Dictionary<string, XDocument?> documents, string path)
    {
        if (!documents.TryGetValue(path, out var document))
        {
            documents[path] = document = File.Exists(path) ? XDocument.Load(path) : null;
        }

        return document;
    }
}

/// <summary>
/// Mirrors NuGet 8's <c>MinPublishAgeExceptions</c>: package id patterns exempt from a minimum
/// publish age, using the same syntax as package source mapping - an exact package id, a prefix
/// ending in <c>*</c>, or <c>*</c> alone.
/// </summary>
internal sealed class MinPublishAgeExceptionPatterns
{
    private const string Key = NuGetMinPublishAgeSettings.MinPublishAgeExceptionsSection;

    private readonly PackageSourceMapping? _mapping;

    public static MinPublishAgeExceptionPatterns Empty { get; } = new([]);

    public MinPublishAgeExceptionPatterns(IReadOnlyList<string> patterns)
    {
        ArgumentNullException.ThrowIfNull(patterns);

        Patterns = patterns;

        // NuGet itself reuses package source mapping's pattern matching (SearchTree) for these.
        _mapping =
            patterns.Count == 0
                ? null
                : new PackageSourceMapping(
                    new Dictionary<string, IReadOnlyList<string>> { [Key] = patterns }
                );
    }

    public IReadOnlyList<string> Patterns { get; }

    public bool IsEnabled => _mapping is not null;

    public bool HasException(string packageId) =>
        _mapping is not null && _mapping.GetConfiguredPackageSources(packageId).Count > 0;
}
