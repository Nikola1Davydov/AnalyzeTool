# Minecraft panel (UI-only extension)

A dockable block-building panel: pick a block from the palette, hit **Build** and place blocks by
clicking in the model (Esc finishes, Ctrl+Z removes the last block), or fill/clear whole regions.

This extension ships **no code** — it is a `plugin.json` + `index.html` pair. The actual block
commands (`McBuildInteractive`, `McFillRegion`, `McClearRegion`, `McPlaceBlocks`, `McSetPalette`)
are built into the AnalyseTool host, and the page calls them via `window.AT.invoke`. That also
makes it the minimal reference for a UI-only extension.

## Install

Copy this folder into the extensions root and reload:

```
%LOCALAPPDATA%\AnalyseTool\extensions\minecraft-panel\
├─ plugin.json
└─ index.html
```

Then click **Reload** on the AnalyseTool ribbon — a **Minecraft** button appears in the Extensions
panel. (Alternatively zip the folder and install it via Settings → Extensions → Install.)

## Notes

- Interactive building needs a work plane: use a floor plan view, or set a work plane in 3D.
- Region coordinates are Minecraft-style cells: integers, **y is up**, one cell = one block.
- Blocks are placed as instances of an auto-generated cube family with the material as an instance
  parameter; deleting via **Clear region** only ever removes blocks this tooling placed.
