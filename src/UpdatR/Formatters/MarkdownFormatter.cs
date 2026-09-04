using System.Globalization;
using System.Text;

namespace UpdatR.Formatters;

public static class MarkdownFormatter
{
    public static string GenerateTitle(Summary summary)
    {
        var sb = new StringBuilder();

        if (summary.UpdatedPackagesCount == 0)
        {
            sb.Append("📦 Update no packages");
        }
        else if (summary.UpdatedPackagesCount == 1)
        {
            sb.Append("📦 Update ").Append(summary.UpdatedPackages.Single().PackageId);
        }
        else
        {
            sb.Append("📦 Update ").Append(summary.UpdatedPackagesCount).Append(" packages");
        }

        return sb.ToString();
    }

    public static string GenerateDescription(Summary summary)
    {
        var sb = new StringBuilder();

        if (summary.UpdatedPackages.Any())
        {
            sb.Append(summary.UpdatedPackagesCount)
                .Append(" package(s) were updated in ")
                .Append(
                    summary
                        .UpdatedPackages.SelectMany(x => x.Updates.Select(y => y.Project))
                        .Distinct()
                        .Count()
                )
                .AppendLine(" projects:")
                .AppendLine()
                .Append("| ")
                .Append(
                    string.Join('|', summary.UpdatedPackages.Select(x => $" {x.PackageId} ")).Trim()
                )
                .AppendLine(" |")
                .AppendLine();
        }

        if (summary.VulnerablePackages.Any())
        {
            sb.AppendLine("## Vulnerable packages");
            sb.AppendLine();
            VulnerablePackages(sb, summary.VulnerablePackages);
            sb.AppendLine();
        }

        if (summary.DeprecatedPackages.Any())
        {
            sb.AppendLine("## Deprecated packages");
            sb.AppendLine();
            DeprecatedPackages(sb, summary.DeprecatedPackages);
            sb.AppendLine();
        }

        if (summary.LicenseMismatchPackages.Any())
        {
            sb.AppendLine("## License mismatches");
            sb.AppendLine();
            LicenseMismatchPackages(sb, summary.LicenseMismatchPackages);
            sb.AppendLine();
        }

        if (summary.UpdatedPackages.Any())
        {
            sb.AppendLine("## Updated packages");
            sb.AppendLine();
            UpdatedPackages(sb, summary.UpdatedPackages);
            sb.AppendLine();
        }

        if (summary.UnknownPackages.Count > 0)
        {
            sb.AppendLine("## Not found packages");
            sb.AppendLine();
            UnknownPackages(sb, summary.UnknownPackages);
            sb.AppendLine();
        }

        if (summary.UnsupportedRangePackages.Any())
        {
            sb.AppendLine("## Unsupported version ranges");
            sb.AppendLine();
            UnsupportedRangePackages(sb, summary.UnsupportedRangePackages);
            sb.AppendLine();
        }

        if (summary.SkippedUpdatePackages.Any())
        {
            sb.AppendLine("## Skipped updates");
            sb.AppendLine();
            SkippedUpdatePackages(sb, summary.SkippedUpdatePackages);
            sb.AppendLine();
        }

        if (summary.UnauthorizedSources.Any())
        {
            sb.AppendLine("## Unauthorized sources");
            sb.AppendLine();
            UnauthorizedSources(sb, summary.UnauthorizedSources);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    public static string Generate(Summary summary)
    {
        var sb = new StringBuilder();

        var title = "# " + GenerateTitle(summary);

        sb.AppendLine(title);

        sb.AppendLine();

        var description = GenerateDescription(summary);

        sb.Append(description);

        return sb.ToString();
    }

    private static void UnauthorizedSources(
        StringBuilder sb,
        IEnumerable<(string Name, string Source)> unauthorizedSources
    )
    {
        sb.AppendLine("| Name | Source |");
        sb.AppendLine("|:-----|:-------|");

        foreach (var (name, source) in unauthorizedSources)
        {
            sb.AppendFormat(CultureInfo.InvariantCulture, "| {0} | {1} |", name, source)
                .AppendLine();
        }

        sb.AppendLine();
    }

    private static void UnknownPackages(
        StringBuilder sb,
        IDictionary<string, IEnumerable<string>> unknownPackages
    )
    {
        foreach (var package in unknownPackages)
        {
            sb.Append("### ").AppendLine(package.Key).AppendLine();

            sb.AppendLine("Used in:");

            foreach (var project in package.Value)
            {
                sb.Append("- ").AppendLine(project);
            }

            sb.AppendLine();
        }
    }

    private static void UnsupportedRangePackages(
        StringBuilder sb,
        IEnumerable<UnsupportedRangePackage> unsupportedRangePackages
    )
    {
        foreach (var (packageId, ranges) in unsupportedRangePackages)
        {
            sb.Append("### ").AppendLine(packageId).AppendLine();

            foreach (var (versionRange, projects) in ranges)
            {
                sb.AppendFormat(CultureInfo.InvariantCulture, "Version range: `{0}`", versionRange)
                    .AppendLine()
                    .AppendLine();

                sb.AppendLine(
                        "> A newer version may be available, but UpdatR doesn't know how to safely rewrite this version range - update it manually if needed."
                    )
                    .AppendLine();

                sb.AppendLine("Used in:");

                foreach (var project in projects)
                {
                    sb.Append("- ").AppendLine(project);
                }

                sb.AppendLine();
            }
        }
    }

    private static void DeprecatedPackages(
        StringBuilder sb,
        IEnumerable<DeprecatedPackage> deprecatedPackages
    )
    {
        foreach (var (packageId, versions) in deprecatedPackages)
        {
            sb.Append("### ").AppendLine(packageId);

            var padding = versions
                .SelectMany(x => x.Projects.Select(y => y.Length))
                .DefaultIfEmpty(0)
                .Max();

            foreach (var ((version, metadata), projects) in versions)
            {
                if (metadata.Message is not null)
                {
                    sb.AppendJoin(
                            Environment.NewLine,
                            metadata.Message.Split("\n").Select(x => $"> {x}")
                        )
                        .AppendLine()
                        .AppendLine();
                }

                sb.AppendFormat(
                        CultureInfo.InvariantCulture,
                        "Reason(s): {0}",
                        string.Join(", ", metadata.Reasons)
                    )
                    .AppendLine()
                    .AppendLine();

                if (metadata.AlternatePackage is not null)
                {
                    sb.AppendFormat(
                            CultureInfo.InvariantCulture,
                            "Alternate Package: {0}",
                            metadata.AlternatePackage.PackageId
                        )
                        .AppendLine()
                        .AppendLine();

                    sb.AppendFormat(
                            CultureInfo.InvariantCulture,
                            "Version range: {0}",
                            metadata.AlternatePackage.Range
                        )
                        .AppendLine()
                        .AppendLine();
                }

                sb.AppendLine("Package used in:");
                sb.AppendLine();
                sb.AppendLine("| Project | Version |");
                sb.AppendLine("|:--------|:--------|");

                foreach (var project in projects)
                {
                    sb.AppendFormat(
                            CultureInfo.InvariantCulture,
                            "| {0} | {1} |",
                            project.PadRight(padding),
                            version
                        )
                        .AppendLine();
                }
            }
            sb.AppendLine();
        }
    }

    private static void VulnerablePackages(
        StringBuilder sb,
        IEnumerable<VulnerablePackage> vulnerablePackages
    )
    {
        foreach (var package in vulnerablePackages)
        {
            sb.Append("### ").AppendLine(package.PackageId);

            foreach (var ((version, vulnerabilities), projects) in package.Versions)
            {
                foreach (var vulnerability in vulnerabilities)
                {
                    sb.AppendFormat(
                            CultureInfo.InvariantCulture,
                            "Version {0} with severity {1}: {2}",
                            version,
                            vulnerability.Severity,
                            vulnerability.AdvisoryUrl
                        )
                        .AppendLine();
                }

                sb.AppendLine();
                sb.AppendLine("#### Used in:");

                foreach (var project in projects)
                {
                    sb.Append("- ").AppendLine(project);
                }
            }

            sb.AppendLine();
        }
    }

    private static void LicenseMismatchPackages(
        StringBuilder sb,
        IEnumerable<LicenseMismatchPackage> licenseMismatchPackages
    )
    {
        foreach (var package in licenseMismatchPackages)
        {
            sb.Append("### ").AppendLine(package.PackageId);

            foreach (var (version, projects) in package.Versions)
            {
                sb.AppendFormat(
                        CultureInfo.InvariantCulture,
                        "{0} {1}: {2}",
                        version.IsInstalledVersion
                            ? "Installed version"
                            : "Newer version available",
                        version.NuGetVersion,
                        version.License
                    )
                    .AppendLine();

                sb.AppendLine();
                sb.AppendLine("#### Used in:");

                foreach (var project in projects)
                {
                    sb.Append("- ").AppendLine(project);
                }
            }

            sb.AppendLine();
        }
    }

    private static void SkippedUpdatePackages(
        StringBuilder sb,
        IEnumerable<SkippedUpdatePackage> skippedUpdatePackages
    )
    {
        foreach (var package in skippedUpdatePackages)
        {
            sb.Append("### ").AppendLine(package.PackageId);

            foreach (var (version, projects) in package.Versions)
            {
                sb.AppendFormat(
                        CultureInfo.InvariantCulture,
                        "Newer version available: {0} ({1})",
                        version.NuGetVersion,
                        version.Reason
                    )
                    .AppendLine();

                sb.AppendLine();
                sb.AppendLine("#### Used in:");

                foreach (var project in projects)
                {
                    sb.Append("- ").AppendLine(project);
                }
            }

            sb.AppendLine();
        }
    }

    private static void UpdatedPackages(
        StringBuilder sb,
        IEnumerable<UpdatedPackage> updatedPackages
    )
    {
        foreach (var packages in updatedPackages)
        {
            if (!packages.Updates.Any())
            {
                continue;
            }

            var padRightProject = packages
                .Updates.Select(x => x.Project.Length)
                .OrderByDescending(x => x)
                .First();

            var padRightFrom = packages
                .Updates.Select(x => x.From.ToString().Length)
                .OrderByDescending(x => x)
                .First();

            sb.Append("### ").AppendLine(packages.PackageId);

            sb.AppendLine("| Project   | From   | To |");
            sb.AppendLine("|:----------|:-------|:---|");

            foreach (var (from, to, project) in packages.Updates)
            {
                sb.AppendFormat(
                        CultureInfo.InvariantCulture,
                        "| {0} | {1} | {2} |",
                        project.PadRight(padRightProject),
                        from.ToString().PadRight(padRightFrom),
                        to
                    )
                    .AppendLine();
            }
            sb.AppendLine();
        }
    }
}
