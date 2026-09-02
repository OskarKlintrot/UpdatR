using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace UpdatR;

internal static class ModuleInitializer
{
    /// <summary>
    /// Eagerly registers MSBuildLocator's assembly resolver as soon as this assembly is loaded.
    /// </summary>
    /// <remarks>
    /// <see cref="Internals.MsBuildProjectInspector"/> is used to resolve
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
    ///
    /// If no .NET SDK/Visual Studio installation can be found, registration fails and is
    /// re-thrown as a clear <see cref="InvalidOperationException"/> explaining the actual
    /// requirement, instead of surfacing as a confusing exception from whatever unrelated line of
    /// user code happened to first touch this assembly.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage",
        "CA2255:The \'ModuleInitializer\' attribute is only intended to be used in application code or advanced source generator scenarios",
        Justification = "Intentional: see remarks above."
    )]
    [ModuleInitializer]
    internal static void Initialize()
    {
        // Microsoft.Build.Locator.dll is bundled directly into UpdatR's own package output
        // instead of being declared as a regular NuGet dependency (see the PrivateAssets="all"
        // comment in UpdatR.csproj - a real dependency would break every consumer's build via its
        // MSBL001 buildTransitive check). That means it isn't guaranteed to be listed in a
        // consuming app's .deps.json, which the default AssemblyLoadContext uses to build its
        // trusted-assembly list for framework-dependent apps. Relying on that default, implicit
        // probing to still find the file is fragile: it IS physically copied next to UpdatR.dll
        // (NuGet copies every file under a referenced package's lib/<tfm> folder to the consumer's
        // output directory regardless of .deps.json), but resolving a simple assembly name still
        // failed with a FileNotFoundException on Linux even though the exact same package/output
        // layout worked fine on Windows. Resolving it explicitly from UpdatR.dll's own directory
        // removes any dependency on .deps.json - or on any OS-specific probing behavior - entirely.
        //
        // This registration MUST happen in a method that itself contains no reference to any
        // Microsoft.Build.* type: the JIT resolves every type referenced by a method's IL -
        // including simple call targets - while compiling THAT method, before any of its
        // instructions actually run. If the Resolving registration and the call into
        // MsBuildProjectInspector lived in the same method, compiling this method would try to
        // resolve Microsoft.Build.Locator before the registration line ever executed, so the
        // handler would never get a chance to run (this was tried and confirmed not to fix the
        // issue). Splitting the actual usage into a separate NoInlining method defers resolving
        // Microsoft.Build.* types until that method is actually invoked, by which point the
        // handler below is already registered.
        AssemblyLoadContext.Default.Resolving += ResolveBundledDependency;

        RegisterMsBuildLocator();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RegisterMsBuildLocator()
    {
        try
        {
            Internals.MsBuildProjectInspector.EnsureMsBuildLocatorIsRegistered();
        }
        catch (Exception ex)
        {
            // Re-thrown with a clear, actionable message: an unhandled exception thrown from a
            // module initializer surfaces at the very first (unrelated-looking) line of user code
            // that happens to touch this assembly, which is a confusing place to learn "install
            // the .NET SDK" from. UpdatR fundamentally requires a local .NET SDK or Visual Studio
            // installation - not just the .NET runtime - because NuGet.Frameworks (used for every
            // target framework comparison, not just .props/.targets support) is deliberately not
            // shipped in UpdatR's own output (see the PackageReference PrivateAssets comment in
            // UpdatR.csproj) and is only ever resolved via MSBuildLocator redirecting to whatever
            // SDK/Visual Studio installation it finds on the machine.
            throw new InvalidOperationException(
                "UpdatR requires a .NET SDK or Visual Studio installation to be present on this "
                    + "machine (not just the .NET runtime). None could be found. Install the .NET "
                    + "SDK from https://dotnet.microsoft.com/download and try again.",
                ex
            );
        }
    }

    private static Assembly? ResolveBundledDependency(
        AssemblyLoadContext context,
        AssemblyName assemblyName
    )
    {
        if (assemblyName.Name != "Microsoft.Build.Locator")
        {
            return null;
        }

        var updatrDirectory = Path.GetDirectoryName(typeof(ModuleInitializer).Assembly.Location);

        if (string.IsNullOrEmpty(updatrDirectory))
        {
            return null;
        }

        var candidatePath = Path.Combine(updatrDirectory, assemblyName.Name + ".dll");

        return File.Exists(candidatePath) ? context.LoadFromAssemblyPath(candidatePath) : null;
    }
}
