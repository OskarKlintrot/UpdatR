using System.Text.RegularExpressions;

namespace UpdatR.Domain.Utils;

/// <summary>
/// Shared helper for the simple <c>*</c>-wildcard matching used for <c>excludePackages</c>,
/// <c>packages</c>, <c>excludeFiles</c> and <c>alignWithTfm</c>.
/// </summary>
internal static class SearchPattern
{
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

    public static Regex ConvertToRegex(string matchAgainst)
    {
        var pattern = "^" + string.Join(".*", matchAgainst.Split('*').Select(x => $"({x})")) + "$";

        pattern = pattern.Replace("()$", "$");

        return new Regex(pattern, RegexOptions.IgnoreCase);
    }
}
