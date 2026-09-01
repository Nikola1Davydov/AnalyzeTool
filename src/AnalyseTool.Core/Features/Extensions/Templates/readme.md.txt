# __ExtensionId__

An AnalyseTool extension. This folder is both the project you develop in and the folder that ships:
`plugin.json` and the UI sit in the root, compiled assemblies go into a folder per Revit year.

## Develop

```
dotnet build -c Release                      # builds for the year set in the .csproj
dotnet build -c Release -p:RevitVersion=2026 # or any other year
```

In Revit, press **AnalyseTool → Reload** to pick up the new build without restarting. If a command
does not appear, ask the plugin why rather than guessing: **Settings → Extensions** shows the load
state, and over MCP `GetExtensionDiagnostics` reports the compile error, an incompatible year or a
duplicate id — three failures that look identical from outside.

## Release

Packing produces one zip with every Revit year inside — that is what a user installs:

```
dotnet build -c Release -t:PackExtension     # -> artifacts\__ExtensionId__-<version>.zip
```

**To publish, push a tag.** The included workflow (`.github/workflows/ci.yml`) builds on every push,
and on a `v*` tag it additionally creates a GitHub Release with that zip attached:

```
git tag v1.0.0
git push --tags
```

Two rules the workflow enforces, both worth knowing before you tag:

- **The tag must match `version` in `plugin.json`** (`v1.0.0` ↔ `"version": "1.0.0"`). A mismatch
  fails the build on purpose: the zip name and the version behind `updateFeed` would disagree, and
  the update badge in AnalyseTool's Settings would be wrong from then on.
- **A push alone does not release anything.** It builds and leaves a downloadable artifact on the
  run; only a tag produces a Release. That is deliberate — `updateFeed` points at the latest
  release, so a release per commit would tell every user to update after every commit of yours.

Users install the zip through **Settings → Extensions → Install from file…**, or receive it as an
update if `plugin.json` declares an `updateFeed`.

## Writing more commands

`LLM.md` in this folder is the full authoring guide — the command contract, the manifest, the
`window.AT` bridge for pages, and the rules for changing any of them. It is written to be pasted
into an AI assistant, and it is the same text the plugin serves over MCP as `GetAuthoringGuide`.
