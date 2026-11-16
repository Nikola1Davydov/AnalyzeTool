import { motion } from "motion/react";
import { useId, useRef, useState } from "react";
import { bridge } from "revit-bridge";
import { appCommands } from "../../api/app";
import type { PaneOverview } from "../../api/pane";
import { messageOf, useActions } from "./Actions";
import {
  defaultProfile,
  exportProfile,
  findButton,
  missingFolders,
  normalize,
  type Theme,
  type TileSize,
  useProfile,
  withoutMissingFolders,
} from "./profile";
import { spring, TileIcon } from "./Tile";

/** The segmented control of iOS: the selected option sits on a thumb that slides to the next choice. */
function Segmented<T extends string>(props: { value: T; options: { value: T; label: string }[]; onChange: (value: T) => void }) {
  const id = useId();
  return (
    <div className="segmented" role="radiogroup">
      {props.options.map((option) => (
        <button
          key={option.value}
          role="radio"
          aria-checked={props.value === option.value}
          className={props.value === option.value ? "on" : ""}
          onClick={() => props.onChange(option.value)}
        >
          {props.value === option.value && <motion.span layoutId={`thumb-${id}`} className="segmented-thumb" transition={spring} />}
          <span className="segmented-label">{option.label}</span>
        </button>
      ))}
    </div>
  );
}

/** The switch of iOS. */
function Switch({ on, onChange, label }: { on: boolean; onChange: (on: boolean) => void; label: string }) {
  return (
    <label className="switch-row">
      <span>{label}</span>
      <button role="switch" aria-checked={on} className={`switch ${on ? "on" : ""}`} onClick={() => onChange(!on)}>
        <motion.span className="switch-knob" layout transition={spring} />
      </button>
    </label>
  );
}

const sizeOptions: { value: TileSize; label: string }[] = [
  { value: "small", label: "Small" },
  { value: "medium", label: "Medium" },
  { value: "large", label: "Large" },
];

const themeOptions: { value: Theme; label: string }[] = [
  { value: "revit", label: "Revit" },
  { value: "system", label: "Windows" },
  { value: "light", label: "Light" },
  { value: "dark", label: "Dark" },
];

