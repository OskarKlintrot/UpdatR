using Microsoft.Extensions.Logging;
using NuGet.Frameworks;
using NuGet.Versioning;
using UpdatR.Domain.Utils;
using UpdatR.Internals;

namespace UpdatR.Domain;

/// <summary>
/// Shared package-update algorithm for the file types UpdatR can update - <see cref="Csproj"/>,
/// <see cref="PropsFile"/> and <see cref="FileBasedApp"/>. Handles resolving floating
/// versions/ranges, checking for deprecated/vulnerable packages and license mismatches, and
/// deciding whether/how to rewrite a version string, so each of those three only needs to supply
/// how its package references are found (<see cref="EnumerateCandidates"/>), how a resolved
/// version is written back in memory (<see cref="ApplyVersionUpdate"/>), and how changes are
/// persisted to disk (<see cref="PersistAsync"/>).
/// </summary>
internal abstract partial class PackageContainer
{
    public abstract string Name { get; }

    public abstract string Path { get; }

    /// <summary>
    /// A single package reference/directive with a version to evaluate, found by
    /// <see cref="EnumerateCandidates"/>. Subclasses derive from this to attach whatever extra
    /// state they need in order to rewrite it via <see cref="ApplyVersionUpdate"/>.
    /// </summary>
    protected abstract class Candidate
    {
        public required string PackageId { get; init; }

        /// <summary>The version string as written, e.g. "1.2.3", "4.8.*" or "[1.0,2.0)".</summary>
        public required string VersionString { get; init; }

        /// <summary>
        /// The original source text this candidate was found in, used in log messages - e.g. the
        /// PackageReference's XML, or the <c>#:package</c> directive.
        /// </summary>
        public required string SiteText { get; init; }

        /// <summary>
        /// The specific target framework(s) this candidate is known to apply to - e.g. a
        /// <c>PackageReference</c> inside an
        /// <c>ItemGroup Condition="'$(TargetFramework)'=='net6.0'"</c> in a multi-targeted
        /// project or a shared props/targets file. Resolved via a real per-framework MSBuild
        /// evaluation (see <see cref="Internals.MsBuildProjectInspector.GetPackageItemSourcesByTfm"/>),
        /// so it reflects exactly what MSBuild itself would build with. <see langword="null"/> if
        /// not known more precisely than the container's overall target framework(s) - the common
        /// case for an unconditioned reference, or when a more precise resolution wasn't
        /// attempted/possible (e.g. a single-targeted project, or failed MSBuild evaluation) - in
        /// which case <see cref="ResolveTfms"/>'s result is used instead.
        /// </summary>
        public IReadOnlyCollection<NuGetFramework>? ApplicableTfms { get; init; }
    }

    /// <summary>
    /// The noun used to describe a single candidate in log messages, e.g. "package reference" or
    /// "package directive".
    /// </summary>
    protected abstract string ReferenceKind { get; }

    /// <summary>
    /// If <see langword="true"/>, a project with only <see cref="ProjectWithPackages.UnknownPackages"/>
    /// (i.e. nothing else to report) is still included in the update result.
    /// </summary>
    protected virtual bool IncludeUnknownOnlyProjects => false;

    /// <summary>
    /// Finds every package reference/directive with a version to evaluate. References without a
    /// version (nothing to update, e.g. a bare <c>#:package Foo</c> directive, or a
    /// PackageReference that only uses <c>Update</c> to override metadata) should not be yielded
    /// at all.
    /// </summary>
    protected abstract IEnumerable<Candidate> EnumerateCandidates();

    /// <summary>
    /// Resolves the target framework(s) an update must be compatible with, honoring
    /// <paramref name="tfmOverride"/> when supplied.
    /// </summary>
    protected abstract IReadOnlyCollection<NuGetFramework> ResolveTfms(NuGetFramework? tfmOverride);

    /// <summary>
    /// Writes <paramref name="newVersionString"/> back for <paramref name="candidate"/> in
    /// memory. Persisting to disk happens separately, via <see cref="PersistAsync"/>.
    /// </summary>
    protected abstract void ApplyVersionUpdate(Candidate candidate, string newVersionString);

    /// <summary>
    /// Persists changes made via <see cref="ApplyVersionUpdate"/> to disk, unless
    /// <paramref name="dryRun"/> is <see langword="true"/>. Only called if at least one candidate
    /// was actually updated.
    /// </summary>
    protected abstract Task PersistAsync(bool dryRun);

