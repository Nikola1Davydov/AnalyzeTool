import { Button, Link, Spinner, Text } from "@fluentui/react-components";
import { ChevronRight16Regular, FolderOpen16Regular } from "@fluentui/react-icons";
import { useMemo } from "react";
import { type AppInfo, appCommands, openPath } from "../../api/app";
import { Markdown } from "../../components/Markdown";
import { errorText, useNotify } from "../../components/notify";
import { Page } from "../../components/Page";
import { useAppQuery } from "../../revit/hooks";

const REPO_URL = "https://github.com/Nikola1Davydov/AnalyzeTool";

type Release = { title: string; body: string };

/** CHANGELOG.md split at its «## » headings: one fold per release. */
function splitReleases(markdown: string): { intro: string; releases: Release[] } {
  const parts = markdown.split(/^## /m);
  const intro = parts.shift() ?? "";
  return {
    intro: intro.replace(/^# .*$/m, "").trim(),
    releases: parts.map((part) => {
      const newline = part.indexOf("\n");
      return {
        title: (newline < 0 ? part : part.slice(0, newline)).trim(),
        body: newline < 0 ? "" : part.slice(newline + 1),
      };
    }),
  };
}

export function AboutPage() {
  const info = useAppQuery<AppInfo>(appCommands.GetAppInfo);
  const notify = useNotify();
  const changelog = useMemo(() => splitReleases(info.data?.changelog ?? ""), [info.data?.changelog]);

  return (
    <Page title="About AnalyseTool" narrow>
      <Text className="muted block-text">
        An open-source Revit add-in: analyse and edit parameters, manage shared parameters, and add your own buttons in C# —
        or let your AI client write them over MCP.
      </Text>

      {info.isPending && <Spinner />}
      {info.isError && <Text className="error-text">{errorText(info.error)}</Text>}

      {info.data && (
        <section className="panel block">
          <div className="key-values">
            <span className="muted">Version</span>
            <span>{info.data.version}</span>
            <span className="muted">Revit</span>
            <span>{info.data.revitVersion}</span>
            <span className="muted">Profile folder</span>
            <span>
              <Link className="mono small" onClick={() => openPath(info.data.profileFolder).catch((e) => notify.failed(e))}>
                {info.data.profileFolder}
              </Link>
            </span>
          </div>
          <div className="link-row">
            <Link href={REPO_URL} target="_blank" rel="noopener noreferrer">
              Source code
            </Link>
            <Link href={`${REPO_URL}/issues`} target="_blank" rel="noopener noreferrer">
              Report a bug or an idea
            </Link>
            <Link href={`${REPO_URL}/releases`} target="_blank" rel="noopener noreferrer">
              Releases
            </Link>
            <Button
              size="small"
              appearance="subtle"
              icon={<FolderOpen16Regular />}
              onClick={() => openPath(info.data.profileFolder).catch((e) => notify.failed(e))}
            >
              Open profile folder
            </Button>
          </div>
        </section>
      )}

      {info.data && (
        <section className="block">
          <Text as="h2" size={500} weight="semibold">
            What's new
          </Text>
          {changelog.intro && <Markdown text={changelog.intro} />}
          {changelog.releases.map((release, index) => (
            <details key={release.title} className="release" open={index === 0}>
              <summary>
                <ChevronRight16Regular className="release-chevron" />
                <Markdown text={release.title} className="inline" />
              </summary>
              <Markdown text={release.body} />
            </details>
          ))}
          {!changelog.releases.length && !changelog.intro && <Text className="muted">No changelog.</Text>}
        </section>
      )}
    </Page>
  );
}
