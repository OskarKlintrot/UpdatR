namespace UpdatR;

/// <summary>
/// Declares that a dotnet tool in a <c>dotnet-tools.json</c> manifest (e.g. <c>dotnet-ef</c>)
/// must never be updated past the highest version compatible with the package it drives - matched
/// by <paramref name="PinnedPackageIdPattern"/> - in every project the manifest applies to. E.g.
/// the built-in <see cref="EntityFrameworkCore"/> default keeps <c>dotnet-ef</c> in step with
/// each affected project's <c>Microsoft.EntityFrameworkCore(.*)</c> package version, so the tool
/// and the packages it drives (migrations, scaffolding, etc.) never mismatch.
/// </summary>
/// <param name="ToolPackageId">The dotnet tool's package id, e.g. <c>dotnet-ef</c>.</param>
/// <param name="PinnedPackageIdPattern">
/// Package id pattern (case-insensitive) matched against package ids referenced by an affected
/// project to find the package this tool is pinned to - the same <c>*</c>-wildcard matching used
/// for e.g. <c>alignWithTfm</c>, matched against the whole package id. Use e.g.
/// <c>Microsoft.EntityFrameworkCore*</c> to also match <c>Microsoft.EntityFrameworkCore.Sqlite</c>
/// and other packages in the same family - an exact id with no <c>*</c>, e.g. <c>xunit</c>, only
/// matches that one package id, not e.g. <c>xunit.core</c>. If the pattern matches more than one
/// referenced package, they must all be pinned to the same version, or a
/// <see cref="AmbiguousToolPackagePinException"/> is thrown - e.g. a too-broad pattern like
/// <c>xunit*</c> would match both <c>xunit.v3</c> and an unrelated, differently-versioned
/// <c>xunit.runner.visualstudio</c>.
/// </param>
public sealed record ToolPackagePin(string ToolPackageId, string PinnedPackageIdPattern)
{
    /// <summary>
    /// The built-in default pin rule: keeps <c>dotnet-ef</c> from moving ahead of the highest
    /// <c>Microsoft.EntityFrameworkCore(.*)</c> version any affected project can still resolve.
    /// </summary>
    public static readonly ToolPackagePin EntityFrameworkCore = new(
        "dotnet-ef",
        "Microsoft.EntityFrameworkCore*"
    );
}
