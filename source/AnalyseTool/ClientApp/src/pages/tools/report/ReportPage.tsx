import {
  Button,
  Checkbox,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  Dropdown,
  Field,
  Input,
  Menu,
  MenuItem,
  MenuList,
  MenuPopover,
  MenuTrigger,
  Option,
  OptionGroup,
  Radio,
  RadioGroup,
  SpinButton,
  Spinner,
  Switch,
  Text,
} from "@fluentui/react-components";
import {
  ArrowClockwise16Regular,
  Dismiss12Regular,
  Globe16Regular,
  Building16Regular,
  MoreVertical16Regular,
  Print16Regular,
  TaskListSquareLtr16Regular,
} from "@fluentui/react-icons";
import { useQuery } from "@tanstack/react-query";
import { useEffect, useMemo, useRef, useState } from "react";
import {
  type DocumentData,
  type ParameterRef,
  type ParameterReportDto,
  type ParameterReportResult,
  toolCommands,
} from "../../../api/tools";
import { useConfirm } from "../../../components/confirm";
import { errorText, useNotify } from "../../../components/notify";
import { useActiveDocument, useInvoke } from "../../../revit/hooks";
import { refKey, useSharedParameters } from "../shared/SharedParametersContext";
import { BarChart } from "./BarChart";
import { useParameterSets } from "./parameterSets";
import { ParameterPickerDialog } from "./ParameterPickerDialog";
import {
  type ReportSettings,
  SECTION_KINDS,
  SECTION_LABELS,
  type SectionKind,
  loadSettings,
  percent,
  readStored,
  sectionBars,
  storeSettings,
  writeStored,
} from "./report";

/**
 * The A4 report: the chosen parameters on printable pages — a summary, then per parameter the analyses switched on,
 * each as a chart, a table or both. What is on screen is what prints.
 */
