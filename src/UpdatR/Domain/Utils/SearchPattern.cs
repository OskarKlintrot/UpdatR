using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace UpdatR.Domain.Utils;

/// <summary>
/// Shared helper for the simple <c>*</c>-wildcard matching used for <c>excludePackages</c>,
/// <c>packages</c>, <c>excludeFiles</c> and <c>alignWithTfm</c>.
/// </summary>
internal static class SearchPattern
{
    // Compiling the same pattern (e.g. "Microsoft.*", reused across every project/package
    // combination) is comparatively expensive, so a process-wide cache avoids repeating it.
    private static readonly ConcurrentDictionary<string, Regex> s_regexCache = new(
        StringComparer.Ordinal
    );

    /// <summary>
    /// Builds a predicate that's <see langword="true"/> if the input matches any of
    /// <paramref name="patterns"/>. Returns a predicate always returning
    /// <paramref name="treatNullOrEmptyAs"/> if <paramref name="patterns"/> is <see langword="null"/>
    /// or empty.
    /// </summary>
    public static Func<string, bool> CreateSearch(
        IReadOnlyCollection<string>? patterns,
        bool treatNullOrEmptyAs
    )
    {
        if (patterns is null || patterns.Count == 0)
        {
            return _ => treatNullOrEmptyAs;
        }

        var regexes = patterns.Select(ConvertToRegex).ToList();

        return str => regexes.Any(x => x.IsMatch(str));
    }

    /// <summary>
    /// Converts <paramref name="matchAgainst"/> - a literal string using <c>*</c> as a wildcard
    /// for "any sequence of characters" - into a <see cref="Regex"/> matching the whole input.
    /// Every non-<c>*</c> segment is treated as a literal (via <see cref="Regex.Escape(string)"/>),
    /// so characters with special meaning in a regex (e.g. <c>.</c>, <c>(</c>, <c>+</c>) match
    /// themselves instead of being interpreted as regex syntax or throwing a
    /// <see cref="RegexParseException"/>.
    /// </summary>
    public static Regex ConvertToRegex(string matchAgainst) =>
        s_regexCache.GetOrAdd(
            matchAgainst,
            static pattern =>
            {
                var regexPattern =
                    "^" + string.Join(".*", pattern.Split('*').Select(Regex.Escape)) + "$";

                return new Regex(
                    regexPattern,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
                );
            }
        );
}
