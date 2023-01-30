using System.Xml;

namespace UpdatR.Domain.Utils;

internal static class RetriveTargetFramework
{
    public static string? GetTargetFrameworkFromDirectoryBuildProps(DirectoryInfo path)
    {
        var file = GetDirectoryBuildProps(path);

        var targetFramework = file is null ? null : GetTargetFramework(file.FullName);

        while (targetFramework is null)
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

                targetFramework = file is null ? null : GetTargetFramework(file.FullName);
            }
            else
            {
                return null;
            }
        }

        return targetFramework;

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
        var doc = new XmlDocument();

        doc.Load(path);

        return doc.SelectNodes("/Project/PropertyGroup/TargetFramework")
            ?.OfType<XmlElement>()
            .SingleOrDefault()
            ?.InnerText;
    }
}
