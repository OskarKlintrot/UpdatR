using System.Text;
using Xunit.Abstractions;

namespace UpdatR.E2e;

internal sealed class TestOutputHelperTextWriterAdapter : TextWriter
{
    private readonly ITestOutputHelper _output;

    private string _currentLine = string.Empty;

    public TestOutputHelperTextWriterAdapter(ITestOutputHelper output)
    {
        _output = output;
    }

    public override Encoding Encoding { get; } = Encoding.UTF8;
    public bool Enabled { get; set; } = true;

    public override void Write(char value)
    {
        if (!Enabled)
        {
            return;
        }

        if (value == '\n')
        {
            WriteCurrentLine();
        }
        else
        {
            _currentLine += value;
        }
    }

    private void WriteCurrentLine()
    {
        _output.WriteLine(_currentLine);

        _currentLine = string.Empty;
    }

    protected override void Dispose(bool disposing)
    {
        if (!string.IsNullOrWhiteSpace(_currentLine))
        {
            WriteCurrentLine();
        }

        base.Dispose(disposing);
    }
}
