---
description: 'How README.md and release notes are sourced, generated and updated'
applyTo: 'README.md,mdsource/**,src/Build/docs/release-notes.txt,src/UpdatR/docs/README.source.md,src/dotnet-updatr/docs/README.source.md'
---

## How the READMEs are built

`README.md` (repo root) is generated - never edit it by hand. It's built by
[MarkdownSnippets](https://github.com/SimonCropp/MarkdownSnippets) (`mdsnippets.json`,
`SourceTransform` convention) from `mdsource/README.source.md`, which only includes the parts in
`mdsource/`:

| Source                                | Content                                                                 | Also in NuGet README of |
| ------------------------------------- | ----------------------------------------------------------------------- | ----------------------- |
| `mdsource/header.include.md`          | Build badge                                                             | -                       |
| `mdsource/cli-readme.include.md`      | `dotnet-updatr`: usage, every CLI option and every `.updatrrc` option   | `dotnet-updatr`         |
| `mdsource/cli-usage.txt`              | Generated `--help` output (`snippet: cli-usage.txt`) - never edit       | `dotnet-updatr`         |
| `mdsource/sdk-readme.include.md`      | `UpdatR` SDK: short intro and `snippet: SampleUsage` only               | `UpdatR`                |
| `mdsource/known-issues.include.md`    | Known issues                                                            | -                       |
| `mdsource/footer.include.md`          | Icon attribution (must stay last - the build checks this)               | both                    |

`src/<project>/docs/README.source.md` produce each package's NuGet README the same way. Their
output, `src/<project>/docs/README.md`, and `src/<project>/docs/release-notes.txt` are
placeholders - see `generated-docs-placeholders.instructions.md`.

## Updating the README

- Document new or changed CLI options and `.updatrrc` options in `mdsource/cli-readme.include.md`,
  next to the existing options and in the same style (one paragraph per `.updatrrc` option, kept
  in sync with the example JSON blocks and `config init --example` output).
- Keep `mdsource/sdk-readme.include.md` limited to its intro and the usage snippet. Don't add
  feature sections there; the SDK is documented through XML docs and the CLI section.
- CLI option descriptions come from `src/dotnet-updatr/Program.cs`; change them there.
- Regenerate, then revert the placeholders before committing:

  ```powershell
  cd tools/Build
  dotnet run Build.cs -- generate-docs
  cd ../..
  git checkout -- src/UpdatR/docs/README.md src/UpdatR/docs/release-notes.txt src/dotnet-updatr/docs/README.md src/dotnet-updatr/docs/release-notes.txt
  ```

  `generate-docs` builds the solution, rewrites `mdsource/cli-usage.txt` from `--help`, runs
  `mdsnippets` and fills in the placeholders. Commit the regenerated `README.md` and
  `mdsource/cli-usage.txt` together with the source change. (The `docs` workflow also runs
  `update-README` on `main`, but don't rely on it.)

## Updating release notes

`src/Build/docs/release-notes.txt` holds the notes for the next release. It's copied into both
packages at pack time and emptied automatically after a stable release
(`restore-release-notes-txt`), so it only ever contains unreleased changes.

- Add one line per user-visible change, starting with `- `. Prefix breaking changes with
  `- breaking: `.
- Name the public surface that changed: CLI flags (`--flag`), `.updatrrc` keys,
  `UpdateOptions`/`Summary` members and enum values, in backticks.
- Describe behavior, not implementation. Skip internal refactorings, tests and docs-only changes.
- Don't add version headers or dates, and don't edit the copies under `src/<project>/docs/`.
