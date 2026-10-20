## UpdatR

[![Latest Nuget Version](https://badgen.net/nuget/v/UpdatR/latest)](https://www.nuget.org/packages/UpdatR/)
[![Latest Nuget Version](https://badgen.net/nuget/dt/UpdatR)](https://www.nuget.org/packages/UpdatR/)

NuGet package to programmatically update package reference, dotnet-tools.json and `#:package` directives in file-based apps.

The tool will try to stick to package versions that is supported by the projects target framework moniker. If a package supports both .NETStandard and .NET, the compatibility with .NETStandard will be ignored if the project is targeting .NET. This is to avoid false positives where a package technically supports a TFM but in reality never have been tested against the TFM.

See [dotnet-updatr](#dotnet-updatr) for a dotnet tool that can be run from the command-line.

### Usage

snippet: SampleUsage

### Minimum package publication age

Set `UpdateOptions.MinPublishAgeHours` (or `minPublishAgeHours` in `.updatrrc`) to keep versions published within that many hours from being picked as update targets. `0` disables the check. The value applies to every package source; NuGet's `minPublishAgeHours` and `minPublishAgeExceptions` in `nuget.config` (.NET 11 SDK) aren't read yet. Held-back versions are reported in `Summary.SkippedUpdatePackages` with `SkippedUpdateReason.MinimumPublishAge`. An invalid value, or a missing publish date while an age is set, throws `MinimumPublishAgeException` before any files are changed. See [dotnet-updatr](#dotnet-updatr) for details.
