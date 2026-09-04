namespace UpdatR;

/// <summary>
/// Why a newer version of a package exists but wasn't applied, even though it isn't a license
/// mismatch (see <see cref="LicenseMismatchPackage"/> for that case) and isn't an unsupported
/// version range/floating version (see <see cref="UnsupportedRangePackage"/> for that case).
/// </summary>
public enum SkippedUpdateReason
{
    /// <summary>
    /// The newer version's major component is ahead of what <c>alignWithTfm</c> allows for the
    /// project's target framework(s) - see <c>alignWithTfm</c> in <see cref="UpdateOptions"/> and
    /// <see cref="UpdatRConfig"/>.
    /// </summary>
    AlignedWithTfm,

    /// <summary>
    /// The newer version's major component is ahead of what a matching
    /// <see cref="UpdatR.PackageVersionPolicy"/> allows.
    /// </summary>
    PackageVersionPolicy,

    /// <summary>
    /// The newer version isn't compatible with every one of the project's target framework(s).
    /// </summary>
    IncompatibleTargetFramework,
}
