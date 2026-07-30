# Minecraft × Revit (UI-only extension)

A first-person voxel game in an AnalyseTool window — with the Revit model as the save file.
Walk around, place and break blocks; **every change is mirrored into the Revit model live**
(placed blocks become family instances, broken blocks are deleted). Reopening the game loads the
world back from the model via `McGetBlocks`.

Rendering is three.js (vendored, MIT — see `three.LICENSE`) with procedurally generated
pixel-art textures: no copyrighted game assets. Want nicer ones? Drop your own 16×16 PNGs into a
`textures/` folder next to `index.html` (`textures/grass.png`, `textures/stone.png`, …) — a file
beats the procedural texture for that block.

## Controls

| Input | Action |
| --- | --- |
| Click | capture the mouse / play |
| WASD, Shift | move, sprint |
| Space | jump |
| F | toggle fly mode (Space up, C down) |
| Left / right click | break / place a block |
| 1–9 or click hotbar | choose block |
| Esc | pause (release mouse) |

## Your own cube family

In the settings box (top right) enter the name of a loaded cube family — e.g. `McCube` — and the
game places THAT family instead of the auto-generated `MC_Block`. Per block type the family gets a
duplicated TYPE (named `grass`, `stone`, …) whose material parameter is set to the matching `MC_*`
material — so your family's own Material parameter drives the look in Revit. The family should be
one block in size (default 1 m cube, origin at the bottom center — the Generic Model convention).
Leave the field empty to use the auto-generated cube family.

## Install

Copy this folder into the extensions root and reload:

```
%LOCALAPPDATA%\AnalyseTool\extensions\minecraft-panel\
├─ plugin.json
├─ index.html
├─ three.module.min.js
└─ three.LICENSE
```

Then click **Reload** on the AnalyseTool ribbon — a **Minecraft** button appears in the Extensions
panel. (Alternatively zip the folder and install it via Settings → Extensions → Install.)

## Notes

- This extension ships **no C# code** — the block commands (`McPlaceBlocks`, `McClearRegion`,
  `McGetBlocks`, `McFillRegion`, `McSetPalette`) are built into the AnalyseTool host and the page
  calls them via `window.AT.invoke`. It doubles as a reference for UI-only extensions.
- Breaking a block only ever deletes elements this tooling placed (tagged `MC:` in Comments) —
  the rest of the model is never touched.
- Game coordinates are Minecraft-style cells: integers, y is up, one cell = one block
  (block size configurable in the settings box; keep it consistent per model).
