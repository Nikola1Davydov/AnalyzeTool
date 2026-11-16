---
name: add-page
description: >
  Add a page of the React app and show it in a window or the dock pane.
  USE FOR: a new route in ClientApp, opening it from a ribbon button, the dock pane, calling commands from the page, working with the Vite dev server and mocks, links to external sites.
  DO NOT USE FOR: the command the page calls (add-command); the button that opens the page (add-ribbon-button).
---

# Add a page

The UI of the add-in is one React app, `source/AnalyseTool/ClientApp` (Vite, React, TanStack Query). A page is a
hash route of it; there is one window class for every page.

## Step 1: Add the route

`ClientApp/src/App.tsx` reads the route from `location.hash` (`useRoute`). Add a component and pick it by route:

```tsx
if (route === "/families") return <FamiliesPage />;
```

Use a route library of your choice when the pages grow; keep hash routes — the add-in opens pages as `#/route`.

## Step 2: Call commands from the page

Use the hooks of `ClientApp/src/revit/hooks.ts`, never `bridge.invoke` directly: they add the `Document` header, so a
call made with data of another document gets `document_changed`, and the page re-reads the active document.

```tsx
const document = useActiveDocument();                        // { key, title } or null
const families = useCommand<FamilyItem[]>("GetFamilies");    // reads, cached per document
const rename = useCommandMutation<void, RenameFamiliesRequest>("RenameFamilies");
```

`useCommandMutation` returns `mutate(payload)`, `progress` (`{ fraction, message }`), `cancel()` and the usual
TanStack Query state.

## Step 3: Show the page

From a ribbon button (see add-ribbon-button) — an entry in `Ribbon/analysetool/plugin.json`:

```json
{ "name": "Families", "command": "OpenWindow", "args": { "route": "/families" } }
```

Give the route its title and size in `AppWindows.Open` (`Infrastructure/App/AppCommands.cs`). From C#:
`Host.GetService<IWindows>().Open("/families", options => …)`.

* One window per route: a second `Open` of the same route brings the window to the front.
* The dock pane is one per add-in and registered in `Host.cs` (`AddWebView(..., w => w.DockPane(id, title, route))`);
  `IWindows.ShowDockPane("/route")` shows a route in it. Keep the pane id: Revit remembers where the user docked it.
* The windows are modeless and owned by the Revit window.

## Step 4: Develop without Revit

```
cd source/AnalyseTool/ClientApp
npm install
npm run dev
```

In a normal browser the page talks to the mocks of `ClientApp/src/mocks.ts` (`registerMocks`) — add an answer for every
command the page calls. In a Debug build inside Revit the windows load the same dev server (`ClientApp:DevServerUrl`,
`http://localhost:5173` by default) with hot reload; F12 opens DevTools. A Release build ships `ClientApp/dist` and loads
it from `https://app.analysetool.local`; the build pipeline runs `npm run build`.

## External links

Any page in the WebView can call commands. With the navigation protection on, only the add-in's own pages load: a
link to another address — also `target="_blank"` — opens in the system browser, and messages from another origin are
dropped. `AllowAnyNavigation()` in `AddWebView` turns the protection off; this template's `Host.cs` calls it — remove the
call to protect the add-in.

## Check

`npm run build` in `ClientApp` (type check and Vite); then Revit: the button opens the window, the page shows the active
document and its data, switching documents refreshes it.
