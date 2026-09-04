using System.Runtime.InteropServices;

namespace UpdatR.Domain.Utils;

/// <summary>
/// Comparer for file/directory paths. Windows and macOS have case-insensitive file systems by
/// default, but Linux is case-sensitive - comparing paths with a hardcoded
/// <see cref="StringComparer.OrdinalIgnoreCase"/> everywhere would silently treat e.g.
/// <c>Directory.Build.props</c> and <c>directory.build.props</c> as the same file on Linux, even
/// though the file system (and MSBuild, and every other tool) treats them as distinct.
/// </summary>
internal static class PathComparer
{
    /// <summary>
    /// <see langword="true"/> on platforms whose default file system is case-insensitive
    /// (Windows, macOS). Used, rather than a runtime file-system probe, because it only needs to
    /// match the case-sensitivity every other tool in the toolchain (MSBuild, the shell, git)
    /// already assumes for the current OS.
    /// </summary>
    public static bool IsCaseInsensitiveFileSystem =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        || RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    /// <summary>
    /// OS-aware path equality/hashing comparer - case-insensitive on Windows/macOS, case-sensitive
    /// on Linux.
    /// </summary>
    public static StringComparer Comparer { get; } =
        IsCaseInsensitiveFileSystem ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>
    /// OS-aware <see cref="StringComparison"/> for path equality checks - case-insensitive on
    /// Windows/macOS, case-sensitive on Linux.
    /// </summary>
    public static StringComparison Comparison { get; } =
        IsCaseInsensitiveFileSystem ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>
    /// OS-aware equality check for two paths, using <see cref="Comparison"/>.
    /// </summary>
    public static bool Equals(string? path1, string? path2) =>
        string.Equals(path1, path2, Comparison);
}
