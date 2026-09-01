using System.Xml;

namespace UpdatR.Domain.Utils;

internal static class RetriveTargetFramework
{
    public static string? GetTargetFrameworkFromDirectoryBuildProps(DirectoryInfo path)
    {
        var targetFrameworks = GetTargetFrameworksFromDirectoryBuildProps(path);

        return targetFrameworks is null ? null : targetFrameworks[0];
    }

    /// <summary>
    /// Walks up from <paramref name="path"/> looking for a <c>Directory.Build.props</c> that
    /// declares a <c>TargetFramework</c> or <c>TargetFrameworks</c> - stopping as soon as one is
    /// found that either doesn't import a <c>Directory.Build.props</c> from a parent directory,
    /// or does declare a target framework.
    /// </summary>
    public static IReadOnlyList<string>? GetTargetFrameworksFromDirectoryBuildProps(
        DirectoryInfo path
    )
    {
        var file = GetDirectoryBuildProps(path);

        var targetFrameworks = file is null ? null : GetTargetFrameworks(file.FullName);

        while (targetFrameworks is null)
        {
            // Make sure we don't try to go beyond C:\
            if (Path.GetPathRoot(path.FullName) == path.FullName)
            {
                return null;
            }

            if (file is null || ImportsFromAbove(file))
            {
                path = path.Parent!;

                file = GetDirectoryBuildProps(path);

                targetFrameworks = file is null ? null : GetTargetFrameworks(file.FullName);
            }
            else
            {
                return null;
            }
        }

        return targetFrameworks;

        static FileInfo? GetDirectoryBuildProps(DirectoryInfo path)
        {
            return path.GetFiles(
                    "Directory.Build.props",
                    new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive }
                )
                .FirstOrDefault();
        }
    }

    public static bool ImportsFromAbove(FileInfo file)
    {
        var doc = new XmlDocument();

        doc.Load(file.FullName);

        // Check if current Directory.Build.props imports another Directory.Build.props:
        // <Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))" />

        return doc.SelectSingleNode(
            "//Import[@Project=\"$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))\"]"
        )
            is not null;
    }

    public static string? GetTargetFramework(string path)
    {
        var targetFrameworks = GetTargetFrameworks(path);

        return targetFrameworks is null ? null : targetFrameworks[0];
    }

    /// <summary>
    /// Reads the target framework(s) declared in the project file at <paramref name="path"/>.
    /// Prefers a multi-targeted <c>TargetFrameworks</c> (plural, <c>;</c>-separated) property
    /// group, falling back to a single <c>TargetFramework</c> property. Returns
    /// <see langword="null"/> if neither is declared.
    /// </summary>
    public static IReadOnlyList<string>? GetTargetFrameworks(string path)
    {
        var doc = new XmlDocument();

        doc.Load(path);

        var targetFrameworks = doc.SelectNodes("/Project/PropertyGroup/TargetFrameworks")
            ?.OfType<XmlElement>()
            .FirstOrDefault()
            ?.InnerText;

        if (!string.IsNullOrWhiteSpace(targetFrameworks))
        {
            return targetFrameworks
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        var targetFramework = doc.SelectNodes("/Project/PropertyGroup/TargetFramework")
            ?.OfType<XmlElement>()
            .SingleOrDefault()
            ?.InnerText;

        return string.IsNullOrWhiteSpace(targetFramework) ? null : [targetFramework];
    }
}
