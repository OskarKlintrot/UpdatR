using System.Runtime.CompilerServices;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Locator;

namespace UpdatR.Internals;

/// <summary>
/// Which physical file a <c>PackageReference</c>, <c>PackageVersion</c> or
/// <c>GlobalPackageReference</c> item was declared in. <see cref="SourceFile"/> is the project or
/// the imported <c>Directory.Build.props</c> / <c>Directory.Packages.props</c> (or any other
/// props/targets file) that actually contains the item, no matter how deep the import chain is.
/// </summary>
internal sealed record PackageItemSource(
    string ItemType,
    string PackageId,
    string? Version,
    string SourceFile
);

/// <summary>
/// Inspects a project using the real MSBuild evaluation engine instead of hand-parsing
/// <c>Directory.Build.props</c>/<c>Directory.Packages.props</c> import chains. This correctly
/// handles arbitrarily deep/nested imports and SDK-injected imports (such as
/// <c>Directory.Packages.props</c> for Central Package Management) because MSBuild itself
/// resolves them during evaluation.
/// </summary>
internal static class MsBuildProjectInspector
{
    private static readonly Lock RegistrationLock = new();
    private static bool _registered;

    /// <summary>
    /// Evaluates <paramref name="projectPath"/> and returns every evaluated
    /// <c>PackageReference</c>, <c>PackageVersion</c> and <c>GlobalPackageReference</c> item
    /// together with the file it was declared in.
    /// </summary>
    public static IReadOnlyList<PackageItemSource> GetPackageItemSources(string projectPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);

        EnsureMsBuildLocatorIsRegistered();

        // Calling into a separate, non-inlined method is required: this method must not
        // reference any Microsoft.Build.* type directly, or the JIT will try to load those
        // assemblies while compiling *this* method - i.e. before EnsureMsBuildLocatorIsRegistered
        // above has had a chance to run, and MSBuildLocator's assembly resolver hasn't been
        // registered yet at that point.
        return GetPackageItemSourcesCore(projectPath);
    }

    /// <summary>
    /// Returns every props/targets file imported by <paramref name="projectPath"/>, in the order
    /// MSBuild imported them. This includes SDK-injected imports such as
    /// <c>Directory.Build.props</c> and, when Central Package Management is enabled,
    /// <c>Directory.Packages.props</c>.
    /// </summary>
    public static IReadOnlyList<string> GetImportedFiles(string projectPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);

        EnsureMsBuildLocatorIsRegistered();

        // See comment in GetPackageItemSources for why this must be a separate, non-inlined
        // method.
        return GetImportedFilesCore(projectPath);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static IReadOnlyList<PackageItemSource> GetPackageItemSourcesCore(string projectPath)
    {
        using var collection = new ProjectCollection();

        try
        {
            var project = collection.LoadProject(projectPath);

            return
            [
                .. project
                    .AllEvaluatedItems.Where(item =>
                        item.ItemType
                            is "PackageReference"
                                or "PackageVersion"
                                or "GlobalPackageReference"
                    )
                    .Select(item => new PackageItemSource(
                        item.ItemType,
                        item.EvaluatedInclude,
                        GetVersionMetadata(item),
                        item.Xml!.ContainingProject.FullPath
                    )),
            ];
        }
        finally
        {
            collection.UnloadAllProjects();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static IReadOnlyList<string> GetImportedFilesCore(string projectPath)
    {
        using var collection = new ProjectCollection();

        try
        {
            var project = collection.LoadProject(projectPath);

            return
            [
                .. project.Imports.Select(import => import.ImportedProject.FullPath).Distinct(),
            ];
        }
        finally
        {
            collection.UnloadAllProjects();
        }
    }

    private static string? GetVersionMetadata(ProjectItem item)
    {
        var version = item.GetMetadataValue("Version");

        return string.IsNullOrEmpty(version) ? null : version;
    }

    /// <summary>
    /// Registers MSBuildLocator's assembly resolver, if it hasn't already been registered. Safe
    /// to call multiple times and from multiple threads.
    /// </summary>
    /// <remarks>
    /// This method must never be called from the same method as code that directly references a
    /// <c>Microsoft.Build.*</c> type: the JIT resolves all types referenced by a method before
    /// running any of its statements, so by the time execution reached the registration call it
    /// would already be too late.
    /// </remarks>
    public static void EnsureMsBuildLocatorIsRegistered()
    {
        if (_registered)
        {
            return;
        }

        lock (RegistrationLock)
        {
            if (_registered)
            {
                return;
            }

            if (!MSBuildLocator.IsRegistered)
            {
                MSBuildLocator.RegisterDefaults();
            }

            _registered = true;
        }
    }
}
