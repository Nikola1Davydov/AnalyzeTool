import { Button, Link, Spinner, Text, Tooltip } from "@fluentui/react-components";
import { Add16Regular, Dismiss12Regular, Open16Regular, Play16Regular } from "@fluentui/react-icons";
import { useState } from "react";
import { openWindow } from "../../api/app";
import { type ExtensionList, type ExtensionSummary, extensionCommands } from "../../api/extensions";
import { errorText, isCancelled, useNotify } from "../../components/notify";
import { Empty, Page } from "../../components/Page";
import { useAppQuery, useInvoke } from "../../revit/hooks";
import { StatusBadges, VersionBadge } from "../extensions/parts";

/** The dock pane: the user's extensions, their commands to run, and the way to the manager. */
export function DockPage() {
  const list = useAppQuery<ExtensionList>(extensionCommands.GetExtensions);
  const notify = useNotify();
  const open = (route: string) => openWindow(route).catch((error) => notify.failed(error, "Could not open the window"));
  const extensions = [...(list.data?.extensions ?? [])].sort((a, b) => a.name.localeCompare(b.name));

  return (
    <Page
      compact
      title="Extensions"
      actions={
        <>
          <Tooltip content="New button" relationship="label">
            <Button size="small" appearance="subtle" icon={<Add16Regular />} onClick={() => open("/extensions/new")} />
          </Tooltip>
          <Tooltip content="Manage extensions" relationship="label">
            <Button size="small" appearance="subtle" icon={<Open16Regular />} onClick={() => open("/extensions")} />
          </Tooltip>
        </>
      }
    >
      {list.isPending && <Spinner size="small" />}
      {list.isError && <Text className="error-text">{errorText(list.error)}</Text>}
      {list.isSuccess && !extensions.length && (
        <Empty>
          No extensions yet. <Link onClick={() => open("/extensions/new")}>Create a button</Link>.
        </Empty>
      )}
      <ul className="dock-list">
        {extensions.map((extension) => (
          <DockItem key={extension.id} extension={extension} onOpen={open} />
        ))}
      </ul>
      {extensions.length > 0 && (
        <Link className="small" onClick={() => open("/extensions")}>
          Manage extensions…
        </Link>
      )}
    </Page>
  );
}

type RunResult = { command: string; ok: boolean; text: string };

function DockItem({ extension, onOpen }: { extension: ExtensionSummary; onOpen: (route: string) => void }) {
  const invoke = useInvoke();
  const [running, setRunning] = useState<string | null>(null);
  const [result, setResult] = useState<RunResult | null>(null);

  async function run(command: string) {
    setRunning(command);
    setResult(null);
    try {
      const data = await invoke<unknown>(command, null);
      setResult({ command, ok: true, text: data === null || data === undefined ? "Done." : JSON.stringify(data, null, 2) });
    } catch (error) {
      if (!isCancelled(error)) setResult({ command, ok: false, text: errorText(error) });
    } finally {
      setRunning(null);
    }
  }

  return (
    <li className={extension.enabled ? "" : "disabled"}>
      <div className="dock-item-head">
        <Link className="strong" onClick={() => onOpen(`/extensions/${encodeURIComponent(extension.id)}`)}>
          {extension.name}
        </Link>
        <span className="badges">
          <StatusBadges extension={extension} />
          <VersionBadge extension={extension} />
        </span>
      </div>
      {extension.buttons.length > 1 && <div className="muted small">Buttons: {extension.buttons.join(", ")}</div>}
      {extension.enabled && extension.commands.length > 0 && (
        <div className="dock-commands">
          {extension.commands.map((command) => (
            <Button
              key={command}
              size="small"
              icon={running === command ? <Spinner size="extra-tiny" /> : <Play16Regular />}
              disabled={!!running}
              onClick={() => void run(command)}
              title={`Run ${command}`}
            >
              {command.startsWith(`${extension.id}.`) ? command.slice(extension.id.length + 1) : command}
            </Button>
          ))}
        </div>
      )}
      {result && (
        <div className={`dock-result ${result.ok ? "" : "failed"}`}>
          <button type="button" className="link-button dock-result-close" onClick={() => setResult(null)} aria-label="Close">
            <Dismiss12Regular />
          </button>
          <pre>{result.text}</pre>
        </div>
      )}
    </li>
  );
}
