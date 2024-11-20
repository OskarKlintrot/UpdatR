## UpdatR

[![Latest Nuget Version](https://badgen.net/nuget/v/UpdatR/latest)](https://www.nuget.org/packages/UpdatR/)
[![Latest Nuget Version](https://badgen.net/nuget/dt/UpdatR)](https://www.nuget.org/packages/UpdatR/)

NuGet package to programmatically update package reference and dotnet-tools.json.

The tool will try to stick to package versions that is supported by the projects target framework moniker. If a package supports both .NETStandard and .NET, the compatibility with .NETStandard will be ignored if the project is targeting .NET. This is to avoid false positives where a package technically supports a TFM but in reality never have been tested against the TFM.

See [dotnet-updatr](#dotnet-updatr) for a dotnet tool that can be run from the command-line.

### Usage

snippet: SampleUsage
