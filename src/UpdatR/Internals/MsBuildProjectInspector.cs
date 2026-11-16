using System.Runtime.CompilerServices;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Exceptions;
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

internal sealed record MsBuildProjectEvaluation(
    IReadOnlyList<string> TargetFrameworks,
    IReadOnlyList<PackageItemSource> PackageItemSources,
    IReadOnlyDictionary<string, IReadOnlyList<PackageItemSource>> PackageItemSourcesByTfm
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
    /// Disables the SDK's default item globs (<c>Compile</c>, <c>EmbeddedResource</c>,
    /// <c>PRIResource</c>, <c>None</c> - see
    /// <c>Sdks/Microsoft.NET.Sdk/targets/Microsoft.NET.Sdk.DefaultItems.props</c>) for every
    /// evaluation this type performs. Those globs recursively enumerate the entire project
    /// directory tree, which is by far the most expensive part of an SDK-style evaluation and is
    /// never needed here: this type reads framework properties and <c>PackageReference</c>,
    /// <c>PackageVersion</c> and <c>GlobalPackageReference</c> items, none of which are ever
    /// produced by a default item glob (they're always declared explicitly). Safe to apply
    /// unconditionally.
    /// </summary>
    private static readonly Dictionary<string, string> DisableDefaultItemsGlobalProperty = new()
    {
        ["EnableDefaultItems"] = "false",
    };

    /// <summary>
    /// Captures framework and package information in one evaluation per framework, plus the outer
    /// evaluation for a multi-targeted project. The result retains no MSBuild objects.
    /// </summary>
    public static MsBuildProjectEvaluation Evaluate(string projectPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);

        EnsureMsBuildLocatorIsRegistered();

        return EvaluateCore(projectPath);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static MsBuildProjectEvaluation EvaluateCore(string projectPath)
    {
        using var collection = new ProjectCollection(DisableDefaultItemsGlobalProperty);

        try
        {
            var project = collection.LoadProject(projectPath);
            var sources = GetPackageItemSources(project, includeVersionOverride: false);
            Dictionary<string, IReadOnlyList<PackageItemSource>> sourcesByTfm = new(
                StringComparer.OrdinalIgnoreCase
            );
            var targetFrameworks = project
                .GetPropertyValue("TargetFrameworks")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (
                !string.IsNullOrWhiteSpace(project.GetPropertyValue("TargetFramework"))
                || targetFrameworks.Length == 0
            )
            {
                var targetFramework = GetEvaluatedTargetFramework(project);

                return new(
                    string.IsNullOrWhiteSpace(targetFramework) ? [] : [targetFramework],
                    sources,
                    sourcesByTfm
                );
            }

            List<string> evaluatedFrameworks = [];

            foreach (var targetFramework in targetFrameworks)
            {
                var innerProject = collection.LoadProject(
                    projectPath,
                    new Dictionary<string, string>(DisableDefaultItemsGlobalProperty)
                    {
                        ["TargetFramework"] = targetFramework,
                    },
                    toolsVersion: null
                );
                var evaluatedFramework = GetEvaluatedTargetFramework(innerProject);

                if (!string.IsNullOrWhiteSpace(evaluatedFramework))
                {
                    evaluatedFrameworks.Add(evaluatedFramework);
                }

                sourcesByTfm[targetFramework] = GetPackageItemSources(
                    innerProject,
                    includeVersionOverride: true
                );
            }

            return new(evaluatedFrameworks, sources, sourcesByTfm);
        }
        catch (InvalidProjectFileException exception)
        {
            throw new InvalidUpdateTargetException(
                $"Could not evaluate project '{projectPath}' with MSBuild. "
                    + $"{exception.BaseMessage} .NET Framework is not supported.",
                exception
            );
        }
        finally
        {
            collection.UnloadAllProjects();
        }
    }

    private static string GetEvaluatedTargetFramework(Project project)
    {
        if (
            string.Equals(
                project.GetPropertyValue("TargetFrameworkIdentifier"),
                ".NETFramework",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            throw new InvalidUpdateTargetException(
                $"'{project.FullPath}' targets .NET Framework, which UpdatR does not support."
            );
        }

        var targetFramework = project.GetPropertyValue("TargetFramework");

        return string.IsNullOrWhiteSpace(targetFramework)
            ? project.GetPropertyValue("TargetFrameworkMoniker")
            : targetFramework;
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

        // Keep MSBuild types out of this method so registration runs before they are loaded.
        return GetImportedFilesCore(projectPath);
    }

    private static IReadOnlyList<PackageItemSource> GetPackageItemSources(
        Project project,
        bool includeVersionOverride
    ) =>
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
                    includeVersionOverride
                        ? GetVersionOrOverrideMetadata(item)
                        : GetVersionMetadata(item),
                    item.Xml!.ContainingProject.FullPath
                )),
        ];

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static IReadOnlyList<string> GetImportedFilesCore(string projectPath)
    {
        using var collection = new ProjectCollection(DisableDefaultItemsGlobalProperty);

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
    /// Same as <see cref="GetVersionMetadata"/>, but also falls back to a
    /// <c>VersionOverride</c> metadata value (used by a <c>PackageReference</c> to override a
    /// centrally-managed version for a single project under Central Package Management) when
    /// <c>Version</c> isn't set. Only used for per-framework package sources, to
    /// correlate results back to a hand-parsed <c>PackageReference</c> element that used
    /// <c>VersionOverride</c> instead of <c>Version</c> - unlike a centrally-managed
    /// <c>PackageVersion</c>'s <c>Version</c>, <c>VersionOverride</c> is a literal attribute
    /// already present at evaluation time, no build target needs to run to populate it.
    /// </summary>
    private static string? GetVersionOrOverrideMetadata(ProjectItem item) =>
        GetVersionMetadata(item)
        ?? (
            item.GetMetadataValue("VersionOverride") is { Length: > 0 } versionOverride
                ? versionOverride
                : null
        );

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