    /// <summary>
    /// Called once, after <see cref="ApplyVersionUpdate"/> has been called at least once -
    /// regardless of <c>dryRun</c>, since in-memory state has already changed either way. Does
    /// nothing by default.
    /// </summary>
    protected virtual void OnChangesApplied() { }

    /// <summary>
    /// Called when <paramref name="candidate"/>'s version string can't be parsed as either an
    /// exact version or a version range at all. Does nothing by default.
    /// </summary>
    protected virtual void OnUnparseableVersion(Candidate candidate, ILogger logger) { }

    /// <summary>
    /// Lets a subclass override the version this algorithm would otherwise resolve
    /// <paramref name="candidate"/> to, e.g. so <see cref="DotnetTools"/> can pin
    /// <c>dotnet-ef</c> to the highest version still compatible with every referenced project's
    /// <c>EntityFrameworkVersion</c>. Called only once an update target has already been found -
    /// not for a candidate that's already up to date. Returns <paramref name="updateTo"/>
    /// unchanged by default.
    /// </summary>
    protected virtual PackageMetadata AdjustUpdateTarget(
        Candidate candidate,
        NuGetPackage package,
        NuGetVersion currentVersion,
        PackageMetadata updateTo
    ) => updateTo;

    /// <summary>
    /// Resolves the target framework(s) used to compute the current major version an
    /// <c>alignWithTfm</c> update must not move past. Given <paramref name="candidateTfms"/> (the
    /// same set <see cref="ResolveTfms"/> and compatibility checks use) by default; overridden by
    /// <see cref="DotnetTools"/>, which - unlike <see cref="Csproj"/>/<see cref="PropsFile"/>/
    /// <see cref="FileBasedApp"/> - doesn't itself target a framework, so it aligns against every
    /// affected project's target framework(s) instead.
    /// </summary>
    protected virtual IReadOnlyCollection<NuGetFramework> ResolveAlignmentTfms(
        IReadOnlyCollection<NuGetFramework> candidateTfms
    ) => candidateTfms;

