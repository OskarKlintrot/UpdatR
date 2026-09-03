## dotnet-updatr

[![Latest Nuget Version](https://badgen.net/nuget/v/dotnet-updatr/latest)](https://www.nuget.org/packages/dotnet-updatr/)
[![Latest Nuget Version](https://badgen.net/nuget/dt/dotnet-updatr)](https://www.nuget.org/packages/dotnet-updatr/)

Dotnet tool for updating package reference, dotnet-tools.json and `#:package` directives in file-based apps.

The tool will try to stick to package versions that is supported by the projects target framework moniker. If a package supports both .NETStandard and .NET, the compatibility with .NETStandard will be ignored if the project is targeting .NET. This is to avoid false positives where a package technically supports a TFM but in reality never have been tested against the TFM.

See [UpdatR](#updatr) for SDK.

### Installation

```
> dotnet tool install --global dotnet-updatr
```

### Basic Usage

To update all `*.csproj` files, `dotnet-tools.json` files and file-based apps (`.cs` files with `#:package` directives) recursively:

```
> update
```

If you only want to update the `*.csproj` files, `dotnet-tools.json` files and file-based apps that is part of a solution you can specify the solution directly:

```
> update path/to/solution.sln
```

You can also update a single `*.csproj`, `dotnet-tools.json` or file-based app:

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

If you don't want to allow packages with certain licenses to be installed you can specify which licenses are allowed:

```
> update --allowed-licenses MIT --allowed-licenses Apache-2.0
```

Packages without any license metadata are always allowed, and this only affects installing new versions - it will neither touch nor warn about already installed packages that don't match, unless a newer version is available.

If there are specific files you do not want touched, you can exclude them by path:

```
> update --exclude-file "tests/**/Resources/**"
```

If UpdatR fails to find the correct lowest TFM to support, for example for projects that supports multiple TFM's, then it's possible to set the TFM manually:

```
> update --tfm net6.0
```

### `.updatrrc` config file

Instead of (or in addition to) `--exclude-package` and `--allowed-licenses` you can add a `.updatrrc` JSON file, either next to the target path or in the current working directory:

```json
{
  "excludePackages": ["Microsoft.*", "Newtonsoft.*"],
  "allowedLicenses": ["MIT", "Apache-2.0"],
  "path": "src/MySolution.sln",
  "excludeFiles": ["tests/**/Resources/**"],
  "alignWithTfm": ["Microsoft.Extensions.*"]
}
```

All options are optional. `excludePackages`, `allowedLicenses`, `excludeFiles` and `alignWithTfm` are merged with the corresponding command line option, if given. `path` is only used when no target path is given on the command line (i.e. it resolves to the current directory) - it's resolved relative to the directory the `.updatrrc` file is in, and lets you point `update` at, say, a solution file by default instead of recursively scanning every `*.csproj`, `dotnet-tools.json` and file-based app under the current directory. `excludeFiles` supports `*` as wildcard and is matched against each file's path relative to the resolved target - use it to permanently exclude files (e.g. test fixtures) that would otherwise be picked up.

`//` line comments, `/* */` block comments and trailing commas are allowed in `.updatrrc`.

`alignWithTfm` supports `*` as wildcard and is matched against package ids. Some packages (e.g. `Microsoft.Extensions.*`) release versions that multi-target several TFMs, including newer ones than your project targets - which means UpdatR would normally update to that newer major even though it's not actually required, leading to mismatched majors across a package family. Packages matching `alignWithTfm` are instead capped to the major version of the project's target framework (the lowest, for multi-targeted projects), as long as the currently installed version isn't already ahead of it. It also applies to `dotnet-tools.json`, aligned with the target framework(s) of the project(s) the tool manifest applies to - e.g. keeping `dotnet-ef` in step with `Microsoft.EntityFrameworkCore`.

Use `update config init` to create a `.updatrrc` file with all options present, but empty:

```
> update config init
```

By default it's created in the current directory. Pass a path to create it elsewhere, and `--force` to overwrite an existing file:

```
> update config init path/to/project --force
```

Use `update config validate` to check that a `.updatrrc` file is valid:

```
> update config validate
```

Like `init`, it defaults to looking for `.updatrrc` in the current directory, but a path to a directory or the file itself can be given instead:

```
> update config validate path/to/project
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