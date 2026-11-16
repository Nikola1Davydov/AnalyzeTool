import { useQuery } from "@tanstack/react-query";
import { useMemo } from "react";
import { bridge } from "revit-bridge";
import { type AppInfo, appCommands, openPath } from "../../api/app";
import { Markdown } from "../../components/Markdown";
import { messageOf, useActions } from "./Actions";

const REPO_URL = "https://github.com/Nikola1Davydov/AnalyzeTool";

type Release = { title: string; body: string };

/** CHANGELOG.md split at its «## » headings: one fold per release. */
function splitReleases(markdown: string): Release[] {
  return markdown
    .split(/^## /m)
    .slice(1)
    .map((part) => {
      const newline = part.indexOf("\n");
      return { title: (newline < 0 ? part : part.slice(0, newline)).trim(), body: newline < 0 ? "" : part.slice(newline + 1) };
    });
}

/** About, in the profile of the pane: the version, where things live, the links, and what is new. */
export function AboutScreen() {
  const actions = useActions();
  const info = useQuery({ queryKey: ["appInfo"], queryFn: ({ signal }) => bridge.invoke<AppInfo>(appCommands.GetAppInfo, null, { signal }) });
  const releases = useMemo(() => splitReleases(info.data?.changelog ?? ""), [info.data?.changelog]);
  const open = (url: string) => void bridge.invoke(appCommands.OpenUrl, { url }).catch((error: unknown) => actions.toast(messageOf(error), "error"));

  return (
    <div className="profile about">
      <section className="profile-card">
        <span className="avatar large about-logo">AT</span>
        <div>
          <strong>AnalyseTool {info.data?.version}</strong>
          <div className="muted small">for Revit {info.data?.revitVersion}</div>
          <div className="muted small">Local and free. No account, nothing leaves this computer.</div>
        </div>
      </section>

      {info.isError && <p className="error small">{info.error.message}</p>}

      <section className="link-section">
        <button className="row-link" onClick={() => open(REPO_URL)}>
          <span>🌐 Source on GitHub</span>
          <span className="chevron-right">›</span>
        </button>
        <button className="row-link" onClick={() => open(`${REPO_URL}/issues`)}>
          <span>🐞 Report a bug or an idea</span>
          <span className="chevron-right">›</span>
        </button>
        {info.data && (
          <button className="row-link" onClick={() => void openPath(info.data.profileFolder).catch((error: unknown) => actions.toast(messageOf(error), "error"))}>
            <span>📂 Profile folder</span>
            <span className="chevron-right">›</span>
          </button>
        )}
        <p className="muted small">The profile folder holds your folders of buttons, the logs, the statistics and this pane's settings.</p>
      </section>

      <section>
        <h2>What's new</h2>
        {info.isPending && <p className="muted small">Loading…</p>}
        {releases.map((release, index) => (
          <details key={release.title} className="advanced release" open={index === 0}>
            <summary>{release.title.replace(/^\[([^\]]+)\]\s*\/?\s*/, "$1 · ")}</summary>
            <Markdown text={release.body} />
          </details>
        ))}
      </section>
    </div>
  );
}