    protected async Task<ProjectWithPackages?> UpdatePackagesCoreAsync(
        IDictionary<string, NuGetPackage?> packages,
        bool dryRun,
        bool usePrerelease,
        ILogger logger,
        NuGetFramework? tfm,
        IReadOnlyCollection<string>? allowedLicenses,
        IReadOnlyCollection<string>? alignWithTfm = null,
        IReadOnlyCollection<PackageVersionPolicy>? packagePolicies = null
    )
    {
        var tfms = ResolveTfms(tfm);

        var shouldAlignWithTfm = SearchPattern.CreateSearch(
            alignWithTfm,
            treatNullOrEmptyAs: false
        );

        var project = new ProjectWithPackages(Path);

        var changed = false;

        foreach (var candidate in EnumerateCandidates())
        {
            var packageId = candidate.PackageId;
            var versionStr = candidate.VersionString;

            // An explicit tfmOverride always wins (same as ResolveTfms itself). Otherwise, a
            // candidate resolved to a more precise subset of the container's target
            // framework(s) - e.g. via a Condition on $(TargetFramework) - is only checked for
            // compatibility against that subset, so the same package can be updated to a
            // different version for each subset independently.
            var candidateTfms =
                tfm is null && candidate.ApplicableTfms is { Count: > 0 } specificTfms
                    ? specificTfms
                    : tfms;

            VersionRange? versionRange = null;
            NuGetVersion? version;

            if (NuGetVersion.TryParse(versionStr, out var parsedVersion))
            {
                version = parsedVersion;
            }
            else if (VersionRange.TryParse(versionStr, out versionRange))
            {
                // A floating version, e.g. "4.8.*", or a version range, e.g. "[1.0,2.0)". The
                // concrete version is resolved further down, once the package's available
                // versions are known.
                version = null;
            }
            else
            {
                OnUnparseableVersion(candidate, logger);

                continue;
            }

            if (!packages.TryGetValue(packageId, out var package))
            {
                LogMissingPackage(logger, packageId);

                project.AddUnknownPackage(packageId);

                continue;
            }
            else if (package is null)
            {
                // Ignore package

                continue;
            }

            if (versionRange is not null)
            {
                // Resolve the version NuGet would currently pick for the floating version/range,
                // so it can be used the same way an exact version would be below.
                var resolved = package
                    .PackageMetadatas.Where(x => Satisfies(versionRange, x.Version))
                    .OrderByDescending(x => x.Version)
                    .FirstOrDefault();

                if (resolved is null)
                {
                    LogFloatingVersionSkipped(
                        logger,
                        versionStr,
                        ReferenceKind,
                        candidate.SiteText
                    );

                    continue;
                }

                version = resolved.Version;

                CheckForDeprecationAndVulnerabilities(logger, project, packageId, resolved);
                CheckForLicenseMismatch(
                    logger,
                    project,
                    packageId,
                    version,
                    resolved,
                    allowedLicenses
                );
            }
            else if (package.TryGet(version!, out var metadata))
            {
                CheckForDeprecationAndVulnerabilities(logger, project, packageId, metadata);
                CheckForLicenseMismatch(
                    logger,
                    project,
                    packageId,
                    version!,
                    metadata,
                    allowedLicenses
                );
            }

            var alignMajor = TfmAlignment.ResolveAlignMajor(ResolveAlignmentTfms(candidateTfms));

            var alignWithTfmMaxMajor = shouldAlignWithTfm(packageId)
                ? TfmAlignment.ResolveMaxMajor(alignMajor, version!)
                : null;

            var policyMaxMajor = ResolvePackagePolicyMaxMajor(packagePolicies, packageId);

            var maxMajor = CombineMaxMajor(alignWithTfmMaxMajor, policyMaxMajor);

            if (
                !TargetFrameworkCompatibility.TryGetLatestCompatibleWithAllTfms(
                    package,
                    version!,
                    candidateTfms,
                    usePrerelease,
                    allowedLicenses,
                    out var updateTo,
                    maxMajor
                )
            )
            {
                if (versionRange is not null)
                {
                    // Nothing newer than what the floating version/range already resolves to.
                    LogFloatingVersionSkipped(
                        logger,
                        versionStr,
                        ReferenceKind,
                        candidate.SiteText
                    );

                    continue;
                }

                CheckForDeprecationAndVulnerabilities(
                    logger,
                    project,
                    packageId,
                    package.PackageMetadatas.FirstOrDefault(x => x.Version == version)
                );

                CheckForSkippedLicenseMismatch(
                    logger,
                    project,
                    package,
                    packageId,
                    version!,
                    candidateTfms,
                    usePrerelease,
                    allowedLicenses,
                    maxMajor
                );

                CheckForSkippedUpdate(
                    logger,
                    project,
                    package,
                    packageId,
                    version!,
                    usePrerelease,
                    allowedLicenses,
                    alignWithTfmMaxMajor,
                    policyMaxMajor
                );

                continue;
            }

            updateTo = AdjustUpdateTarget(candidate, package, version!, updateTo);

            CheckForSkippedUpdate(
                logger,
                project,
                package,
                packageId,
                updateTo.Version,
                usePrerelease,
                allowedLicenses,
                alignWithTfmMaxMajor,
                policyMaxMajor
            );

            if (updateTo.Version <= version!)
            {
                // AdjustUpdateTarget capped the target back to - or below - the installed
                // version, e.g. a dotnet tool that's already in step with the package it's
                // pinned to. Applying it would needlessly rewrite the file, report a bogus
                // "updated X from V to V" (tripping --fail-on outdated), and in the "tool is
                // ahead of its pin" case actually downgrade it.
                continue;
            }

            if (versionRange is not null)
            {
                if (Satisfies(versionRange, updateTo.Version))
                {
                    // The newer version is already covered by the existing floating version/range
                    // - NuGet already resolves to it automatically, nothing to write.
                    continue;
                }

                var newVersionStr = BuildFloatingVersionString(versionRange, updateTo.Version);

                if (newVersionStr is null)
                {
                    // Don't know how to safely rewrite this kind of floating version/range (e.g.
                    // a prerelease float, or a fixed range like "[1.0,2.0)") - leave it as-is,
                    // but call it out clearly since a newer version is available.
                    LogUnsupportedVersionRange(
                        logger,
                        versionStr,
                        ReferenceKind,
                        candidate.SiteText
                    );

                    project.AddUnsupportedRangePackage(new(packageId, versionStr));

                    continue;
                }

                ApplyVersionUpdate(candidate, newVersionStr);
                changed = true;

                LogUpdateSuccessful(logger, Name, packageId, version!, updateTo.Version);

                project.AddUpdatedPackage(new(packageId, version!, updateTo.Version));

                CheckForDeprecationAndVulnerabilities(logger, project, packageId, updateTo);

                continue;
            }

            ApplyVersionUpdate(candidate, updateTo.Version.ToString());
            changed = true;

            LogUpdateSuccessful(logger, Name, packageId, version!, updateTo.Version);

            project.AddUpdatedPackage(new(packageId, version!, updateTo.Version));

            CheckForDeprecationAndVulnerabilities(logger, project, packageId, updateTo);
        }

        if (changed)
        {
            await PersistAsync(dryRun).ConfigureAwait(false);

            OnChangesApplied();
        }

        return
            project.AnyPackages() || (IncludeUnknownOnlyProjects && project.UnknownPackages.Any())
            ? project
            : null;
    }

