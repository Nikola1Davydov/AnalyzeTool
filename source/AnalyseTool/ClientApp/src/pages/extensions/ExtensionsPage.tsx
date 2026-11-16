import {
  Button,
  Link,
  Menu,
  MenuItem,
  MenuList,
  MenuPopover,
  MenuTrigger,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  Spinner,
  Switch,
  Table,
  TableBody,
  TableCell,
  TableHeader,
  TableHeaderCell,
  TableRow,
  Tooltip,
} from "@fluentui/react-components";
import {
  Add16Regular,
  ArrowClockwise16Regular,
  ArrowDownload16Regular,
  Delete16Regular,
  FolderOpen16Regular,
  MoreHorizontal16Regular,
  Open16Regular,
  Settings16Regular,
} from "@fluentui/react-icons";
import { type ExtensionList, type ExtensionSummary, extensionCommands } from "../../api/extensions";
import { browseFile, openPath, openWindow } from "../../api/app";
import { plural } from "../../components/format";
import { errorText, useNotify } from "../../components/notify";
import { Empty, Page } from "../../components/Page";
import { useAppQuery } from "../../revit/hooks";
import { href, navigate } from "../../router";
import { useExtensionActions, useInstallPackage } from "./actions";
import { DiagnosticsList, KindBadge, StatusBadges, VersionBadge } from "./parts";

/** The extension manager: every extension with its kind, state, problems and versions. */
export function ExtensionsPage() {
  const list = useAppQuery<ExtensionList>(extensionCommands.GetExtensions);
  const actions = useExtensionActions();
  const notify = useNotify();
  const install = useInstallPackage();
  const extensions = [...(list.data?.extensions ?? [])].sort(
    (a, b) => (a.zone === b.zone ? a.name.localeCompare(b.name) : a.zone === "Dev" ? -1 : 1),
  );
  const broken = Object.entries(list.data?.brokenFolders ?? {});

  return (
    <Page
      title="Extensions"
      subtitle={
        <>
          Your own ribbon buttons — C# commands and pages, each in a folder with its own history.
          {list.data && <> Loaded for Revit {list.data.revitVersion}.</>}
        </>
      }
      actions={
        <>
          <Button appearance="primary" icon={<Add16Regular />} onClick={() => navigate("/extensions/new")}>
            New button
          </Button>
          <Button
            icon={<ArrowDownload16Regular />}
            disabled={install.busy}
            onClick={async () => {
              try {
                const path = await browseFile({ title: "Install extension package", filter: "Extension package|*.zip" });
                if (path) await install.run(path);
              } catch (error) {
                notify.failed(error);
              }
            }}
          >
            Install package…
          </Button>
          <Button
            icon={<ArrowClockwise16Regular />}
            disabled={actions.reload.isPending}
            onClick={() => actions.reload.mutate({})}
          >
            Reload all
          </Button>
          <Tooltip content="Folders of extensions" relationship="label">
            <Button
              icon={<Settings16Regular />}
              appearance="subtle"
              onClick={() => openWindow("/settings").catch((error) => notify.failed(error))}
            />
          </Tooltip>
        </>
      }
    >
      {list.isPending && <Spinner label="Loading extensions…" />}
      {list.isError && (
        <MessageBar intent="error">
          <MessageBarBody>{errorText(list.error)}</MessageBarBody>
        </MessageBar>
      )}

      {broken.length > 0 && (
        <MessageBar intent="warning" layout="multiline" className="block">
          <MessageBarBody>
            <MessageBarTitle>{plural(broken.length, "folder")} could not be read</MessageBarTitle>
            <ul className="plain-list">
              {broken.map(([folder, reason]) => (
                <li key={folder}>
                  <Link onClick={() => openPath(folder).catch((error) => notify.failed(error))}>{folder}</Link> — {reason}
                </li>
              ))}
            </ul>
          </MessageBarBody>
        </MessageBar>
      )}

      {list.isSuccess && !extensions.length && (
        <Empty>
          No extensions yet. <Link href={href("/extensions/new")}>Create your first button</Link> — AnalyseTool writes the C#
          for you to edit, and keeps every saved version.
        </Empty>
      )}

      {extensions.length > 0 && (
        <div className="table-scroll">
          <Table size="small" className="extensions-table">
            <TableHeader>
              <TableRow>
                <TableHeaderCell>Extension</TableHeaderCell>
                <TableHeaderCell className="col-kind">Kind</TableHeaderCell>
                <TableHeaderCell>Status</TableHeaderCell>
                <TableHeaderCell className="col-commands">Buttons & commands</TableHeaderCell>
                <TableHeaderCell className="col-switch">On</TableHeaderCell>
                <TableHeaderCell className="col-actions" />
              </TableRow>
            </TableHeader>
            <TableBody>
              {extensions.map((extension) => (
                <ExtensionRows key={extension.id} extension={extension} actions={actions} />
              ))}
            </TableBody>
          </Table>
        </div>
      )}
    </Page>
  );
}

