using NuGet.Versioning;

namespace UpdatR.Update.Internals;
internal static class State
{
    public static NuGetVersion? EntityFrameworkVersion { get; private set; }

    public static void SetEntityFrameworkVersion(NuGetVersion version) => EntityFrameworkVersion = version;
}
