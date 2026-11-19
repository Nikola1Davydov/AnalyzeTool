import { Button, Spinner, Text } from "@fluentui/react-components";
import { Document16Regular, FolderOpen16Regular, Open16Regular } from "@fluentui/react-icons";
import { joinPath, openPath } from "../../api/app";
import {
  type ExtensionFile,
  type ExtensionFileContent,
  type ExtensionSummary,
  extensionCommands,
} from "../../api/extensions";
import { CodeView } from "../../components/CodeView";
import { fileSize, relativeTime } from "../../components/format";
import { errorText, useNotify } from "../../components/notify";
import { Empty } from "../../components/Page";
import { useAppQuery } from "../../revit/hooks";
import { DiagnosticsList } from "./parts";

const BINARY = /\.(dll|pdb|exe|png|jpe?g|gif|ico|bmp|zip|rvt|rfa|woff2?|ttf)$/i;

const samePath = (a: string, b: string) => a.replace(/\\/g, "/").toLowerCase() === b.replace(/\\/g, "/").toLowerCase();

type FilesTabProps = {
  extension: ExtensionSummary;
  target: { file: string; line?: number } | null;
  onTarget: (target: { file: string; line?: number } | null) => void;
};

/** The files of an extension, read-only, with the compiler messages on their lines. Editing happens in the user's editor. */
export function FilesTab({ extension, target, onTarget }: FilesTabProps) {
  const notify = useNotify();
  const files = useAppQuery<ExtensionFile[]>(extensionCommands.ListExtensionFiles, { id: extension.id });
  const sorted = [...(files.data ?? [])].sort((a, b) => a.path.localeCompare(b.path));
  const current =
    (target && sorted.find((file) => samePath(file.path, target.file))) ??
    sorted.find((file) => /^src[\\/].*\.cs$/i.test(file.path)) ??
    sorted[0];
  const binary = !!current && BINARY.test(current.path);
  const content = useAppQuery<ExtensionFileContent>(
    extensionCommands.ReadExtensionFile,
    { id: extension.id, path: current?.path ?? "" },
    { enabled: !!current && !binary },
  );
  const fileDiagnostics = current ? extension.diagnostics.filter((d) => samePath(d.file, current.path)) : [];
  const open = (path: string) => openPath(path).catch((error) => notify.failed(error, "Could not open"));

  if (files.isPending) return <Spinner label="Listing files…" />;
  if (files.isError) return <Text className="error-text">{errorText(files.error)}</Text>;
  if (!sorted.length) return <Empty>The folder has no files.</Empty>;

  return (
    <div className="files-layout">
      <ul className="file-list">
        {sorted.map((file) => {
          const marks = extension.diagnostics.filter((d) => samePath(d.file, file.path));
          return (
            <li key={file.path}>
              <button
                type="button"
                className={current && samePath(current.path, file.path) ? "selected" : ""}
                onClick={() => onTarget({ file: file.path })}
                title={`${fileSize(file.size)}, changed ${relativeTime(file.modified)}`}
              >
                <Document16Regular />
                <span className="mono">{file.path}</span>
                {marks.length > 0 && (
                  <span className={marks.some((d) => d.severity === "error") ? "dot error" : "dot warning"} />
                )}
              </button>
            </li>
          );
        })}
      </ul>

      <section className="file-view">
        {current && (
          <header className="file-view-header">
            <span className="mono strong">{current.path}</span>
            <span className="muted small">
              {fileSize(current.size)} · changed {relativeTime(current.modified)}
            </span>
            <span className="spacer" />
            <Button size="small" icon={<Open16Regular />} onClick={() => open(joinPath(extension.directory, current.path))}>
              Open in editor
            </Button>
            <Button size="small" icon={<FolderOpen16Regular />} onClick={() => open(extension.directory)}>
              Open folder
            </Button>
          </header>
        )}
        {extension.diagnostics.length > 0 && (
          <DiagnosticsList diagnostics={extension.diagnostics} onOpen={(d) => onTarget({ file: d.file, line: d.line })} />
        )}
        {binary && <Empty>A binary file — open it with its program.</Empty>}
        {!binary && content.isPending && <Spinner size="small" />}
        {!binary && content.isError && <Text className="error-text">{errorText(content.error)}</Text>}
        {!binary && content.data && (
          <CodeView
            text={content.data.content}
            line={target && current && samePath(target.file, current.path) ? target.line : undefined}
            diagnostics={fileDiagnostics}
          />
        )}
      </section>
    </div>
  );
}
