#region SampleUsage
using UpdatR;
using UpdatR.Formatters;

var updatr = new Updater(); // Can take an ILogger

var summary = await updatr.UpdateAsync("path");

if (summary.UpdatedPackagesCount == 0) // No packages where updated
{
    return;
}

var title = MarkdownFormatter.GenerateTitle(summary);

var description = "# PR created automatically by UpdatR"
    + Environment.NewLine
    + Environment.NewLine
    + MarkdownFormatter.GenerateDescription(summary);

// Use title as title in the PR and description as the description/body in the PR
#endregion
