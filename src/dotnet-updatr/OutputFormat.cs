namespace UpdatR.Cli;

/// <summary>
/// Format of the summary written to stdout by <c>--output</c>.
/// </summary>
public enum OutputFormat
{
    /// <summary>Human-readable, colored summary. Default.</summary>
    Text = 0,

    /// <summary>
    /// Machine-readable JSON only. Logs and any other diagnostic output are routed to stderr
    /// instead, so stdout can be safely piped to or parsed by another program.
    /// </summary>
    Json = 1,
}
