namespace UpdatR;

/// <summary>
/// Minimum severity of finding that should cause <see cref="Summary.ShouldFail"/> to be
/// <see langword="true"/>, intended for CI gating (e.g. an unhandled exception is a run failure,
/// while <see cref="Summary.ShouldFail"/> is "the run succeeded, but found something you told it
/// to fail on").
/// </summary>
/// <remarks>
/// Levels are cumulative: a higher level's findings also satisfy every lower level, e.g.
/// <see cref="Vulnerable"/> packages also count towards <see cref="Deprecated"/> and
/// <see cref="Outdated"/>.
/// <list type="table">
/// <listheader>
/// <term>Level</term>
/// <description>Fails when</description>
/// </listheader>
/// <item>
/// <term><see cref="None"/> (0)</term>
/// <description>Never. Default.</description>
/// </item>
/// <item>
/// <term><see cref="Outdated"/> (1)</term>
/// <description>
/// Any package was updated, is deprecated, or is vulnerable. Most useful together with
/// <see cref="UpdateOptions.DryRun"/>, to fail a CI run when packages need updating without
/// actually changing anything.
/// </description>
/// </item>
/// <item>
/// <term><see cref="Deprecated"/> (2)</term>
/// <description>Any package is deprecated or vulnerable.</description>
/// </item>
/// <item>
/// <term><see cref="Vulnerable"/> (3)</term>
/// <description>Any package is vulnerable.</description>
/// </item>
/// </list>
/// </remarks>
public enum FailOn
{
    /// <summary>Never fail. Default.</summary>
    None = 0,

    /// <summary>
    /// Fail if any package was updated, is deprecated, or is vulnerable. Most useful together
    /// with <see cref="UpdateOptions.DryRun"/>, to fail a CI run when packages need updating
    /// without actually changing anything.
    /// </summary>
    Outdated = 1,

    /// <summary>Fail if any package is deprecated or vulnerable.</summary>
    Deprecated = 2,

    /// <summary>Fail if any package is vulnerable.</summary>
    Vulnerable = 3,
}