    private static void CheckForDeprecationAndVulnerabilities(
        ILogger logger,
        ProjectWithPackages project,
        string packageId,
        PackageMetadata? packageMetadata
    )
    {
        if (packageMetadata is null)
        {
            return;
        }

        if (packageMetadata.DeprecationMetadata is not null)
        {
            project.AddDeprecatedPackage(
                new(packageId, packageMetadata.Version, packageMetadata.DeprecationMetadata)
            );

            LogDeprecatedPackage(
                logger,
                packageId,
                packageMetadata.Version,
                string.Join(", ", packageMetadata.DeprecationMetadata.Reasons)
            );
        }

        if (packageMetadata.Vulnerabilities?.Any() == true)
        {
            project.AddVulnerablePackage(
                new(packageId, packageMetadata.Version, packageMetadata.Vulnerabilities)
            );

            LogVulnerablePackage(
                logger,
                packageId,
                packageMetadata.Version,
                packageMetadata.Vulnerabilities.Count()
            );
        }
    }

    private static void CheckForLicenseMismatch(
        ILogger logger,
        ProjectWithPackages project,
        string packageId,
        NuGetVersion version,
        PackageMetadata packageMetadata,
        IReadOnlyCollection<string>? allowedLicenses
    )
    {
        if (
            allowedLicenses is not { Count: > 0 }
            || NuGetPackage.IsLicenseAllowed(packageMetadata, allowedLicenses)
        )
        {
            return;
        }

        project.AddLicenseMismatchPackage(
            new(packageId, version, packageMetadata.LicenseExpression!, isInstalledVersion: true)
        );

        LogLicenseMismatch(logger, packageId, version, packageMetadata.LicenseExpression!);
    }

    private static void CheckForSkippedLicenseMismatch(
        ILogger logger,
        ProjectWithPackages project,
        NuGetPackage package,
        string packageId,
        NuGetVersion version,
        IReadOnlyCollection<NuGetFramework> tfms,
        bool usePrerelease,
        IReadOnlyCollection<string>? allowedLicenses,
        int? maxMajor = null
    )
    {
        if (
            allowedLicenses is not { Count: > 0 }
            || !TargetFrameworkCompatibility.TryGetLatestCompatibleWithAllTfms(
                package,
                version,
                tfms,
                usePrerelease,
                allowedLicenses: null,
                out var skipped,
                maxMajor
            )
            || NuGetPackage.IsLicenseAllowed(skipped, allowedLicenses)
        )
        {
            return;
        }

        project.AddLicenseMismatchPackage(
            new(packageId, skipped.Version, skipped.LicenseExpression!, isInstalledVersion: false)
        );

        LogSkippedLicenseMismatch(logger, packageId, skipped.Version, skipped.LicenseExpression!);
    }

