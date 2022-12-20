// CA1852 Type 'Program' can be sealed because it has no subtypes in its containing assembly and is not externally visible
#pragma warning disable CA1852 // <-- Disabled due to bug: https://github.com/dotnet/roslyn-analyzers/issues/6141

// begin-snippet: SampleUsage
using UpdatR;
using UpdatR.Formatters;

var updatr = new Updater(); // Can take an ILogger

var summary = await updatr.UpdateAsync("path");

if (summary.UpdatedPackagesCount == 0) // No packages where updated
{
    return;
}

var title = MarkdownFormatter.GenerateTitle(summary);

var description =
    "# PR created automatically by UpdatR"
    + Environment.NewLine
    + Environment.NewLine
    + MarkdownFormatter.GenerateDescription(summary);

// Use title as title in the PR and description as the description/body in the PR
// end-snippet
