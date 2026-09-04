namespace UpdatR;

/// <summary>
/// Declares that a dotnet tool in a <c>dotnet-tools.json</c> manifest (e.g. <c>dotnet-ef</c>)
/// must never be updated past the highest version compatible with the package it drives - matched
/// by <paramref name="PinnedPackageIdPrefix"/> - in every project the manifest applies to. E.g.
/// the built-in <see cref="EntityFrameworkCore"/> default keeps <c>dotnet-ef</c> in step with
/// each affected project's <c>Microsoft.EntityFrameworkCore(.*)</c> package version, so the tool
/// and the packages it drives (migrations, scaffolding, etc.) never mismatch.
/// </summary>
/// <param name="ToolPackageId">The dotnet tool's package id, e.g. <c>dotnet-ef</c>.</param>
/// <param name="PinnedPackageIdPrefix">
/// Prefix (case-insensitive) matched against package ids referenced by an affected project to
/// find the package this tool is pinned to, e.g. <c>Microsoft.EntityFrameworkCore</c> (which also
/// matches e.g. <c>Microsoft.EntityFrameworkCore.Sqlite</c>).
/// </param>
public sealed record ToolPackagePin(string ToolPackageId, string PinnedPackageIdPrefix)
{
    /// <summary>
    /// The built-in default pin rule: keeps <c>dotnet-ef</c> from moving ahead of the highest
    /// <c>Microsoft.EntityFrameworkCore(.*)</c> version any affected project can still resolve.
    /// </summary>
    public static readonly ToolPackagePin EntityFrameworkCore = new(
        "dotnet-ef",
        "Microsoft.EntityFrameworkCore"
    );
}