/** The profile: every setting of the pane in one place, kept in the AnalyseTool profile folder; export and import move it. */
export function ProfileScreen(props: { overview: PaneOverview; onStats: () => void; onPackages: () => void; onMcp: () => void; onFolders: () => void }) {
  const { overview, onStats, onPackages } = props;
  const { profile, update } = useProfile();
  const actions = useActions();
  const [importError, setImportError] = useState<string | null>(null);
  const file = useRef<HTMLInputElement | null>(null);

  const dock = profile.dock.map((key) => ({ key, found: findButton(overview.folders, key) }));
  const moveIn = (list: "dock" | "ribbon") => (index: number, delta: number) =>
    update((current) => {
      const items = [...current[list]];
      const [item] = items.splice(index, 1);
      items.splice(Math.max(0, Math.min(items.length, index + delta)), 0, item);
      return { ...current, [list]: items };
    });
  const moveDock = moveIn("dock");

  const importFile = async (selected: File | undefined) => {
    if (!selected) return;
    try {
      const imported = normalize(JSON.parse(await selected.text()));
      update(() => imported);
      setImportError(null);
    } catch (error) {
      setImportError(`The file is not a profile: ${messageOf(error)}`);
    }
  };

  const openWindow = (route: string) =>
    void bridge.invoke(appCommands.OpenWindow, { route }).catch((error: unknown) => actions.toast(messageOf(error), "error"));

  const customized = Object.keys(profile.order).length;
  const missing = missingFolders(profile, overview.folders);

  return (
    <div className="profile">
      <section className="profile-card">
        <span className="avatar large">{overview.userName.slice(0, 2).toUpperCase()}</span>
        <div>
          <strong>{overview.userName}</strong>
          <div className="muted small">on {overview.machineName}</div>
          <div className="muted small">
            Saved {profile.updatedAt === defaultProfile.updatedAt ? "— not changed yet" : new Date(profile.updatedAt).toLocaleString()}
          </div>
        </div>
      </section>

      <section className="link-section">
        <button className="row-link" onClick={onStats}>
          <span>📊 Statistics</span>
          <span className="chevron-right">›</span>
        </button>
        <p className="muted small">How often every button is clicked, how long it runs and how often it fails — on this computer.</p>
      </section>

      <section className="link-section">
        <button className="row-link" onClick={onPackages}>
          <span>📦 Packages</span>
          <span className="chevron-right">›</span>
        </button>
        <p className="muted small">Extensions someone shared as a .zip: install, update, turn off, remove.</p>
      </section>

      <section className="link-section">
        <button className="row-link" onClick={props.onMcp}>
          <span>🤖 AI clients (MCP)</span>
          <span className="chevron-right">›</span>
        </button>
        <p className="muted small">Let Claude Code, Codex, Cursor and others use AnalyseTool and your buttons — what to paste where.</p>
      </section>

      <section className="link-section">
        <button className="row-link" onClick={props.onFolders}>
          <span>📂 Folders of buttons</span>
          <span className="chevron-right">›</span>
        </button>
        <p className="muted small">Where your folders live: yours, a network share or a git checkout shared with your team.</p>
      </section>

      <section>
        <h2>Buttons</h2>
        <Segmented value={profile.size} options={sizeOptions} onChange={(size) => update((current) => ({ ...current, size }))} />
        <p className="muted small">The size of the tiles. The number of columns follows the width of the pane.</p>
      </section>

      <section>
        <h2>Theme</h2>
        <Segmented value={profile.theme} options={themeOptions} onChange={(theme) => update((current) => ({ ...current, theme }))} />
      </section>

      <section>
        <Switch label="Animations and effects" on={profile.effects} onChange={(effects) => update((current) => ({ ...current, effects }))} />
        <p className="muted small">Turn off on a computer without a graphics card (a virtual machine, a remote desktop).</p>
      </section>

      <section>
        <h2>Dock</h2>
        {dock.length === 0 && <p className="muted small">No favourites yet. Right-click a button and tap ☆ to put it in the dock.</p>}
        <ul className="dock-list">
          {dock.map(({ key, found }, index) => (
            <li key={key}>
              {found ? (
                <TileIcon glyph={found.button.glyph} image={found.button.image} title={found.button.name} />
              ) : (
                <span className="tile-icon letters gone">?</span>
              )}
              <span className="dock-name">
                {found?.button.name ?? key}
                <span className="muted small"> · {found?.folder.name ?? "gone"}</span>
              </span>
              <button className="icon-button" title="Up" disabled={index === 0} onClick={() => moveDock(index, -1)}>
                ↑
              </button>
              <button className="icon-button" title="Down" disabled={index === dock.length - 1} onClick={() => moveDock(index, 1)}>
                ↓
              </button>
              <button
                className="icon-button"
                title="Remove from the dock"
                onClick={() => update((current) => ({ ...current, dock: current.dock.filter((item) => item !== key) }))}
              >
                ✕
              </button>
            </li>
          ))}
        </ul>
        {missing.length > 0 && (
          <>
            <p className="muted small">Some favourites belong to extensions that are not loaded now — removed, renamed or on a folder that is offline.</p>
            <button onClick={() => update((current) => withoutMissingFolders(current, overview.folders))}>
              Remove {missing.length} unavailable
            </button>
          </>
        )}
      </section>

      <section>
        <h2>Order of the buttons</h2>
        <p className="muted small">
          {customized === 0 ? "The order of the ribbon. Right-click a button and drag it to change it." : `Your own order in ${customized} place(s).`}
        </p>
        <button disabled={customized === 0} onClick={() => update((current) => ({ ...current, order: {} }))}>
          Reset the order
        </button>
      </section>

      <section>
        <h2>Profile</h2>
        <p className="muted small">
          Kept in <span className="mono">{overview.profileFile}</span> — the same in every Revit version on this computer.
        </p>
        <div className="toolbar">
          <button onClick={() => exportProfile(profile, overview.userName)}>Export</button>
          <button onClick={() => file.current?.click()}>Import</button>
          <button
            onClick={() => {
              if (confirm("Reset the profile? The dock, the order and the settings of the pane go back to the defaults.")) update(() => defaultProfile);
            }}
          >
            Reset
          </button>
        </div>
        <input ref={file} type="file" accept="application/json,.json" hidden onChange={(event) => void importFile(event.target.files?.[0])} />
        {importError && <p className="error small">{importError}</p>}
      </section>
    </div>
  );
}