export function ReportPage({ fromSelection }: { fromSelection: boolean }) {
  const shared = useSharedParameters();
  const invoke = useInvoke();
  const document = useActiveDocument();
  const notify = useNotify();
  const confirm = useConfirm();
  const sets = useParameterSets();

  const [settings, setSettings] = useState<ReportSettings>(loadSettings);
  useEffect(() => storeSettings(settings), [settings]);
  const updateSection = (kind: SectionKind, changes: Partial<ReportSettings["sections"][SectionKind]>) =>
    setSettings((current) => ({
      ...current,
      sections: { ...current.sections, [kind]: { ...current.sections[kind], ...changes } },
    }));

  // The open project, by CreationGUID: a set can belong to it, and the remembered selection is its own
  const documentData = useQuery({
    queryKey: [document.data?.key ?? null, toolCommands.GetDocumentData],
    enabled: !!document.data,
    queryFn: ({ signal }) => invoke<DocumentData>(toolCommands.GetDocumentData, null, { signal }),
  });
  const projectId = documentData.data?.id || undefined;
  const projectName = documentData.data?.name ?? "";

  const [chosen, setChosen] = useState<ParameterRef[]>([]);
  const [activeSetId, setActiveSetId] = useState<string | null>(null);
  const [pickerOpen, setPickerOpen] = useState(false);

  // The last selection comes back per project when the window reopens; a fresh pick in the table wins
  const restoredFor = useRef<string | null>(null);
  useEffect(() => {
    if (!documentData.isSuccess) return;
    const storageKey = `at.report.selection:${projectId ?? "none"}`;
    if (restoredFor.current === storageKey) return;
    restoredFor.current = storageKey;
    const saved = readStored<{ chosen?: ParameterRef[]; activeSetId?: string }>(storageKey);
    setChosen(Array.isArray(saved?.chosen) ? saved.chosen : []);
    setActiveSetId(typeof saved?.activeSetId === "string" ? saved.activeSetId : null);
  }, [documentData.isSuccess, projectId]);
  useEffect(() => {
    if (restoredFor.current) writeStored(restoredFor.current, { chosen, activeSetId });
  }, [chosen, activeSetId]);

  const takenSelection = useRef("");
  useEffect(() => {
    if (!restoredFor.current) return;
    const signature = shared.reportSelection.map(refKey).join("|");
    if (fromSelection && shared.reportSelection.length && signature !== takenSelection.current) {
      setChosen(shared.reportSelection.map((ref) => ({ ...ref })));
      setActiveSetId(null);
    }
    takenSelection.current = signature;
  }, [fromSelection, shared.reportSelection, documentData.isSuccess]);

  // What the report can count: what the project has bound
  const bound = useMemo(
    () => new Set(shared.project.filter((item) => item.bound).map((item) => refKey({ guid: item.guid, name: item.name }))),
    [shared.project],
  );
  const reportable = chosen.filter((ref) => bound.has(refKey(ref)));
  const missing = chosen.length - reportable.length;

  const visibleSets = sets.forProject(projectId);
  const activeSet = visibleSets.find((set) => set.id === activeSetId) ?? null;
  const sameList = (a: ParameterRef[], b: ParameterRef[]) => a.map(refKey).join("|") === b.map(refKey).join("|");
  const setModified = !!activeSet && !sameList(activeSet.parameters, chosen);

  function applySet(id: string | null) {
    setActiveSetId(id);
    const set = sets.sets.find((item) => item.id === id);
    if (set) setChosen(set.parameters.map((parameter) => ({ ...parameter })));
  }

  // ---- Data ------------------------------------------------------------------------------------------
  const [debounced, setDebounced] = useState({ refs: reportable, top: settings.topValues });
  const signature = reportable.map(refKey).join("|");
  useEffect(() => {
    const timer = setTimeout(() => setDebounced({ refs: reportable, top: settings.topValues }), 300);
    return () => clearTimeout(timer);
  }, [signature, settings.topValues]);

  const report = useQuery({
    queryKey: [document.data?.key ?? null, toolCommands.GetParameterReport, debounced.refs.map(refKey), debounced.top],
    enabled: !!document.data && debounced.refs.length > 0,
    queryFn: ({ signal }) =>
      invoke<ParameterReportResult>(
        toolCommands.GetParameterReport,
        { parameters: debounced.refs, topValues: debounced.top },
        { signal },
      ),
  });
  const data = reportable.length ? report.data : undefined;

  // ---- Saving sets ----------------------------------------------------------------------------------
  const [nameDialog, setNameDialog] = useState<{ mode: "new" | "rename"; name: string; thisProjectOnly: boolean } | null>(null);
  const nameError =
    nameDialog && nameDialog.name.trim() && sets.nameTaken(nameDialog.name, projectId, nameDialog.mode === "rename" ? (activeSetId ?? undefined) : undefined)
      ? "A set with this name exists."
      : "";
  const scopeOf = (thisProjectOnly: boolean) =>
    thisProjectOnly && projectId ? { projectId, projectName } : { projectId: undefined, projectName: undefined };

  async function confirmName() {
    if (!nameDialog || !nameDialog.name.trim() || nameError) return;
    if (nameDialog.mode === "new") {
      const created = await sets.create(nameDialog.name, chosen, scopeOf(nameDialog.thisProjectOnly));
      if (created) {
        setActiveSetId(created.id);
        notify.success(`Saved set «${created.name}»`);
      }
    } else if (activeSetId) {
      await sets.update(activeSetId, { name: nameDialog.name.trim() });
    }
    setNameDialog(null);
  }

  async function toggleScope() {
    if (!activeSet) return;
    const toThisProject = !activeSet.projectId;
    if (await sets.update(activeSet.id, scopeOf(toThisProject))) {
      notify.success(toThisProject ? `«${activeSet.name}» is now only in this project` : `«${activeSet.name}» is now in all projects`);
    }
  }

  async function deleteSet() {
    if (!activeSet) return;
    const ok = await confirm({
      title: `Delete the set «${activeSet.name}»?`,
      message: "The report keeps its current parameters.",
      confirmLabel: "Delete",
      danger: true,
    });
    if (ok && (await sets.remove(activeSet.id))) setActiveSetId(null);
  }

  const date = new Date().toLocaleDateString();
  const projectSets = visibleSets.filter((set) => set.projectId);
  const globalSets = visibleSets.filter((set) => !set.projectId);

  return (
    <div className="tool-page report-page">
      <aside className="report-settings no-print">
        <Field label="Title">
          <Input size="small" value={settings.title} onChange={(_, d) => setSettings({ ...settings, title: d.value })} />
        </Field>

        <div className="settings-block">
          <Text weight="semibold">Parameters</Text>
          <div className="form-row tight">
            <Dropdown
              size="small"
              className="grow"
              placeholder={visibleSets.length ? "No saved set" : "No saved sets yet"}
              value={activeSet?.name ?? ""}
              selectedOptions={activeSetId ? [activeSetId] : []}
              onOptionSelect={(_, d) => applySet(d.optionValue ?? null)}
            >
              {projectSets.length > 0 && (
                <OptionGroup label={projectName ? `This project — ${projectName}` : "This project"}>
                  {projectSets.map((set) => (
                    <Option key={set.id} value={set.id}>
                      {set.name}
                    </Option>
                  ))}
                </OptionGroup>
              )}
              {globalSets.length > 0 && (
                <OptionGroup label="All projects">
                  {globalSets.map((set) => (
                    <Option key={set.id} value={set.id}>
                      {set.name}
                    </Option>
                  ))}
                </OptionGroup>
              )}
            </Dropdown>
            <Menu>
              <MenuTrigger disableButtonEnhancement>
                <Button size="small" appearance="subtle" icon={<MoreVertical16Regular />} aria-label="Sets" />
              </MenuTrigger>
              <MenuPopover>
                <MenuList>
                  <MenuItem
                    disabled={!chosen.length}
                    onClick={() => setNameDialog({ mode: "new", name: "", thisProjectOnly: !!projectId })}
                  >
                    Save as new set…
                  </MenuItem>
                  <MenuItem
                    disabled={!activeSet}
                    onClick={() => setNameDialog({ mode: "rename", name: activeSet?.name ?? "", thisProjectOnly: false })}
                  >
                    Rename…
                  </MenuItem>
                  <MenuItem
                    disabled={!activeSet || (!activeSet.projectId && !projectId)}
                    icon={activeSet?.projectId ? <Globe16Regular /> : <Building16Regular />}
                    onClick={() => void toggleScope()}
                  >
                    {activeSet?.projectId ? "Make available in all projects" : "Keep only in this project"}
                  </MenuItem>
                  <MenuItem disabled={!activeSet} onClick={() => void deleteSet()}>
                    Delete set
                  </MenuItem>
                </MenuList>
              </MenuPopover>
            </Menu>
          </div>
          {activeSet && (
            <span className="muted small">{activeSet.projectId ? "Only in this project" : "In all projects"}</span>
          )}
          {setModified && (
            <div className="form-row tight small">
              <span className="warning-text grow">Changed since saved</span>
              <Button
                size="small"
                appearance="transparent"
                disabled={sets.busy}
                onClick={() =>
                  void sets.update(activeSet!.id, { parameters: chosen.map((r) => ({ guid: r.guid ?? undefined, name: r.name ?? "" })) }).then(
                    (ok) => ok && notify.success(`Updated set «${activeSet!.name}»`),
                  )
                }
              >
                Save
              </Button>
              <Button size="small" appearance="transparent" onClick={() => applySet(activeSetId)}>
                Revert
              </Button>
            </div>
          )}
          {!activeSet && chosen.length > 0 && (
            <div className="form-row tight small">
              <span className="muted grow">Not saved as a set</span>
              <Button
                size="small"
                appearance="transparent"
                onClick={() => setNameDialog({ mode: "new", name: "", thisProjectOnly: !!projectId })}
              >
                Save as set…
              </Button>
            </div>
          )}

          <div className="chosen-list">
            {chosen.map((ref, index) => (
              <div
                key={refKey(ref)}
                className={`tree-row ${bound.has(refKey(ref)) ? "" : "muted"}`}
                title={bound.has(refKey(ref)) ? undefined : "Not bound in this project — skipped"}
              >
                <span className="muted small order">{index + 1}</span>
                <span className="tree-row-name truncate">{ref.name}</span>
                <span className="hover-actions">
                  <Button
                    size="small"
                    appearance="subtle"
                    icon={<Dismiss12Regular />}
                    aria-label="Remove"
                    onClick={() => setChosen(chosen.filter((_, i) => i !== index))}
                  />
                </span>
              </div>
            ))}
            {!chosen.length && (
              <div className="muted small pad">None yet — choose them, open a saved set, or tick rows on Shared parameters.</div>
            )}
          </div>
          {missing > 0 && <span className="muted small">{missing} not bound in this project — skipped.</span>}
          <Button size="small" icon={<TaskListSquareLtr16Regular />} onClick={() => setPickerOpen(true)}>
            Choose parameters…
          </Button>
        </div>

        <div className="settings-block">
          <Text weight="semibold">Show</Text>
          <Switch
            checked={settings.summary}
            onChange={(_, d) => setSettings({ ...settings, summary: d.checked })}
            label="Summary — all parameters in one table"
          />
          {SECTION_KINDS.map((kind) => (
            <div key={kind} className="section-card">
              <Switch
                checked={settings.sections[kind].enabled}
                onChange={(_, d) => updateSection(kind, { enabled: d.checked })}
                label={
                  <span>
                    {SECTION_LABELS[kind].title} <span className="muted small">— {SECTION_LABELS[kind].hint}</span>
                  </span>
                }
              />
              {settings.sections[kind].enabled && (
                <div className="form-row tight indent">
                  <Checkbox
                    label="Chart"
                    checked={settings.sections[kind].chart}
                    onChange={(_, d) => updateSection(kind, { chart: !!d.checked })}
                  />
                  <Checkbox
                    label="Table"
                    checked={settings.sections[kind].table}
                    onChange={(_, d) => updateSection(kind, { table: !!d.checked })}
                  />
                  {kind === "values" && (
                    <>
                      <span className="small">Top</span>
                      <SpinButton
                        size="small"
                        className="top-values"
                        min={3}
                        max={50}
                        value={settings.topValues}
                        onChange={(_, d) => {
                          const next = d.value ?? Number(d.displayValue);
                          if (next && !Number.isNaN(next)) setSettings({ ...settings, topValues: Math.min(50, Math.max(3, next)) });
                        }}
                      />
                    </>
                  )}
                </div>
              )}
            </div>
          ))}
        </div>

        <div className="settings-block push-down">
          <Button appearance="primary" icon={<Print16Regular />} disabled={!data || report.isFetching} onClick={() => window.print()}>
            Print
          </Button>
          <Button
            size="small"
            icon={<ArrowClockwise16Regular />}
            disabled={!reportable.length || report.isFetching}
            onClick={() => void report.refetch()}
          >
            Refresh
          </Button>
        </div>
      </aside>

      <main className="report-preview">
        {!reportable.length && (
          <div className="empty no-print">
            {chosen.length
              ? "None of the chosen parameters is bound in this project."
              : "Choose parameters on the left — or tick rows on Shared parameters."}
          </div>
        )}
        {reportable.length > 0 && report.isPending && <Spinner className="no-print" label="Counting…" />}
        {report.isError && reportable.length > 0 && <div className="error-text no-print">{errorText(report.error)}</div>}
        {data && <ReportSheet data={data} settings={settings} date={date} />}
      </main>

      <ParameterPickerDialog open={pickerOpen} value={chosen} onApply={setChosen} onClose={() => setPickerOpen(false)} />

      <Dialog open={!!nameDialog} onOpenChange={(_, d) => !d.open && setNameDialog(null)}>
        <DialogSurface style={{ maxWidth: "min(24rem, 95vw)" }}>
          <DialogBody>
            <DialogTitle>{nameDialog?.mode === "rename" ? "Rename set" : "Save as new set"}</DialogTitle>
            <DialogContent className="form">
              {nameDialog && (
                <>
                  <Field
                    validationState={nameError ? "error" : "none"}
                    validationMessage={nameError || undefined}
                    hint={nameDialog.mode === "new" ? `${chosen.length} parameters, in this order.` : undefined}
                  >
                    <Input
                      autoFocus
                      placeholder="e.g. Fire safety"
                      value={nameDialog.name}
                      onChange={(_, d) => setNameDialog({ ...nameDialog, name: d.value })}
                      onKeyDown={(event) => event.key === "Enter" && void confirmName()}
                    />
                  </Field>
                  {nameDialog.mode === "new" && (
                    <RadioGroup
                      value={nameDialog.thisProjectOnly ? "project" : "all"}
                      onChange={(_, d) => setNameDialog({ ...nameDialog, thisProjectOnly: d.value === "project" })}
                    >
                      <Radio
                        value="project"
                        disabled={!projectId}
                        label={`Only this project${projectName ? ` — ${projectName}` : ""}`}
                      />
                      <Radio value="all" label="All projects" />
                    </RadioGroup>
                  )}
                </>
              )}
            </DialogContent>
            <DialogActions>
              <Button onClick={() => setNameDialog(null)}>Cancel</Button>
              <Button
                appearance="primary"
                disabled={!nameDialog?.name.trim() || !!nameError || sets.busy}
                onClick={() => void confirmName()}
              >
                Save
              </Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>
    </div>
  );
}

