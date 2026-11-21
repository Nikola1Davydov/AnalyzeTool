import type { ReactNode } from "react";
import { bridge } from "revit-bridge";
import { appCommands } from "../../api/app";
import { messageOf, useActions } from "./Actions";
import { links } from "./links";
import { OffRibbonMark } from "./Tile";

/** A row that opens a video (or the channel) in the browser: watching it is quicker than reading. */
export function WatchVideo({ url, label }: { url: string; label: string }) {
  const actions = useActions();
  return (
    <button className="guide-video" title={url} onClick={() => void bridge.invoke(appCommands.OpenUrl, { url }).catch((error: unknown) => actions.toast(messageOf(error), "error"))}>
      <span className="guide-video-play">▶</span>
      <span>{label}</span>
      <span className="chevron-right">›</span>
    </button>
  );
}

/** One step of the guide: a small picture made of the pane's own pieces, and what it means. */
export function Step({ picture, title, children }: { picture: ReactNode; title: string; children: ReactNode }) {
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
      <WatchVideo url={links.toolsVideo} label="Watch it on YouTube" />
      <Step picture={<span className="guide-folder" />} title="A folder is a tab of the Revit ribbon">
        Every folder here is a tab of its own name; AnalyseTool is the AnalyseTool tab. «New folder» makes a new tab. Inside a
        folder, its sections are the panels of that tab. On disk it is the same, as in pyRevit:{" "}
        <span className="mono">Sheets.tab\Numbering.panel\Renumber.pushbutton\</span> — one folder per button, with its code, page
        and versions. Folders of AnalyseTool 1.x (with a plugin.json) are together in «Older buttons»; ✎ › Move to… puts such a button
        into a tab.
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
      <Step
        picture={
          <span className="guide-tile">
            <OffRibbonMark />
          </span>
        }
        title="An eye struck through: not on the ribbon"
      >
        The button stays here and works — a tap runs it — but the Revit ribbon does not show it. A folder off the ribbon says
        so once, above its buttons; «+» or that line brings it back.
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

const Glyph = ({ children }: { children: ReactNode }) => <span className="guide-glyph">{children}</span>;

/** «How tests work» (the «?» on the Tests tab): what a folder, a suite and a test are, a run, its results and how to write one. */
export function HowTestsWork() {
  return (
    <div className="guide">
      <h1 className="large-title">How tests work</h1>
      <WatchVideo url={links.testsVideo} label="Watch it on YouTube" />
      <Step picture={<span className="guide-folder" />} title="A folder is a project">
        One folder for a project or a BEP, with as many suites as it needs. On disk its name ends in{" "}
        <span className="mono">.tests</span> — tests load only from such a folder — and it has its own versions, like a folder of buttons — put it on a shared drive and the whole team runs the same tests. «New folder» makes one with a first suite.
      </Step>
      <Step picture={<span className="guide-tile" />} title="A suite is a C# file of tests">
        Like unit tests, but for the model: «doors · Fire Rating · not empty», «every room is placed», «no duct crosses a beam». A
        tile opens its tests; ▶ Run folder runs every suite of the folder at once.
      </Step>
      <Step picture={<Glyph>▶</Glyph>} title="Run: nothing in the model changes">
        «Run suite» checks the open model with every test; «Run failed» only what failed last time. Whatever a test does
        is rolled back.
      </Step>
      <Step picture={<Glyph>✓ ✕</Glyph>} title="Passed, new failures, accepted, inconclusive">
        A failure is one element (or a pair) that breaks the rule. Red: new. Accepted: known and agreed — it stays visible but does
        not fail the test. Inconclusive: Revit could not tell. An error is a bug in the test itself, with its file and line.
      </Step>
      <Step picture={<Glyph>ⓘ 👁</Glyph>} title="See the failing elements">
        <b>ⓘ</b> what the test checks and the element's value. <b>👁</b> isolates it in the view you are in, <b>Selection box</b> puts a
        section box around it. <b>Highlight</b> draws in red where it fails — the clash itself when the test found one, else the element —
        without touching the model; «Clear highlights»
        removes it. On a test, the same buttons act on all its failures.
      </Step>
      <Step picture={<Glyph>🔧</Glyph>} title="Repair or accept">
        A test that knows the fix shows its button (🔧 «Set Fire Rating to EI30», for example) — one Ctrl+Z undoes it. «Accept» writes the failure into the baseline of this model,
        kept next to the tests, so the team sees the same.
      </Step>
      <Step picture={<span className="guide-tile add">+</span>} title="Writing tests: ask the AI">
        The easiest way: connect Claude Code, Codex or Cursor (profile › AI clients) and say what to check — «every door needs a
        Fire Rating», «walls of type X only on level 1». It writes the suite, runs it and shows you the result. Or open the
        folder (📁) and write C# yourself: the AGENTS.md there points to the guide; saving reloads the tests.
      </Step>
      <Step picture={<span className="guide-avatar">NI</span>} title="Statistics">
        Profile › Statistics › Model tests: how often each test ran, failed or broke.
      </Step>
    </div>
  );
}
