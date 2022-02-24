using System.Globalization;
using System.Text;

namespace UpdatR.Update.Formatters;
public static class TextFormatter
{
    public static string PlainText(Summary summary)
    {
        var sb = new StringBuilder();

        sb.AppendLine("------------------------------");
        Title(sb, summary);
        sb.AppendLine();
        sb.AppendLine("------------------------------");

        if (summary.VulnerablePackages.Any())
        {
            sb.AppendLine("Vulnerable packages");
            sb.AppendLine("--");
            VulnerablePackages(sb, summary.VulnerablePackages);
            sb.AppendLine();
            sb.AppendLine("------------------------------");
        }

        if (summary.DeprecatedPackages.Any())
        {
            sb.AppendLine("Deprecated packages");
            sb.AppendLine("--");
            DeprecatedPackages(sb, summary.DeprecatedPackages);
            sb.AppendLine();
            sb.AppendLine("------------------------------");
        }

        if (summary.UpdatedPackages.Any())
        {
            sb.AppendLine("Updated packages");
            sb.AppendLine("--");
            UpdatedPackages(sb, summary.UpdatedPackages);
            sb.AppendLine();
            sb.AppendLine("------------------------------");
        }

        if (summary.UnknownPackages.Count > 0)
        {
            sb.AppendLine("Not found packages");
            sb.AppendLine("--");
            UnknownPackages(sb, summary.UnknownPackages);
            sb.AppendLine();
            sb.AppendLine("------------------------------");
        }

        if (summary.UnauthorizedSources.Any())
        {
            sb.AppendLine("Unauthorized sources");
            sb.AppendLine("--");
            UnauthorizedSources(sb, summary.UnauthorizedSources);
            sb.AppendLine();
            sb.AppendLine("------------------------------");
        }

        return sb.ToString();
    }

    private static void UnauthorizedSources(StringBuilder sb, IEnumerable<(string Name, string Source)> unauthorizedSources)
    {
        foreach (var (name, source) in unauthorizedSources)
        {
            sb.AppendFormat(
                new CultureInfo("en-US"),
                "{0} ({1})",
                name,
                source);
            sb.AppendLine();
            sb.AppendLine("--");
        }
    }

    private static void UnknownPackages(StringBuilder sb, IDictionary<string, IEnumerable<string>> unknownPackages)
    {
        foreach (var package in unknownPackages)
        {
            sb.AppendLine(package.Key);
            sb.AppendLine("Used in:");
            foreach (var project in package.Value)
            {
                sb.Append("- ").AppendLine(project);
            }
            sb.AppendLine("--");
        }
    }

    private static void Title(StringBuilder sb, Summary summary)
    {
        if (summary.UpdatedPackagesCount == 0)
        {
            sb.AppendLine("Updated no packages.");
        }
        else if (summary.UpdatedPackagesCount == 1)
        {
            sb.Append("📦 Updated ").AppendLine(summary.UpdatedPackages.Single().PackageId);
        }
        else
        {
            sb.Append("📦 Updated ").Append(summary.UpdatedPackagesCount).AppendLine(" packages.");
        }
    }

    private static void DeprecatedPackages(StringBuilder sb, IEnumerable<DeprecatedPackage> deprecatedPackages)
    {
        foreach (var (packageId, versions) in deprecatedPackages)
        {
            sb.AppendLine(packageId);

            var padding = versions
                .SelectMany(x => x.Projects.Select(y => y.Length))
                .OrderByDescending(x => x)
                .First();

            foreach (var ((version, metadata), projects) in versions)
            {
                sb.AppendFormat(
                    new CultureInfo("en-US"),
                    "Reason(s): {0}",
                    string.Join(", ", metadata.Reasons));

                sb.AppendLine();

                sb.AppendLine(metadata.Message.Replace("\n", Environment.NewLine));

                if (metadata.AlternatePackage is not null)
                {
                    sb
                        .AppendFormat(
                            new CultureInfo("en-US"),
                            "Alternate Package: {0}",
                            metadata.AlternatePackage.PackageId)
                        .AppendLine();

                    sb
                        .AppendFormat(
                            new CultureInfo("en-US"),
                            "Version range: {0}",
                            metadata.AlternatePackage.Range)
                        .AppendLine();
                }

                sb.AppendLine("Package used in:");

                foreach (var project in projects)
                {
                    sb.AppendFormat(
                        new CultureInfo("en-US"),
                        "{0} {1}",
                        project.PadRight(padding),
                        version);

                    sb.AppendLine();
                }
            }
            sb.AppendLine("--");
        }
    }

    private static void VulnerablePackages(StringBuilder sb, IEnumerable<VulnerablePackage> vulnerablePackages)
    {
        foreach (var package in vulnerablePackages)
        {
            sb.AppendLine(package.PackageId);

            foreach (var ((version, vulnerabilities), projects) in package.Versions)
            {
                foreach (var vulnerability in vulnerabilities)
                {
                    sb.AppendFormat(
                        new CultureInfo("en-US"),
                        "Version {0} with severity {1}: {2}",
                        version,
                        vulnerability.Severity,
                        vulnerability.AdvisoryUrl);
                }

                sb.AppendLine();
                sb.AppendLine("Used in:");

                foreach (var project in projects)
                {
                    sb.AppendLine(project);
                }
            }

            sb.AppendLine("--");
        }
    }

    private static void UpdatedPackages(StringBuilder sb, IEnumerable<UpdatedPackage> updatedPackages)
    {
        foreach (var packages in updatedPackages)
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
    }
}
