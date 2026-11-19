import { Badge, Button, Tooltip } from "@fluentui/react-components";
import {
  ArrowClockwise16Regular,
  ArrowEnter16Regular,
  ChartMultiple16Regular,
  Delete16Regular,
  Dismiss16Regular,
  DocumentAdd16Regular,
  DocumentText20Regular,
  FolderOpen16Regular,
  Save16Regular,
} from "@fluentui/react-icons";
import { useState } from "react";
import type { SharedParameterDto } from "../../../api/tools";
import { useConfirm } from "../../../components/confirm";
import { fileName, plural } from "../../../components/format";
import { useNotify } from "../../../components/notify";
import { navigate } from "../../../router";
import { BindDialog, ParameterDialog, QuickBindDialog } from "./dialogs";
import { FilePane } from "./FilePane";
import { ProjectPane } from "./ProjectPane";
import { type ParameterRow, useSharedParameters } from "./SharedParametersContext";

/**
 * The shared parameter FILE on the left — organised in its groups by dragging — and the PROJECT on the right — what is
 * bound, by category. Dragging from the file into the project adds parameters to it. One selection spans both: it
 * feeds the report and the actions in the bar above.
 */
export function SharedParametersPage() {
  const store = useSharedParameters();
  const notify = useNotify();
  const confirm = useConfirm();
  const { file, dirty, loading, saving, selection, selectedRows, reportSelection } = store;

  const [edit, setEdit] = useState<{ parameter: SharedParameterDto | null; groupId?: number } | null>(null);
  const [bindRows, setBindRows] = useState<ParameterRow[] | null>(null);
  const [quick, setQuick] = useState<{ guids: string[]; category: string } | null>(null);

  const selectedInFile = selectedRows.filter((row) => row.file);

  /** The full dialog needs the file saved; offer to do it rather than refuse. */
  async function openBind(guids: string[]) {
    if (store.bindBlocker) {
      if (!dirty) return notify.warning(store.bindBlocker);
      const ok = await confirm({
        title: "Save the file first?",
        message: "Revit adds parameters from the file on disk.",
        confirmLabel: "Save and continue",
      });
      if (!ok || !(await store.save())) return;
    }
    const wanted = new Set(guids.map((guid) => guid.toLowerCase()));
    const rows = store.rows.filter((row) => row.file && row.guid && wanted.has(row.guid));
    if (rows.length) setBindRows(rows);
  }

  async function deleteSelected() {
    const inProject = selectedInFile.filter((row) => row.project).length;
    const ok = await confirm({
      title: `Delete ${selectedInFile.length === 1 ? `«${selectedInFile[0].name}»` : plural(selectedInFile.length, "parameter")} from the file?`,
      message: inProject
        ? `${inProject} of them stay in the project — this only removes them from the shared parameter file.`
        : "They are removed when you save the file.",
      confirmLabel: "Delete",
      danger: true,
    });
    if (ok) store.deleteParameters(selectedInFile.map((row) => row.guid!));
  }

  async function reload() {
    if (await store.confirmDiscard()) await store.load();
  }

  return (
    <div className="tool-page shared-page">
      <section className="file-bar">
        <DocumentText20Regular className="muted" />
        <div className="file-bar-name">
          {file?.path ? (
            <>
              <div className="strong truncate" title={file.path}>
                {fileName(file.path)}{" "}
                {!file.exists && (
                  <Badge appearance="tint" color="danger" size="small">
                    missing
                  </Badge>
                )}
                {file.exists && !file.isRevitCurrent && (
                  <Badge appearance="tint" color="warning" size="small">
                    not Revit's current file
                  </Badge>
                )}{" "}
                {dirty && (
                  <Badge appearance="tint" color="warning" size="small">
                    unsaved changes
                  </Badge>
                )}
              </div>
              <div className="muted small truncate">{file.path}</div>
            </>
          ) : (
            <div className="muted">Revit has no shared parameter file set — open one or create a new one.</div>
          )}
        </div>
        <div className="toolbar-buttons">
          <Button size="small" icon={<FolderOpen16Regular />} onClick={() => void store.openFile(false)}>
            Open…
          </Button>
          <Button size="small" icon={<DocumentAdd16Regular />} onClick={() => void store.openFile(true)}>
            New file…
          </Button>
          <Tooltip content="Reload the file and the project" relationship="label">
            <Button size="small" appearance="subtle" icon={<ArrowClockwise16Regular />} disabled={loading} onClick={() => void reload()} />
          </Tooltip>
          {dirty && (
            <>
              <Button size="small" appearance="subtle" onClick={store.discard}>
                Discard
              </Button>
              <Button size="small" appearance="primary" icon={<Save16Regular />} disabled={saving} onClick={() => void store.save()}>
                Save
              </Button>
            </>
          )}
        </div>
      </section>

      <div className={`selection-bar ${selection.length ? "active" : ""}`}>
        {selection.length ? (
          <strong>{selection.length} selected</strong>
        ) : (
          <span className="muted">
            Drag parameters between groups to organise the file, and into the project to add them. Tick them for the report.
          </span>
        )}
        <span className="spacer" />
        {selection.length > 0 && (
          <>
            <Button
              size="small"
              appearance="primary"
              icon={<ArrowEnter16Regular />}
              disabled={!selectedInFile.length}
              title={selectedInFile.length ? undefined : "Select parameters of the file"}
              onClick={() => void openBind(selectedInFile.map((row) => row.guid!))}
            >
              Add to project
            </Button>
            <Button
              size="small"
              icon={<ChartMultiple16Regular />}
              disabled={!reportSelection.length}
              title={reportSelection.length ? undefined : "Select parameters that are bound in the project"}
              onClick={() => navigate("/report", { from: "selection" })}
            >
              Report ({reportSelection.length})
            </Button>
            <Tooltip content="Delete from the file" relationship="label">
              <Button
                size="small"
                appearance="subtle"
                icon={<Delete16Regular />}
                disabled={!selectedInFile.length}
                onClick={() => void deleteSelected()}
              />
            </Tooltip>
            <Tooltip content="Clear selection" relationship="label">
              <Button size="small" appearance="subtle" icon={<Dismiss16Regular />} onClick={() => store.setSelection([])} />
            </Tooltip>
          </>
        )}
      </div>

      <div className="two-panes">
        <FilePane onEdit={(parameter, groupId) => setEdit({ parameter, groupId })} />
        <ProjectPane onQuickBind={(guids, category) => setQuick({ guids, category })} onBind={(guids) => void openBind(guids)} />
      </div>

      <ParameterDialog open={!!edit} parameter={edit?.parameter ?? null} groupId={edit?.groupId} onClose={() => setEdit(null)} />
      <BindDialog open={!!bindRows} rows={bindRows ?? []} onClose={() => setBindRows(null)} />
      <QuickBindDialog
        open={!!quick}
        guids={quick?.guids ?? []}
        categoryName={quick?.category ?? ""}
        onClose={() => setQuick(null)}
      />
    </div>
  );
}
