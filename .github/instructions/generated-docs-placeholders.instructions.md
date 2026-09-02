---
description: 'Generated NuGet package docs are placeholders and must stay untouched'
applyTo: 'src/UpdatR/docs/README.md,src/UpdatR/docs/release-notes.txt,src/dotnet-updatr/docs/README.md,src/dotnet-updatr/docs/release-notes.txt'
---

These four files are placeholders. Real content is written into them by `tools/Build/Build.cs`
right before `dotnet pack`, and reverted back to the checked-in placeholder afterwards
(`reset-generated-docs` target).

- Do not edit these files by hand.
- Do not commit changes to these files, even if a build/pack run has temporarily modified them
  locally - revert them (e.g. `git checkout -- <path>`) before committing.
- Update the real sources instead: the corresponding `docs/README.source.md` (processed by
  `dotnet mdsnippets`) for README content, and `src/Build/docs/release-notes.txt` for release
  notes.
