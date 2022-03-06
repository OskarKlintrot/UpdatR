[![build](https://github.com/OskarKlintrot/UpdatR/actions/workflows/build.yml/badge.svg)](https://github.com/OskarKlintrot/UpdatR/actions/workflows/build.yml)

# UpdatR

## dotnet-updatr

[![Latest Nuget Version](https://badgen.net/nuget/v/dotnet-updatr/latest)](https://www.nuget.org/packages/dotnet-updatr/)
[![Latest Nuget Version](https://badgen.net/nuget/dt/dotnet-updatr)](https://www.nuget.org/packages/dotnet-updatr/)

Dotnet tool for updating package reference and dotnet-tools.json.

See [UpdatR](#updatrupdate) for SDK.

### Installation

```
> dotnet tool install --global dotnet-updatr
```

#### Basic Usage

To update all `*.csproj` and `dotnet-tools.json` recursivly:

```
> update
```

If you only want to update the `*.csproj` and `dotnet-tools.json` that is part of a solution you can specifiy the solution directly:

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

#### As part of CI/CD

You can get the output as a markdown by setting a path for the output:

```
> update --output path/to/output/folder
```

It's possible to get the title and the rest of the output as separate .md-files which is helpful when creating a pull request:

```
> update --title path/to/title.md --description path/to/description.md
```

then you can use `title.md` as the title for your pull request and `description.md` as the body.

UpdatR is used to update it's own dependencies, have a look at [Build.cs](https://github.com/OskarKlintrot/UpdatR/blob/main/tools/Build/Build.cs) for an example that uses [Bullseye](https://www.nuget.org/packages/Bullseye) and [SimpleExec](https://www.nuget.org/packages/SimpleExec). However, if you are using C# in your CI/CD pipeline it's probably easier to just use [UpdatR.Update](#updatrupdate) directly instead. That's the package that powers UpdatR under the hood.

#### All options

```
Usage:
  UpdatR.Update.Cli [<args>] [options]

Arguments:
  <args>  Path to solution or project(s). Defaults to current folder. Target can be a specific file or
          folder. If target is a folder then all *.csproj-files and dontet-config.json-files will be
          processed. [default: .]

Options:
  --output <output>                                    Defaults to "output.md". Explicitly set to
                                                       fileName.txt to generate plain text instead of
                                                       markdown. []
  --title <title>                                      Outputs title to path. []
  --description <description>                          Outputs description to path. []
  --verbosity                                          Log level. [default: Warning]
  <Critical|Debug|Error|Information|None|Trace|Warnin
  g>
  --dry-run                                            Do not save any changes. [default: False]
  --browser                                            Open summary in browser. [default: False]
  --interactive                                        Interaction with user is possible. [default: False]
  --version                                            Show version information
  -?, -h, --help                                       Show help and usage information
```

## UpdatR

[![Latest Nuget Version](https://badgen.net/nuget/v/UpdatR/latest)](https://www.nuget.org/packages/UpdatR/)
[![Latest Nuget Version](https://badgen.net/nuget/dt/UpdatR)](https://www.nuget.org/packages/UpdatR/)

NuGet package to programmatically update package reference and dotnet-tools.json.

See [dotnet-updatr](#dotnet-updatr) for a dotnet tool that can be run from the command-line.

### Usage

```csharp
using UpdatR;

var updatr = new Updater(); // Can take an ILogger

var summary = await updatr.UpdateAsync("path");

var title = MarkdownFormatter.GenerateTitle(summary);

var description = "# PR created automatically by UpdatR"
    + Environment.NewLine
    + Environment.NewLine
    + MarkdownFormatter.GenerateDescription(summary);

// Use title as title in the PR and description as the description/body in the PR
```

# Icon
Package by Sergey Novosyolov from [NounProject.com](http://NounProject.com)
