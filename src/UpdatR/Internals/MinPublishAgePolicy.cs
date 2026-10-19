using NuGet.Versioning;

namespace UpdatR.Internals;

/// <summary>
/// The effective minimum publish age for one package source: versions published more recently
/// than <see cref="MinPublishAge"/> before the run started aren't eligible as update targets,
/// unless the package matches one of the exception patterns. Mirrors NuGet's own check,
/// <c>Published &lt;= UtcNow - MinPublishAge</c>.
/// </summary>
internal sealed class MinPublishAgePolicy(
    TimeSpan minPublishAge,
    MinPublishAgeExceptionPatterns exceptions
)
{
    public static MinPublishAgePolicy Disabled { get; } =
        new(TimeSpan.Zero, MinPublishAgeExceptionPatterns.Empty);

    public TimeSpan MinPublishAge { get; } = minPublishAge;

    public MinPublishAgeExceptionPatterns Exceptions { get; } = exceptions;

    public bool AppliesTo(string packageId) =>
        MinPublishAge > TimeSpan.Zero && !Exceptions.HasException(packageId);

    /// <summary>
    /// <see langword="true"/> if <paramref name="published"/> is after the cutoff
    /// (<paramref name="now"/> minus <see cref="MinPublishAge"/>), i.e. the version is too new.
    /// </summary>
    /// <exception cref="MinimumPublishAgeException">
    /// The policy applies to <paramref name="packageId"/> but <paramref name="published"/> is
    /// unknown, so it can't be checked.
    /// </exception>
    public bool IsTooRecent(
        string sourceName,
        string packageId,
        NuGetVersion version,
        DateTimeOffset? published,
        DateTimeOffset now
    )
    {
        if (!AppliesTo(packageId))
        {
            return false;
        }

        if (published is null)
        {
            throw new MinimumPublishAgeException(
                $"Package source '{sourceName}' did not report when {packageId} {version} was published, so its minimum publish age of {MinPublishAge.TotalHours} hour(s) can't be enforced. Exempt the package via <minPublishAgeExceptions> in nuget.config, or set 'minPublishAgeHours' to 0 to disable the check."
            );
        }

        return published.Value > GetCutoff(now, MinPublishAge);
    }

    internal static DateTimeOffset GetCutoff(DateTimeOffset now, TimeSpan minPublishAge) =>
        now - DateTimeOffset.MinValue <= minPublishAge
            ? DateTimeOffset.MinValue
            : now - minPublishAge;

    /// <summary>
    /// Converts a <c>minPublishAgeHours</c> value from <see cref="UpdateOptions"/> or
    /// <c>.updatrrc</c> into a <see cref="TimeSpan"/>, validating it the same way NuGet validates
    /// the attribute in <c>nuget.config</c>.
    /// </summary>
    /// <exception cref="MinimumPublishAgeException"></exception>
    public static TimeSpan? ParseHours(int? hours, string origin)
    {
        if (hours is null)
        {
            return null;
        }

        if (hours < 0 || hours > TimeSpan.MaxValue.TotalHours)
        {
            throw new MinimumPublishAgeException(
                $"'minPublishAgeHours' from {origin} must be a non-negative whole number of hours, but was {hours}."
            );
        }

        return TimeSpan.FromHours(hours.Value);
    }
}
