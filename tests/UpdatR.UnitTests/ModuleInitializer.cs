using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

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
        RedirectTempPathToIsolatedDirectory();

        UpdatR.Internals.MsBuildProjectInspector.EnsureMsBuildLocatorIsRegistered();
    }

    /// <summary>
    /// Redirects <see cref="Path.GetTempPath"/> for this whole test process to an isolated
    /// subdirectory instead of the OS temp root.
    /// </summary>
    /// <remarks>
    /// Several tests (e.g. <c>CsprojTests</c>, <c>PropsFileTests</c>) write their fixture project
    /// directly into <see cref="Path.GetTempPath"/>. For multi-targeted fixtures, that project
    /// gets evaluated by the real MSBuild engine (see <c>MsBuildProjectInspector</c>), and MSBuild
    /// evaluation expands the SDK's default item globs (<c>&lt;None Include="**\*"&gt;</c> and
    /// friends) <b>relative to the project's directory</b>. On a developer machine the OS temp
    /// root routinely accumulates tens of thousands of files from unrelated tools, so every such
    /// evaluation pays for a full recursive scan of that entire tree - multiple seconds each,
    /// dozens of times across the suite. A fresh CI runner has an empty temp root and never sees
    /// this cost, which is why this was invisible there while taking minutes locally.
    /// <para/>
    /// Redirecting to a small, private, per-process subdirectory (cleaned up on exit, with a
    /// best-effort sweep of stale directories from previous test runs) makes every such
    /// evaluation cheap regardless of how cluttered the real OS temp root is, without touching
    /// each test individually - including tests that only use <see cref="Path.GetTempPath"/> as a
    /// string root and never trigger MSBuild at all.
    /// </remarks>
    private static void RedirectTempPathToIsolatedDirectory()
    {
        var isolatedRoot = Path.Combine(
            Path.GetTempPath(),
            "UpdatR.UnitTests",
            Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)
        );

        Directory.CreateDirectory(isolatedRoot);

        CleanUpStaleDirectories(Path.Combine(Path.GetTempPath(), "UpdatR.UnitTests"), isolatedRoot);

        // Path.GetTempPath() re-reads these environment variables on every call, so setting them
        // on the current process is sufficient - no caching to invalidate.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Environment.SetEnvironmentVariable("TMP", isolatedRoot);
            Environment.SetEnvironmentVariable("TEMP", isolatedRoot);
        }
        else
        {
            Environment.SetEnvironmentVariable("TMPDIR", isolatedRoot);
        }

        AppDomain.CurrentDomain.ProcessExit += (_, _) => TryDeleteDirectory(isolatedRoot);
    }

    private static void CleanUpStaleDirectories(string parent, string currentRoot)
    {
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(parent))
            {
                if (!string.Equals(directory, currentRoot, StringComparison.OrdinalIgnoreCase))
                {
                    TryDeleteDirectory(directory);
                }
            }
        }
        catch (IOException)
        {
            // Best-effort only - a previous run's directory might still be in use, or the parent
            // might not exist yet.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort only.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort only.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort only.
        }
    }
}
