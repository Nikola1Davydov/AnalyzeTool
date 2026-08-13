# AnalyseTool UI — staged for extraction

This folder is **transitional**. It holds the product frontend (parameter analysis, family
management, QC) after it was lifted out of the framework shell in `src/clientapp`. It is meant to be
moved into its own repository and shipped as an installable extension; nothing in the plugin build
references it.

It is a complete, self-contained Vite application:

```bash
npm install
npm run build     # verified green
npm run dev       # serves on :22525 — point plugin.json's ui.devUrl here for HMR inside Revit
```

## How it talks to Revit

Only through `window.AT.invoke`, which the host injects into every extension page before the page's
own scripts run. There is no import from the shell, and no shared build. `src/RevitBridge.ts` wraps
that global and adds two things the injected bridge does not provide: a clear error when the page is
opened outside the host, and re-dispatch of host broadcasts as `at:<Command>` DOM events.

## Moving it out

The folder content is already the shape of a repository root (`package.json`, `vite.config.js`,
`index.html`, `plugin.json`, `src/`), so it transplants as-is. Git recorded every file as a rename,
so `git log --follow` still reaches the original history.

The C# side (`src/AnalyseTool.Tools`: `Actions/`, `Ai/`, `Elements/`, `Families/`) has not moved yet
and belongs in the same repository — it already compiles against the SDK alone.

## Known gaps before this can actually be installed

Three framework-side limitations block a faithful port. None of them are bugs in this folder; all
three are tracked on the framework side:

1. **One ribbon surface per extension.** `plugin.json` allows a single `ui.button` and a single
   `dockable` flag, but this product needs three entry points: the analyser window, the Family
   Control window, and the dockable palette (`#/families-dock`). Only the analyser window is
   declared in `plugin.json` today.

2. **Event broadcasts.** The host's injected bridge ignores messages of `Type: "Event"`, so
   `at:DocumentChanged` and `at:QueueChanged` never fire on an extension page. `src/RevitBridge.ts`
   works around it locally by attaching its own listener; the framework should do it for everyone.

3. **AI model selection crosses an origin boundary.** `useAiSettingsStore` shares the selected model
   between windows through `localStorage`, which worked only because every window was served from
   the same virtual host. The shell is served from `https://app/` and extensions from
   `https://ext-<id>/` — different origins, separate storage. The selection needs to move to host
   commands. Everything else in this app that uses `localStorage` (family rules, library paths,
   naming rules, palette settings, canvas persistence) stays within this one origin and is fine.

## What was changed during the lift

- `src/RevitBridge.ts` — rewritten to delegate to the host-injected bridge; the command map keeps
  only the commands this product calls.
- `src/App.vue`, `src/layout/FooterLayout.vue` — dropped the framework's update-check wiring. The
  footer now shows this extension's own version, read straight from `plugin.json`; updates are the
  host Extension Manager's job via `updateFeed`.
- `src/layout/Sidebar.vue` — removed the shell's About entry.
- `src/router/index.js` — product routes only.
- `src/view/Families/BulkRenameDialog.vue` — a raw NUL byte used as a key separator was replaced with
  a `\0` escape. Same behaviour, but git, grep and `file` no longer treat the source as binary.
- `src/stores/useNotificationStore.ts`, `useAiSettingsStore.ts`, `src/components/RevitBusyBar.vue`,
  `src/layout/*`, `src/main.js`, `src/assets/main.css` — copied, not moved: the shell still needs its
  own versions. These are the files to watch for drift until the split is final.