    /// <summary>
    /// Reports an update that was skipped for a reason other than a license mismatch (see
    /// <see cref="CheckForSkippedLicenseMismatch"/>) or an unsupported version range/floating
    /// version - i.e. a newer version was capped by <c>alignWithTfm</c> or a matching
    /// <see cref="PackageVersionPolicy"/>, or is incompatible with one of the project's target
    /// framework(s). Compares <paramref name="version"/> (the currently installed version, or the
    /// version an update was already applied to) against the absolute latest version available,
    /// ignoring target framework compatibility entirely - if that's newer, whatever's left
    /// blocking it (<paramref name="policyMaxMajor"/> or <paramref name="alignWithTfmMaxMajor"/>,
    /// whichever's major is what's in the way, or else target framework compatibility) is
    /// reported.
    /// </summary>
    private static void CheckForSkippedUpdate(
        ILogger logger,
        ProjectWithPackages project,
        NuGetPackage package,
        string packageId,
        NuGetVersion version,
        bool usePrerelease,
        IReadOnlyCollection<string>? allowedLicenses,
        int? alignWithTfmMaxMajor,
        int? policyMaxMajor
    )
    {
        if (
            !TargetFrameworkCompatibility.TryGetLatestIgnoringTfmCompatibility(
                package,
                version,
                usePrerelease,
                allowedLicenses,
                maxMajor: null,
                out var latest
            )
        )
        {
            return;
        }

        var reason =
            policyMaxMajor is not null && latest.Version.Major > policyMaxMajor
                ? SkippedUpdateReason.PackageVersionPolicy
            : alignWithTfmMaxMajor is not null && latest.Version.Major > alignWithTfmMaxMajor
                ? SkippedUpdateReason.AlignedWithTfm
            : SkippedUpdateReason.IncompatibleTargetFramework;

        project.AddSkippedUpdatePackage(new(packageId, latest.Version, reason));

        LogSkippedUpdate(logger, packageId, latest.Version, reason);
    }

    /// <summary>
    /// Finds the first <see cref="PackageVersionPolicy"/> (in order) whose
    /// <see cref="PackageVersionPolicy.PackageIdPattern"/> matches <paramref name="packageId"/>,
    /// and returns its <see cref="PackageVersionPolicy.MaxMajor"/>, or <see langword="null"/> if
    /// none match.
    /// </summary>
    private static int? ResolvePackagePolicyMaxMajor(
        IReadOnlyCollection<PackageVersionPolicy>? packagePolicies,
        string packageId
    )
    {
        if (packagePolicies is null || packagePolicies.Count == 0)
        {
            return null;
        }

        foreach (var policy in packagePolicies)
        {
            if (SearchPattern.ConvertToRegex(policy.PackageIdPattern).IsMatch(packageId))
            {
                return policy.MaxMajor;
            }
        }

        return null;
    }

    /// <summary>
    /// Combines an <c>alignWithTfm</c>-derived cap with a <see cref="PackageVersionPolicy"/>
    /// cap - both may apply to the same package - by taking the more restrictive (lower) of the
    /// two, non-null values.
    /// </summary>
    private static int? CombineMaxMajor(int? alignWithTfmMaxMajor, int? policyMaxMajor)
    {
        if (alignWithTfmMaxMajor is null)
        {
            return policyMaxMajor;
        }

        if (policyMaxMajor is null)
        {
            return alignWithTfmMaxMajor;
        }

        return Math.Min(alignWithTfmMaxMajor.Value, policyMaxMajor.Value);
    }

    /// <summary>
    /// Resolves a <see cref="NuGetVersion"/> that represents <paramref name="versionStr"/> well
    /// enough to be used as a lookup key, i.e. to ensure the package is queried for on NuGet even
    /// though it can't be parsed as an exact version. Returns the lower bound of the version
    /// range for a floating version (e.g. "4.8.*") or a version range (e.g. "[1.0,2.0)"), or
    /// <see langword="null"/> if <paramref name="versionStr"/> can't be parsed at all. Used by
    /// <see cref="EnumerateCandidates"/> implementations to build the lookup dictionary key for
    /// floating versions/ranges the same way as for plain versions.
    /// </summary>
    protected static NuGetVersion? ResolveRepresentativeVersion(string versionStr) =>
        NuGetVersion.TryParse(versionStr, out var version) ? version
        : VersionRange.TryParse(versionStr, out var versionRange)
            ? versionRange.MinVersion ?? versionRange.Float?.MinVersion
        : null;

    /// <summary>
    /// Checks if <paramref name="version"/> matches <paramref name="range"/>, respecting its
    /// floating segment (if any). <see cref="VersionRange.Satisfies(NuGetVersion)"/> alone
    /// ignores the floating segment, e.g. it considers "2.0.0" to satisfy "1.*" even though "1.*"
    /// is restricted to the 1.x series.
    /// </summary>
    private static bool Satisfies(VersionRange range, NuGetVersion version) =>
        range.Float?.Satisfies(version) ?? range.Satisfies(version);

