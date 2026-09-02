using System.Runtime.CompilerServices;

namespace UpdatR.UnitTests;

internal static class ModuleInitializer
{
    /// <summary>
    /// Eagerly registers MSBuildLocator's assembly resolver as soon as this test assembly is
    /// loaded. Needed because this project references <c>NuGet.Frameworks</c> directly (e.g.
    /// <c>NuGetFramework.Parse</c> in test setup code), and that reference must be excluded from
    /// the output directory - see the comment on the same PackageReference in this project's
    /// .csproj file - for the same MSBL001 reason as UpdatR.csproj itself.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage",
        "CA2255:The \'ModuleInitializer\' attribute is only intended to be used in application code or advanced source generator scenarios",
        Justification = "Intentional: guarantees MSBuildLocator is registered before any test in this assembly runs."
    )]
    [ModuleInitializer]
    internal static void Initialize()
    {
        UpdatR.Internals.MsBuildProjectInspector.EnsureMsBuildLocatorIsRegistered();
    }
}
