using System.Xml;

namespace UpdatR.Update.Domain.Utils;

internal static class RetriveTargetFramework
{
    public static string? GetTargetFrameworkFromDirectoryBuildProps(DirectoryInfo path)
    {
        var file = GetDirectoryBuildProps(path);

        var targetFramework = file is null ? null : GetTargetFramework(file.FullName);

        while (Path.GetPathRoot(path.FullName) != path.FullName)
        {
            if (file is null)
            {
                path = path.Parent!;

                file = GetDirectoryBuildProps(path);

                targetFramework = file is null ? null : GetTargetFramework(file.FullName);
            }

            if (targetFramework is not null)
            {
                return targetFramework;
            }
        }

        return null;

        static FileInfo? GetDirectoryBuildProps(DirectoryInfo path)
        {
            return path
                .GetFiles("Directory.Build.props", new EnumerationOptions
                {
                    MatchCasing = MatchCasing.CaseInsensitive
                })
                .FirstOrDefault();
        }
    }

    public static string? GetTargetFramework(string path)
    {
        var doc = new XmlDocument();

        doc.Load(path);

        return doc
            .SelectNodes("/Project/PropertyGroup/TargetFramework")?
            .OfType<XmlElement>()
            .SingleOrDefault()?
            .InnerText;
    }
}