/** The printable sheet: blocks that never break inside, a heading kept with what follows. */
function ReportSheet({ data, settings, date }: { data: ParameterReportResult; settings: ReportSettings; date: string }) {
  const sections = SECTION_KINDS.filter((kind) => settings.sections[kind].enabled);
  return (
    <div className="sheet">
      <div className="sheet-title keep">
        <div className="sheet-title-text">{settings.title || "Parameter report"}</div>
        <div className="sheet-meta">
          {data.documentTitle} · {date} · {data.parameters.length} {data.parameters.length === 1 ? "parameter" : "parameters"}
        </div>
      </div>

      {settings.summary && data.parameters.length > 0 && (
        <div className="keep">
          <div className="sheet-section-title">Summary</div>
          <table className="report-table">
            <thead>
              <tr>
                <th>Parameter</th>
                <th>Binding</th>
                <th className="num">Filled</th>
                <th className="num">Total</th>
                <th className="bar-col">Filled %</th>
              </tr>
            </thead>
            <tbody>
              {data.parameters.map((parameter) => (
                <tr key={parameter.name + (parameter.guid ?? "")}>
                  <td>{parameter.name}</td>
                  <td>{parameter.error ? "—" : parameter.isInstance ? "Instance" : "Type"}</td>
                  <td className="num">{parameter.error ? "—" : parameter.filled}</td>
                  <td className="num">{parameter.error ? "—" : parameter.total}</td>
                  <td>
                    {parameter.error ? (
                      <span className="muted">{parameter.error}</span>
                    ) : (
                      <PercentBar filled={parameter.filled} total={parameter.total} />
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {data.parameters.map((parameter, index) => (
        <ParameterSections key={index} parameter={parameter} sections={sections} settings={settings} />
      ))}
    </div>
  );
}

function PercentBar({ filled, total }: { filled: number; total: number }) {
  return (
    <div className="percent-bar">
      <div className="percent-track">
        <div className="percent-fill" style={{ width: `${percent(filled, total)}%` }} />
      </div>
      <span>{percent(filled, total)}%</span>
    </div>
  );
}

function ParameterSections({
  parameter,
  sections,
  settings,
}: {
  parameter: ParameterReportDto;
  sections: SectionKind[];
  settings: ReportSettings;
}) {
  const share = percent(parameter.filled, parameter.total);
  return (
    <>
      <div className="sheet-heading keep-with-next">
        <div className="sheet-heading-row">
          <span className="sheet-heading-name">{parameter.name}</span>
          {!parameter.error && <span className={share === 100 ? "complete" : ""}>{share}% filled</span>}
        </div>
        <div className="sheet-meta">
          {parameter.error
            ? parameter.error
            : parameter.total === 0
              ? "No element in the bound categories carries this parameter."
              : `${parameter.isInstance ? "Instance parameter" : "Type parameter (counted per instance)"} · ${parameter.filled} of ${parameter.total} elements · ${parameter.distinctValues} distinct ${parameter.distinctValues === 1 ? "value" : "values"}`}
        </div>
      </div>
      {!parameter.error &&
        parameter.total > 0 &&
        sections.map((kind) => {
          const bars = sectionBars(kind, parameter);
          if (!bars.length) return null;
          const section = settings.sections[kind];
          return (
            <div key={kind} className="keep">
              <div className="sheet-section-title">{SECTION_LABELS[kind].title}</div>
              {section.chart && <BarChart bars={bars} stacked={kind !== "values"} />}
              {section.table && <SectionTable kind={kind} parameter={parameter} />}
            </div>
          );
        })}
    </>
  );
}

function SectionTable({ kind, parameter }: { kind: SectionKind; parameter: ParameterReportDto }) {
  if (kind === "values") {
    return (
      <table className="report-table">
        <thead>
          <tr>
            <th>Value</th>
            <th className="num">Elements</th>
            <th className="num">% of filled</th>
          </tr>
        </thead>
        <tbody>
          {parameter.values.map((value, index) => (
            <tr key={index}>
              <td className="break">{value.value}</td>
              <td className="num">{value.count}</td>
              <td className="num">{percent(value.count, parameter.filled)}%</td>
            </tr>
          ))}
          {parameter.otherValuesCount > 0 && (
            <tr className="muted">
              <td>Other ({parameter.distinctValues - parameter.values.length} values)</td>
              <td className="num">{parameter.otherValuesCount}</td>
              <td className="num">{percent(parameter.otherValuesCount, parameter.filled)}%</td>
            </tr>
          )}
        </tbody>
      </table>
    );
  }
  const rows = kind === "filled" ? parameter.categories : parameter.levels;
  return (
    <table className="report-table">
      <thead>
        <tr>
          <th>{kind === "filled" ? "Category" : "Level"}</th>
          <th className="num">Filled</th>
          <th className="num">Empty</th>
          <th className="num">Total</th>
          <th className="num">Filled %</th>
        </tr>
      </thead>
      <tbody>
        {rows.map((row, index) => (
          <tr key={index}>
            <td>{row.name}</td>
            <td className="num">{row.filled}</td>
            <td className="num">{row.total - row.filled}</td>
            <td className="num">{row.total}</td>
            <td className="num">{percent(row.filled, row.total)}%</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}
