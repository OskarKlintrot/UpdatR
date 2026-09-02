using NuGet.Frameworks;
using NuGet.Versioning;

namespace UpdatR.Domain.Utils;

/// <summary>
/// Resolves the major version a package should be capped at, to keep runtime-versioned packages
/// (e.g. <c>Microsoft.Extensions.*</c>) aligned with a project's target framework, rather than
/// updating past it just because a newer major version happens to also be compatible - e.g. a
/// package that multi-targets both <c>net9.0</c> and <c>net10.0</c> in the same (higher-major)
/// release.
/// </summary>
internal static class TfmAlignment
{
    /// <summary>
    /// The major version to align to - the lowest major of every modern (<c>net5.0</c>+)
    /// <c>.NETCoreApp</c> framework in <paramref name="tfms"/>. <see langword="null"/> if none of
    /// <paramref name="tfms"/> is a modern <c>.NETCoreApp</c> framework (e.g. only .NET Framework
    /// or .NET Standard), since there's no meaningful alignment target then.
    /// </summary>
    public static int? ResolveAlignMajor(IReadOnlyCollection<NuGetFramework> tfms)
    {
        int? lowest = null;

        foreach (var tfm in tfms)
        {
            if (
                tfm.Framework != FrameworkConstants.FrameworkIdentifiers.NetCoreApp
                || tfm.Version.Major < 5
            )
            {
                continue;
            }

            if (lowest is null || tfm.Version.Major < lowest)
            {
                lowest = tfm.Version.Major;
            }
        }

        return lowest;
    }

    /// <summary>
    /// A package is only capped to <paramref name="alignMajor"/> if its currently installed
    /// version isn't already ahead of it - if it is (e.g. updated past the target framework's
    /// major some other way), updates are left unrestricted rather than trying to pull it back
    /// down.
    /// </summary>
    public static int? ResolveMaxMajor(int? alignMajor, NuGetVersion installedVersion) =>
        alignMajor is int major && installedVersion.Major <= major ? major : null;
}
