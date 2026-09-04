namespace UpdatR;

/// <summary>
/// Options for <see cref="Updater.UpdateAsync"/>. Every member is optional - leaving them all at
/// their default updates every package to the latest stable version compatible with each
/// project's target framework(s), without any exclusion or restriction.
/// </summary>
public sealed record UpdateOptions
{
    /// <summary>Packages to exclude. Supports * as wildcard.</summary>
    public string[]? ExcludePackages { get; init; }

    /// <summary>
    /// Packages to update. Supports * as wildcard. If <see langword="null"/> or empty then all
    /// packages, except <see cref="ExcludePackages"/>, will be updated.
    /// </summary>
    public string[]? Packages { get; init; }

    /// <summary>Do not save any changes.</summary>
    public bool DryRun { get; init; }

    /// <summary>Allow prerelease packages to be installed.</summary>
    public bool Prerelease { get; init; }

    /// <summary>Interaction with user is possible.</summary>
    public bool Interactive { get; init; }

    /// <summary>
    /// Bypass the local NuGet HTTP cache when checking for package versions, forcing every
    /// source to be queried fresh. Leave <see langword="false"/> (default) to reuse cached
    /// responses, which is faster and avoids unnecessary load on package sources.
    /// </summary>
    public bool NoCache { get; init; }

    /// <summary>Lowest Target Framework Moniker to support.</summary>
    public string? TargetFrameworkMoniker { get; init; }

    /// <summary>
    /// If specified, a package is only updated to a version whose license expression contains one
    /// of these values (case-insensitive substring match). A warning is logged - and included in
    /// the <see cref="Summary"/> - both when the currently installed version's license isn't
    /// allowed, and when a newer version exists but was skipped because its license isn't
    /// allowed. Packages without any license metadata are always allowed. Leave out or empty to
    /// disable license checking.
    /// </summary>
    public string[]? AllowedLicenses { get; init; }

    /// <summary>
    /// Csproj-, dotnet-tools.json-, props/targets- and file-based app files to exclude from being
    /// processed altogether, matched against each file's path relative to the resolved
    /// <c>path</c>. Supports * as wildcard.
    /// </summary>
    public string[]? ExcludeFiles { get; init; }

    /// <summary>
    /// Packages to keep aligned with a project's target framework's major version, instead of
    /// updating to a newer version whose major just happens to also be compatible (e.g. a package
    /// that multi-targets both <c>net9.0</c> and <c>net10.0</c> in the same, higher-major,
    /// release). Supports * as wildcard. Only applies to modern (<c>net5.0</c>+) target
    /// frameworks, and only if the currently installed version's major isn't already ahead of the
    /// target framework's - if it is, updates are left unrestricted. Also applies to
    /// <c>dotnet-tools.json</c> entries, aligned with the target framework(s) of the csproj(s)
    /// the manifest applies to (e.g. keeping <c>dotnet-ef</c> in step with
    /// <c>Microsoft.EntityFrameworkCore</c>).
    /// </summary>
    public string[]? AlignWithTfm { get; init; }

    /// <summary>
    /// Rules pinning a dotnet tool (e.g. <c>dotnet-ef</c>) to the highest version of a package
    /// (matched by the same <c>*</c>-wildcard pattern used elsewhere, e.g.
    /// <c>Microsoft.EntityFrameworkCore*</c>) referenced by the project(s) a
    /// <c>dotnet-tools.json</c> manifest applies to, so the tool is never updated past what those
    /// projects support. <see cref="ToolPackagePin.EntityFrameworkCore"/> is always applied as a
    /// built-in default unless overridden - by <c>.updatrrc</c>'s <c>toolPackagePins</c>, or by an
    /// entry here - for the same tool.
    /// </summary>
    public IReadOnlyCollection<ToolPackagePin>? ToolPackagePins { get; init; }

    /// <summary>
    /// Per-package (or wildcard-matched) fixed major-version caps - see
    /// <see cref="PackageVersionPolicy"/>. Merged with <c>.updatrrc</c>'s <c>packagePolicies</c>
    /// (this collection first, so an entry here takes priority when more than one pattern matches
    /// the same package id).
    /// </summary>
    public IReadOnlyCollection<PackageVersionPolicy>? PackagePolicies { get; init; }

    /// <summary>
    /// Minimum severity of finding that should make <see cref="Summary.ShouldFail"/> true. Leave
    /// out (or <see langword="null"/>) to fall back to <c>.updatrrc</c>'s <c>failOn</c>, or
    /// <see cref="UpdatR.FailOn.None"/> if that's also not set.
    /// </summary>
    public FailOn? FailOn { get; init; }

    /// <summary>
    /// Also make <see cref="Summary.ShouldFail"/> true when the run was incomplete - i.e. it hit
    /// an unauthorized package source, or couldn't resolve a package on any source - so CI can
    /// distinguish "nothing to report" from "I couldn't check". Independent of
    /// <see cref="FailOn"/>. Leave out (or <see langword="null"/>) to fall back to
    /// <c>.updatrrc</c>'s <c>failOnIncomplete</c>, or <see langword="false"/> if that's also not
    /// set.
    /// </summary>
    public bool? FailOnIncomplete { get; init; }
}
