using System.Globalization;
using System.Text;

namespace UpdatR.Update.Formatters;
public static class TextFormatter
{
    public static string PlainText(Summary summary)
    {
        var sb = new StringBuilder();

        sb.AppendLine("------------------------------");
        sb.Append("Updated ").Append(summary.UpdatedPackages.Count()).AppendLine(" package(s).");
        sb.AppendLine("------------------------------");

        foreach (var packages in summary.UpdatedPackages)
        {
            if (!packages.Updates.Any())
            {
                continue;
            }

            var padRightProject = packages.Updates
                .Select(x => x.Project.Length)
                .OrderByDescending(x => x)
                .First();

            var padRightFrom = packages.Updates
                .Select(x => x.From.ToString().Length)
                .OrderByDescending(x => x)
                .First();

            sb.AppendLine(packages.PackageId);

            foreach (var (from, to, project) in packages.Updates)
            {
                sb.AppendFormat(
                    new CultureInfo("en-US"),
                    "{0} {1} => {2}",
                    project.PadRight(padRightProject),
                    from.ToString().PadRight(padRightFrom),
                    to);

                sb.AppendLine();
            }
            sb.AppendLine("--");
        }

        return sb.ToString();
    }
}
