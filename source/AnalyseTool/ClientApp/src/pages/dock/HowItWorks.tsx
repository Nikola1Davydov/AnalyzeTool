import type { ReactNode } from "react";

/** One step of the guide: a small picture made of the pane's own pieces, and what it means. */
function Step({ picture, title, children }: { picture: ReactNode; title: string; children: ReactNode }) {
  return (
    <div className="guide-step">
      <div className="guide-picture">{picture}</div>
      <div>
        <strong>{title}</strong>
        <p className="muted small">{children}</p>
      </div>
    </div>
  );
}

const Badge = ({ className, children }: { className: string; children: ReactNode }) => <span className={`edit-badge guide-badge ${className}`}>{children}</span>;

/** «How it works» (the «?» on the folders): a page with pictures of the pane, the ribbon and the four corners of a tile. */
export function HowItWorks() {
  return (
    <div className="guide">
      <h1 className="large-title">How it works</h1>
      <Step picture={<span className="guide-folder" />} title="A folder is a tab of the Revit ribbon">
        Every folder here is a tab of its own name; AnalyseTool is the AnalyseTool tab. «New folder» makes a new tab. Inside a
        folder, its sections are the panels of that tab.
      </Step>
      <Step picture={<span className="guide-section">PANEL</span>} title="Sections are panels">
        Right-click to edit, then drag a button into another section — it moves to that panel of the ribbon. ✎ next to a section
        renames it, «+ New panel» adds one.
      </Step>
      <Step picture={<span className="guide-tile" />} title="A tap runs the button">
        The same as a click on the ribbon: its command runs in Revit, or its page opens. The result shows at the bottom; tap it for
        the whole text.
      </Step>
      <Step
        picture={
          <span className="guide-tile corners">
            <Badge className="remove">−</Badge>
            <Badge className="change">✎</Badge>
            <Badge className="open-folder">📁</Badge>
            <Badge className="star on">★</Badge>
          </span>
        }
        title="Right-click: the four corners"
      >
        <b>−/+</b> loads the button on the Revit ribbon or not (on a folder: its whole tab). <b>☆</b> keeps it in the dock at the
        bottom of this pane. <b>✎</b> its name, description and «Move to…» another folder. <b>📁</b> opens its folder — to edit
        it with an AI tool (Claude Code, Cursor). «Done» or Escape ends the editing.
      </Step>
      <Step picture={<span className="guide-tile off" />} title="Grey and struck through: not on the ribbon">
        The button (or the folder) stays here and works, but Revit does not show it. «+» brings it back.
      </Step>
      <Step
        picture={
          <span className="guide-tile">
            <span className="guide-error">!</span>
          </span>
        }
        title="A red «!»: something is wrong"
      >
        The code of that button does not compile — tap the «!» for its errors with file and line; the button keeps running its last
        good build meanwhile. A problem of the whole folder (a shared file, plugin.json) is a red bar inside the folder. An orange dot:
        the last run of the button failed — tap it for the error. ✎ › «Code, files and versions…» shows the line in the file; an
        AI client over MCP sees the same errors and can fix them. A button that fails when it runs shows a red message at the bottom
        (tap it for the whole text); Profile › Statistics keeps how often it failed and its last error.
      </Step>
      <Step picture={<span className="guide-tile dim" />} title="Pale: the folder is turned off">
        Its buttons and commands do not load (it was turned off — maybe in AnalyseTool 1.x). Tap a button of it, or the bar at the
        top of the folder, and «Turn it on»; ⋯ › «Turn the folder off» turns it off again.
      </Step>
      <Step picture={<span className="guide-tile pulldown" />} title="▾ is a pulldown">
        Several buttons behind one: on the ribbon a button with a list; here a tap shows the list.
      </Step>
      <Step picture={<span className="guide-dock" />} title="The dock: your quick access">
        The buttons you starred, from any folder, always at the bottom of the pane.
      </Step>
      <Step picture={<span className="guide-tile add">+</span>} title="A new button is C# you can read">
        «New button» in a section writes a page and its C# command, already working. Edit them in any editor or let your AI do it
        (📁 opens the folder; AGENTS.md there tells the AI where the guide is) — saving reloads the button. Every change is a
        version: ✎ › «Code, files and versions…» shows, compares and restores them.
      </Step>
      <Step picture={<span className="guide-avatar">NI</span>} title="Your profile">
        Top right: statistics of your buttons, packages (.zip extensions others shared), AI clients (what to paste into Claude Code,
        Codex, Cursor…), the folders of buttons, report a bug, about, tile size and theme.
      </Step>
    </div>
  );
}