    /// <summary>
    /// Builds a new floating version string with the same floating segment as
    /// <paramref name="range"/>, but with its fixed prefix updated to match
    /// <paramref name="newVersion"/> - e.g. bumps "4.8.*" to "4.9.*" if <paramref name="newVersion"/>
    /// is "4.9.2". Returns <see langword="null"/> for floating segments that aren't safe to
    /// rewrite this way (prerelease floats), or for a plain version range without a floating
    /// segment (e.g. "[1.0,2.0)"), since rewriting those could silently change their meaning.
    /// </summary>
    private static string? BuildFloatingVersionString(
        VersionRange range,
        NuGetVersion newVersion
    ) =>
        range.Float?.FloatBehavior switch
        {
            NuGetVersionFloatBehavior.Major => "*",
            NuGetVersionFloatBehavior.Minor => $"{newVersion.Major}.*",
            NuGetVersionFloatBehavior.Patch => $"{newVersion.Major}.{newVersion.Minor}.*",
            NuGetVersionFloatBehavior.Revision =>
                $"{newVersion.Major}.{newVersion.Minor}.{newVersion.Patch}.*",
            _ => null,
        };

    #region LogMessages
    [LoggerMessage(
        Level = LogLevel.Warning,
        EventId = 1,
        Message = "Could not parse {Version} to NuGetVersion for {Kind} {Site}."
    )]
    protected static partial void LogParseError(
        ILogger logger,
        string version,
        string kind,
        string site
    );

    [LoggerMessage(Level = LogLevel.Warning, EventId = 2, Message = "Could not find {PackageId}.")]
    private static partial void LogMissingPackage(ILogger logger, string packageId);

    [LoggerMessage(
        Level = LogLevel.Debug,
        EventId = 3,
        Message = "Skipping automatic update of floating version {Version} for {Kind} {Site} since NuGet already resolves it to the latest matching version."
    )]
    private static partial void LogFloatingVersionSkipped(
        ILogger logger,
        string version,
        string kind,
        string site
    );

    [LoggerMessage(
        Level = LogLevel.Warning,
        EventId = 4,
        Message = "Could not automatically update version range {VersionRange} for {Kind} {Site} - UpdatR doesn't know how to safely rewrite this kind of version range (e.g. a fixed range like \"[1.0,2.0)\", or a prerelease float). A newer version may be available; update it manually if needed."
    )]
    private static partial void LogUnsupportedVersionRange(
        ILogger logger,
        string versionRange,
        string kind,
        string site
    );

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 5,
        Message = "{Name}: Updated {PackageId} from {FromVersion} to {ToVersion}"
    )]
    private static partial void LogUpdateSuccessful(
        ILogger logger,
        string name,
        string packageId,
        NuGetVersion fromVersion,
        NuGetVersion toVersion
    );

    [LoggerMessage(
        Level = LogLevel.Warning,
        EventId = 6,
        Message = "Package {PackageId} version {Version} is deprecated with reasons: {Reasons}"
    )]
    private static partial void LogDeprecatedPackage(
        ILogger logger,
        string packageId,
        NuGetVersion version,
        string reasons
    );

    [LoggerMessage(
        Level = LogLevel.Warning,
        EventId = 7,
        Message = "Package {PackageId} version {Version} has {Vulnerabilities} vulnerabilities"
    )]
    private static partial void LogVulnerablePackage(
        ILogger logger,
        string packageId,
        NuGetVersion version,
        int vulnerabilities
    );

    [LoggerMessage(
        Level = LogLevel.Warning,
        EventId = 8,
        Message = "Package {PackageId} version {Version} has a license that isn't allowed: {License}"
    )]
    private static partial void LogLicenseMismatch(
        ILogger logger,
        string packageId,
        NuGetVersion version,
        string license
    );

    [LoggerMessage(
        Level = LogLevel.Warning,
        EventId = 9,
        Message = "Package {PackageId} has a newer version {Version} available, but it was skipped because its license isn't allowed: {License}"
    )]
    private static partial void LogSkippedLicenseMismatch(
        ILogger logger,
        string packageId,
        NuGetVersion version,
        string license
    );

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 10,
        Message = "Package {PackageId} has a newer version {Version} available, but it was skipped: {Reason}"
    )]
    private static partial void LogSkippedUpdate(
        ILogger logger,
        string packageId,
        NuGetVersion version,
        SkippedUpdateReason reason
    );
    #endregion
}
