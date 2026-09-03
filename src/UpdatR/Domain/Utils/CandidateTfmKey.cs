namespace UpdatR.Domain.Utils;

/// <summary>
/// Builds a lookup key identifying a single <c>PackageReference</c>, <c>PackageVersion</c> or
/// <c>GlobalPackageReference</c> occurrence as written in a project/props file - i.e. its item
/// type, package id and the exact version string attached to it. Used to correlate a
/// hand-parsed XML element (found via <see cref="Csproj.EnumerateCandidates"/> or
/// <see cref="PropsFile.EnumerateCandidates"/>, which sees every occurrence regardless of any
/// <c>Condition</c>) with the target framework(s) a real, per-framework MSBuild evaluation (see
/// <see cref="Internals.MsBuildProjectInspector.GetPackageItemSourcesByTfm"/>) determined it
/// actually applies to.
/// </summary>
/// <remarks>
/// Relies on the version being a literal string (not a property reference like
/// <c>$(FooVersion)</c>), same as everywhere else in UpdatR that reads a raw <c>Version</c>/
/// <c>VersionOverride</c> attribute directly. Two differently-conditioned occurrences of the same
/// package that coincidentally share the exact same version string can't be told apart this way,
/// but that's harmless: whichever one an update is applied to, both would resolve to the same new
/// version anyway.
/// </remarks>
internal static class CandidateTfmKey
{
    public static string Create(string itemType, string packageId, string version) =>
        $"{itemType}\u0001{packageId}\u0001{version}";
}
