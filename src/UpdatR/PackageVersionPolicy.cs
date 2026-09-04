namespace UpdatR;

/// <summary>
/// Caps a package (or a wildcard set of packages, matched the same way as <c>--package</c> /
/// <c>excludePackages</c>) at a fixed major version, regardless of what would otherwise be the
/// latest compatible/available version. Unlike <c>alignWithTfm</c> - which derives its cap
/// dynamically from a project's target framework - this is a fixed pin, e.g. to intentionally
/// stay on a package's 3.x line while still picking up 3.x patch/minor releases. When both a
/// matching <see cref="PackageVersionPolicy"/> and <c>alignWithTfm</c> apply to the same package,
/// the more restrictive (lower) major wins. The first matching policy, in the order given, is
/// used if more than one pattern matches the same package id.
/// </summary>
/// <param name="PackageIdPattern">
/// Package id pattern to match, supports <c>*</c> as wildcard - e.g. <c>Serilog*</c>.
/// </param>
/// <param name="MaxMajor">The highest major version an update may move to.</param>
public sealed record PackageVersionPolicy(string PackageIdPattern, int MaxMajor);
