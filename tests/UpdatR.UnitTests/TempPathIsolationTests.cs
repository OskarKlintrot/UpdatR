namespace UpdatR.UnitTests;

/// <summary>
/// Guards the invariant established by <see cref="ModuleInitializer"/>: this test assembly must
/// never run against the real OS temp root. See the remarks on
/// <c>ModuleInitializer.RedirectTempPathToIsolatedDirectory</c> for why - a cluttered OS temp
/// root (tens of thousands of files, as accumulates on a real developer machine over time) turns
/// every MSBuild evaluation performed by a multi-targeted fixture (e.g. in <c>CsprojTests</c>)
/// into a multi-second recursive directory scan, which once made this whole suite take 17 minutes
/// locally while staying under 12 seconds in CI (where the temp root starts empty). This
/// regression is invisible in CI, so it needs an explicit guard rather than relying on someone
/// noticing a slowdown.
/// </summary>
public class TempPathIsolationTests
{
    [Fact]
    public void TempPathIsRedirectedToAnIsolatedSubdirectory()
    {
        var tempPath = Path.GetTempPath();

        Assert.Contains(
            "UpdatR.UnitTests" + Path.DirectorySeparatorChar,
            tempPath,
            StringComparison.Ordinal
        );
    }
}
