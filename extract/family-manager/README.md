# Family Manager — staged for extraction

This folder is **transitional**. It holds the Family Manager frontend after it was lifted out of the
main AnalyseTool client in `src/clientapp`. It is meant to move into its own repository and ship as
an installable extension; nothing in the plugin build references it.

AnalyseTool itself — parameter analysis, the canvas, the parameter checks — stays in the main
repository and keeps its name. Family Manager leaves as the first real extension, which also makes it
the honest test of whether the extension mechanism carries a non-trivial feature.

It is a complete, self-contained Vite application:

```bash
npm install
npm run build     # verified green
npm run dev       # serves on :22525 — point plugin.json's ui.devUrl here for HMR inside Revit
```

## How it talks to Revit

Only through `window.AT.invoke`, which the host injects into every extension page before the page's
own scripts run. There is no import from the main client, and no shared build. `src/RevitBridge.ts`
wraps that global and adds two things the injected bridge does not provide: a clear error when the
page is opened outside the host, and re-dispatch of host broadcasts as `at:<Command>` DOM events.

Its command map is grouped by ownership: commands this extension ships (family operations, AI naming
prompts) versus host services it merely consumes (selection, isolation, folder picker, AI provider
registry). The second group is the contract with the host — it must keep working from an extension
origin.

## Moving it out

The folder content is already the shape of a repository root (`package.json`, `vite.config.js`,
`index.html`, `plugin.json`, `src/`), so it transplants as-is. Git recorded every file as a rename,
so `git log --follow` still reaches the original history.

The C# side has not moved yet: `src/AnalyseTool.Tools/Families/` plus three AI naming commands
(`Ai/Features/OllamaSuggestName.cs`, `OllamaSuggestNames.cs`, `OllamaSuggestTemplate.cs` — nothing
else calls them) belong in the same repository. `OllamaAnalyse` and `OllamaEditParameters` stay
behind: they are the analyser's.

## Known gaps before this can actually be installed

Three host-side limitations block a faithful port. None of them are bugs in this folder:

1. **One ribbon surface per extension.** `plugin.json` allows a single `ui.button` and a single
   `dockable` flag, but this extension has two surfaces: the family browser window (`#/families`)
   and the dockable placement palette (`#/families-dock`). Only the browser is declared today.

2. **Event broadcasts.** The host's injected bridge ignores messages of `Type: "Event"`, so
   `at:DocumentChanged` and `at:QueueChanged` never fire on an extension page — `FamilyPaletteView`
   and `RevitBusyBar` both depend on them. `src/RevitBridge.ts` works around it locally by attaching
   its own listener; the host should do it for every extension.

3. **AI model selection crosses an origin boundary.** `useAiSettingsStore` shares the selected model
   between windows through `localStorage`, which worked only because every window was served from the
   same virtual host. The main client is served from `https://app/` and extensions from
   `https://ext-<id>/` — different origins, separate storage. The selection needs to move to host
   commands. Everything else here that uses `localStorage` (family rules, library paths, naming
   rules, palette settings) stays within this one origin and is fine.

## What was changed during the lift

- `src/RevitBridge.ts` — rewritten to delegate to the host-injected bridge; command map grouped by
  ownership.
- `src/App.vue` — reduced to Toast + busy strip + `router-view`. Both screens are chrome-less (the
  host window is the frame), so the copied header/sidebar/footer were deleted outright.
- `src/router/index.js` — the two family routes only.
- `src/main.js` — registers only the PrimeVue components these templates use. An extension inherits
  nothing from the host: it is a separate SPA on its own origin and bundles its own PrimeVue.
- `src/view/Families/BulkRenameDialog.vue` — a raw NUL byte used as a key separator was replaced with
  a `\0` escape. Same behaviour, but git, grep and `file` no longer treat the source as binary.
- `src/stores/useNotificationStore.ts`, `useAiSettingsStore.ts`, `src/components/RevitBusyBar.vue`,
  `AiModelIndicator.vue`, `src/main.js`, `src/assets/main.css` — copied, not moved: the main client
  still needs its own versions. These are the files to watch for drift until the split is final.
