using System.Globalization;
using System.Text;

namespace UpdatR.Formatters;

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

        if (summary.LicenseMismatchPackages.Any())
        {
            sb.AppendLine("License mismatches");
            sb.AppendLine("--");
            LicenseMismatchPackages(sb, summary.LicenseMismatchPackages);
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

        if (summary.UnsupportedRangePackages.Any())
        {
            sb.AppendLine("Unsupported version ranges");
            sb.AppendLine("--");
            UnsupportedRangePackages(sb, summary.UnsupportedRangePackages);
            sb.AppendLine();
            sb.AppendLine("------------------------------");
        }

        if (summary.SkippedUpdatePackages.Any())
        {
            sb.AppendLine("Skipped updates");
            sb.AppendLine("--");
            SkippedUpdatePackages(sb, summary.SkippedUpdatePackages);
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

    private static void UnauthorizedSources(
        StringBuilder sb,
        IEnumerable<(string Name, string Source)> unauthorizedSources
    )
    {
        foreach (var (name, source) in unauthorizedSources)
        {
            sb.AppendFormat(CultureInfo.InvariantCulture, "{0} ({1})", name, source);
            sb.AppendLine();
            sb.AppendLine("--");
        }
    }

    private static void UnknownPackages(
        StringBuilder sb,
        IDictionary<string, IEnumerable<string>> unknownPackages
    )
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

    private static void UnsupportedRangePackages(
        StringBuilder sb,
        IEnumerable<UnsupportedRangePackage> unsupportedRangePackages
    )
    {
        foreach (var (packageId, ranges) in unsupportedRangePackages)
        {
            sb.AppendLine(packageId);

            foreach (var (versionRange, projects) in ranges)
            {
                sb.AppendFormat(CultureInfo.InvariantCulture, "Version range: {0}", versionRange);
                sb.AppendLine();
                sb.AppendLine(
                    "A newer version may be available, but UpdatR doesn't know how to safely rewrite this version range - update it manually if needed."
                );
                sb.AppendLine("Used in:");

                foreach (var project in projects)
                {
                    sb.Append("- ").AppendLine(project);
                }
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

    private static void DeprecatedPackages(
        StringBuilder sb,
        IEnumerable<DeprecatedPackage> deprecatedPackages
    )
    {
        foreach (var (packageId, versions) in deprecatedPackages)
        {
            sb.AppendLine(packageId);

            var padding = versions
                .SelectMany(x => x.Projects.Select(y => y.Length))
                .DefaultIfEmpty(0)
                .Max();

            foreach (var ((version, metadata), projects) in versions)
            {
                sb.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "Reason(s): {0}",
                    string.Join(", ", metadata.Reasons)
                );

                sb.AppendLine();

                if (metadata.Message is not null)
                {
                    sb.AppendLine(metadata.Message.Replace("\n", Environment.NewLine));
                }

                if (metadata.AlternatePackage is not null)
                {
                    sb.AppendFormat(
                            CultureInfo.InvariantCulture,
                            "Alternate Package: {0}",
                            metadata.AlternatePackage.PackageId
                        )
                        .AppendLine();

                    sb.AppendFormat(
                            CultureInfo.InvariantCulture,
                            "Version range: {0}",
                            metadata.AlternatePackage.Range
                        )
                        .AppendLine();
                }

                sb.AppendLine("Package used in:");

                foreach (var project in projects)
                {
                    sb.AppendFormat(
                        CultureInfo.InvariantCulture,
                        "{0} {1}",
                        project.PadRight(padding),
                        version
                    );

                    sb.AppendLine();
                }
            }
            sb.AppendLine("--");
        }
    }

    private static void VulnerablePackages(
        StringBuilder sb,
        IEnumerable<VulnerablePackage> vulnerablePackages
    )
    {
        foreach (var package in vulnerablePackages)
        {
            sb.AppendLine(package.PackageId);

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
                    );
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

    private static void LicenseMismatchPackages(
        StringBuilder sb,
        IEnumerable<LicenseMismatchPackage> licenseMismatchPackages
    )
    {
        foreach (var package in licenseMismatchPackages)
        {
            sb.AppendLine(package.PackageId);

            foreach (var (version, projects) in package.Versions)
            {
                sb.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "{0} {1}: {2}",
                    version.IsInstalledVersion ? "Installed version" : "Newer version available",
                    version.NuGetVersion,
                    version.License
                );

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

    private static void SkippedUpdatePackages(
        StringBuilder sb,
        IEnumerable<SkippedUpdatePackage> skippedUpdatePackages
    )
    {
        foreach (var package in skippedUpdatePackages)
        {
            sb.AppendLine(package.PackageId);

            foreach (var (version, projects) in package.Versions)
            {
                sb.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "Newer version available: {0} ({1})",
                    version.NuGetVersion,
                    version.Reason
                );

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

            sb.AppendLine(packages.PackageId);

            foreach (var (from, to, project) in packages.Updates)
            {
                sb.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "{0} {1} => {2}",
                    project.PadRight(padRightProject),
                    from.ToString().PadRight(padRightFrom),
                    to
                );

                sb.AppendLine();
            }
            sb.AppendLine("--");
        }
    }
}
