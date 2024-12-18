## dotnet-updatr

[![Latest Nuget Version](https://badgen.net/nuget/v/dotnet-updatr/latest)](https://www.nuget.org/packages/dotnet-updatr/)
[![Latest Nuget Version](https://badgen.net/nuget/dt/dotnet-updatr)](https://www.nuget.org/packages/dotnet-updatr/)

Dotnet tool for updating package reference and dotnet-tools.json.

The tool will try to stick to package versions that is supported by the projects target framework moniker. If a package supports both .NETStandard and .NET, the compatibility with .NETStandard will be ignored if the project is targeting .NET. This is to avoid false positives where a package technically supports a TFM but in reality never have been tested against the TFM.

See [UpdatR](#updatr) for SDK.

### Installation

```
> dotnet tool install --global dotnet-updatr
```

### Basic Usage

To update all `*.csproj` and `dotnet-tools.json` recursively:

```
> update
```

If you only want to update the `*.csproj` and `dotnet-tools.json` that is part of a solution you can specify the solution directly:

```
> update path/to/solution.sln
```

You can also update a single `*.csproj` or `dotnet-config.json`:

```
> update path/to/example.csproj
```

If you want to preview the result you can do a dry run:

```
> update --dry-run
```

For larger solutions with multiple packages the console output is not optimal. You can choose to view the result in your default browser instead:

```
> update --browser
```

To allow packages to be updated to prerelease versions use the `--prerelease` options:

```
> update --prerelease
```

To update only one or more specific packages you can use the `--package` option:

```
> update --package Microsoft.* --package Newtonsoft.*
```

If you don't want to update a package or packages you can exclude them:

```
> update --exclude-package Microsoft.* --exclude-package Newtonsoft.*
```

If UpdatR fails to find the correct lowest TFM to support, for example for projects that supports multiple TFM's, then it's possible to set the TFM manually:

```
> update --tfm net6.0
```

### As part of CI/CD

You can get the output as a markdown by setting a path for the output:

```
> update --output path/to/output/folder
```

It's possible to get the title and the rest of the output as separate .md-files which is helpful when creating a pull request:

```
> update --title path/to/title.md --description path/to/description.md
```

then you can use `title.md` as the title for your pull request and `description.md` as the body.

UpdatR is used to update it's own dependencies, have a look at [Build.cs](https://github.com/OskarKlintrot/UpdatR/blob/main/tools/Build/Build.cs) for an example that uses [Bullseye](https://www.nuget.org/packages/Bullseye) and [SimpleExec](https://www.nuget.org/packages/SimpleExec). However, if you are using C# in your CI/CD pipeline it's probably easier to just use [UpdatR](#updatr) directly instead. That's the package that powers `dotnet-updatr` under the hood.

### All options

snippet: cli-usage.txt