function ExtensionRows({
  extension,
  actions,
}: {
  extension: ExtensionSummary;
  actions: ReturnType<typeof useExtensionActions>;
}) {
  const notify = useNotify();
  const detail = `/extensions/${encodeURIComponent(extension.id)}`;
  const hasProblems = extension.errors.length > 0 || extension.diagnostics.some((d) => d.severity === "error");

  return (
    <>
      <TableRow className={hasProblems ? "row-with-problems" : undefined}>
        <TableCell>
          <div className="cell-stack">
            <Link href={href(detail)} className="strong">
              {extension.name}
            </Link>
            <span className="muted small">
              <span className="mono">{extension.id}</span> · v{extension.version}
              {extension.publisher && <> · {extension.publisher}</>}
            </span>
            {extension.description && <span className="small">{extension.description}</span>}
          </div>
        </TableCell>
        <TableCell>
          <KindBadge extension={extension} />
        </TableCell>
        <TableCell>
          <span className="badges">
            <StatusBadges extension={extension} />
            <VersionBadge extension={extension} />
          </span>
        </TableCell>
        <TableCell>
          <div className="cell-stack small">
            {extension.buttons.length > 0 && <span>{extension.buttons.join(", ")}</span>}
            {extension.commands.length > 0 && (
              <Tooltip content={extension.commands.join("\n")} relationship="description">
                <span className="muted">{plural(extension.commands.length, "command")}</span>
              </Tooltip>
            )}
          </div>
        </TableCell>
        <TableCell>
          <Switch
            checked={extension.enabled}
            disabled={actions.setEnabled.isPending}
            onChange={(_, data) => actions.setEnabled.mutate({ id: extension.id, enabled: data.checked })}
            aria-label={extension.enabled ? "Turn off" : "Turn on"}
          />
        </TableCell>
        <TableCell>
          <Menu>
            <MenuTrigger disableButtonEnhancement>
              <Button appearance="subtle" size="small" icon={<MoreHorizontal16Regular />} aria-label="More" />
            </MenuTrigger>
            <MenuPopover>
              <MenuList>
                <MenuItem icon={<Open16Regular />} onClick={() => navigate(detail)}>
                  Details and versions
                </MenuItem>
                <MenuItem
                  icon={<FolderOpen16Regular />}
                  onClick={() => openPath(extension.directory).catch((error) => notify.failed(error))}
                >
                  Open folder
                </MenuItem>
                <MenuItem icon={<ArrowClockwise16Regular />} onClick={() => actions.reload.mutate({ id: extension.id })}>
                  Reload
                </MenuItem>
                <MenuItem icon={<Delete16Regular />} onClick={() => void actions.remove(extension)}>
                  Remove…
                </MenuItem>
              </MenuList>
            </MenuPopover>
          </Menu>
        </TableCell>
      </TableRow>
      {(extension.errors.length > 0 || extension.diagnostics.length > 0) && (
        <TableRow className="problems-row">
          <TableCell colSpan={6}>
            {extension.errors.map((error, index) => (
              <div key={index} className="error-text small">
                {error}
              </div>
            ))}
            <DiagnosticsList
              diagnostics={extension.diagnostics}
              max={3}
              onOpen={(diagnostic) => navigate(detail, { tab: "files", file: diagnostic.file, line: diagnostic.line })}
            />
          </TableCell>
        </TableRow>
      )}
    </>
  );
}
