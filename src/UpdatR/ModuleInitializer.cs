using System.Runtime.CompilerServices;

namespace UpdatR;

internal static class ModuleInitializer
{
    /// <summary>
    /// Eagerly registers MSBuildLocator's assembly resolver as soon as this assembly is loaded.
    /// </summary>
    /// <remarks>
    /// <see cref="UpdatR.MsBuild.MsBuildProjectInspector"/> is used to resolve
    /// <c>PackageReference</c>/<c>PackageVersion</c> items declared in <c>Directory.Build.props</c>
    /// and <c>Directory.Packages.props</c> files, which pulls in Microsoft.Build.Locator. Because
    /// this project also references <c>NuGet.Frameworks</c> - one of the assemblies
    /// Microsoft.Build.Locator requires to not be copied to the output directory (MSBL001), since
    /// MSBuild ships its own copy - <c>NuGet.Frameworks</c> is marked
    /// <c>ExcludeAssets="runtime"</c> in UpdatR.csproj. That means the assembly is not present in
    /// the output directory, and every call to a <c>NuGet.Frameworks</c> type (not just the new
    /// props/targets support) would fail with a <see cref="System.IO.FileNotFoundException"/>
    /// unless MSBuildLocator's assembly-resolve handler has already been registered - it redirects
    /// the load to the copy MSBuildLocator locates from an installed .NET SDK/Visual Studio.
    ///
    /// A module initializer is the only place guaranteed by the runtime to run before any type in
    /// this module is used by any caller, so it is the right place for this one-time setup. This
    /// is only safe because NuGet.Frameworks (and every other Microsoft.Build.* type) is strictly
    /// an implementation detail of UpdatR - it never appears in this assembly's public API - so no
    /// other assembly can ever reference a NuGet.Frameworks type without first going through a
    /// member of this module, which guarantees this method has already run.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage",
        "CA2255:The \'ModuleInitializer\' attribute is only intended to be used in application code or advanced source generator scenarios",
        Justification = "Intentional: see remarks above."
    )]
    [ModuleInitializer]
    internal static void Initialize()
    {
        UpdatR.MsBuild.MsBuildProjectInspector.EnsureMsBuildLocatorIsRegistered();
    }
}
