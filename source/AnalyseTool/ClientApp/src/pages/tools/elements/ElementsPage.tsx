import {
  Button,
  Checkbox,
  Combobox,
  Dropdown,
  Input,
  MessageBar,
  MessageBarActions,
  MessageBarBody,
  MessageBarTitle,
  Option,
  ProgressBar,
  Text,
  Tooltip,
} from "@fluentui/react-components";
import {
  ArrowDownload16Regular,
  Cursor16Regular,
  Eye16Regular,
  Save16Regular,
  Search16Regular,
} from "@fluentui/react-icons";
import { useQuery } from "@tanstack/react-query";
import { useMemo, useState } from "react";
import {
  type DataElement,
  type IsolationResult,
  type ParameterData,
  type SelectionResult,
  type SetDataMode,
  type SetDataResult,
  type SetDataToParametersRequest,
  toolCommands,
} from "../../../api/tools";
import { plural } from "../../../components/format";
import { errorText, useNotify } from "../../../components/notify";
import { Empty } from "../../../components/Page";
import { useActiveDocument, useCommandMutation, useInvoke } from "../../../revit/hooks";

const MAX_ROWS = 500;

const modes: { value: SetDataMode; label: string }[] = [
  { value: "Overwrite", label: "Overwrite" },
  { value: "OnlyIfEmpty", label: "Only if empty" },
  { value: "SkipIfEqual", label: "Skip if equal" },
];

const cellKey = (elementId: number, parameterId: number) => `${elementId}:${parameterId}`;

/**
 * The elements of a category with the values of the parameters you pick: select or isolate them in Revit, and write
 * values back — typed into the cells, or one value for every ticked element.
 */
export function ElementsPage() {
  const invoke = useInvoke();
  const document = useActiveDocument();
  const notify = useNotify();

  const categories = useQuery({
    queryKey: [document.data?.key ?? null, toolCommands.GetCategoriesInRevit],
    enabled: !!document.data,
    queryFn: ({ signal }) => invoke<string[]>(toolCommands.GetCategoriesInRevit, null, { signal }),
  });
  const [categoryInput, setCategoryInput] = useState("");
  const [category, setCategory] = useState<string | null>(null);
  const load = useCommandMutation<DataElement[], { categoryName: string }>(toolCommands.GetDataByCategoryName);
  const write = useCommandMutation<SetDataResult, SetDataToParametersRequest>(toolCommands.SetDataToParameters);

  const [columns, setColumns] = useState<string[]>([]);
  const [search, setSearch] = useState("");
  const [checked, setChecked] = useState<Set<number>>(new Set());
  const [edits, setEdits] = useState<Map<string, { elementId: number; id: number; value: string }>>(new Map());
  const [editing, setEditing] = useState<string | null>(null);
  const [mode, setMode] = useState<SetDataMode>("Overwrite");
  const [fill, setFill] = useState<{ parameter: string; value: string }>({ parameter: "", value: "" });
  const [lastWrite, setLastWrite] = useState<SetDataResult | null>(null);

  const elements = useMemo(() => load.data ?? [], [load.data]);

  // Every parameter name the elements carry, with how many carry it
  const parameterNames = useMemo(() => {
    const counts = new Map<string, number>();
    for (const element of elements) for (const parameter of element.parameters) counts.set(parameter.name, (counts.get(parameter.name) ?? 0) + 1);
    return [...counts.keys()].sort((a, b) => a.localeCompare(b));
  }, [elements]);

  const filtered = useMemo(() => {
    const query = search.trim().toLowerCase();
    if (!query) return elements;
    return elements.filter((element) =>
      `${element.id} ${element.name} ${element.level} ${element.parameters
        .filter((parameter) => columns.includes(parameter.name))
        .map((parameter) => parameter.value)
        .join(" ")}`
        .toLowerCase()
        .includes(query),
    );
  }, [elements, search, columns]);

  const categoryOptions = (categories.data ?? []).filter(
    (name) => !categoryInput.trim() || name.toLowerCase().includes(categoryInput.trim().toLowerCase()),
  );

  function loadCategory(name: string) {
    setCategory(name);
    setChecked(new Set());
    setEdits(new Map());
    setLastWrite(null);
    load.mutate(
      { categoryName: name },
      {
        onSuccess: (data) => {
          // Keep the picked columns the new category also has
          const names = new Set(data.flatMap((element) => element.parameters.map((parameter) => parameter.name)));
          setColumns((current) => current.filter((column) => names.has(column)));
        },
        onError: (error) => notify.failed(error, "Could not read the elements"),
      },
    );
  }

  const targets = checked.size ? filtered.filter((element) => checked.has(element.id)) : filtered;

  async function selectOrIsolate(isolate: boolean) {
    const elementIds = targets.map((element) => element.id);
    try {
      if (isolate) {
        const result = await invoke<IsolationResult>(toolCommands.IsolationInRevit, { elementIds });
        if (result.ok) notify.success(`Isolated ${plural(result.isolated, "element")} in the active view`);
        else notify.error("Could not isolate", result.error ?? undefined);
      } else {
        const result = await invoke<SelectionResult>(toolCommands.SelectionInRevit, { elementIds });
        if (result.ok) notify.success(`Selected ${plural(result.selected, "element")} in Revit`);
        else notify.error("Could not select", result.error ?? undefined);
      }
    } catch (error) {
      notify.failed(error);
    }
  }

  function setEdit(element: DataElement, parameter: ParameterData, value: string) {
    setEdits((current) => {
      const next = new Map(current);
      const key = cellKey(element.id, parameter.id);
      if (value === parameter.value) next.delete(key);
      else next.set(key, { elementId: parameter.elementId || element.id, id: parameter.id, value });
      return next;
    });
  }

  function fillChecked() {
    const name = fill.parameter;
    if (!name) return;
    let count = 0;
    let readOnly = 0;
    setEdits((current) => {
      const next = new Map(current);
      for (const element of targets) {
        const parameter = element.parameters.find((item) => item.name === name);
        if (!parameter) continue;
        if (parameter.isReadOnly) {
          readOnly++;
          continue;
        }
        next.set(cellKey(element.id, parameter.id), { elementId: parameter.elementId || element.id, id: parameter.id, value: fill.value });
        count++;
      }
      return next;
    });
    if (!columns.includes(name)) setColumns([...columns, name]);
    notify.info(`${plural(count, "value")} to write`, readOnly ? `${readOnly} read-only skipped` : undefined);
  }

  function writeEdits() {
    setLastWrite(null);
    write.mutate(
      { items: [...edits.values()], mode },
      {
        onSuccess: (result) => {
          setLastWrite(result);
          if (result.ok) {
            setEdits(new Map());
            if (category) loadCategory(category);
          }
        },
        onError: (error) => notify.failed(error, "Could not write the values"),
      },
    );
  }

  if (document.isSuccess && !document.data) {
    return (
      <div className="tool-page pad">
        <Empty>Open a Revit project to see its elements.</Empty>
      </div>
    );
  }

  return (
    <div className="tool-page elements-page">
      <section className="file-bar wrap">
        <Combobox
          className="category-box"
          placeholder={categories.isPending ? "Loading categories…" : "Pick a category"}
          value={categoryInput}
          selectedOptions={category ? [category] : []}
          onChange={(event) => setCategoryInput(event.target.value)}
          onOptionSelect={(_, data) => {
            if (!data.optionValue) return;
            setCategoryInput(data.optionValue);
            loadCategory(data.optionValue);
          }}
        >
          {categoryOptions.slice(0, 200).map((name) => (
            <Option key={name} value={name}>
              {name}
            </Option>
          ))}
        </Combobox>
        <Button
          icon={<ArrowDownload16Regular />}
          disabled={!category || load.isPending}
          onClick={() => category && loadCategory(category)}
        >
          Reload
        </Button>
        {load.isPending && (
          <>
            <ProgressBar className="grow" value={load.progress?.fraction} />
            <Text className="small muted">{load.progress?.message}</Text>
            <Button size="small" onClick={load.cancel}>
              Cancel
            </Button>
          </>
        )}
        {!load.isPending && elements.length > 0 && (
          <Text className="muted small">
            {plural(elements.length, "element")} in {category}
          </Text>
        )}
      </section>

      {elements.length > 0 && (
        <div className="selection-bar active wrap">
          <Input
            size="small"
            contentBefore={<Search16Regular />}
            placeholder="Filter rows"
            value={search}
            onChange={(_, data) => setSearch(data.value)}
          />
          <Combobox
            size="small"
            multiselect
            className="columns-box"
            placeholder="Parameters to show"
            value={columns.length ? plural(columns.length, "parameter") : ""}
            selectedOptions={columns}
            onOptionSelect={(_, data) => setColumns(data.selectedOptions)}
          >
            {parameterNames.map((name) => (
              <Option key={name} value={name}>
                {name}
              </Option>
            ))}
          </Combobox>
          <span className="spacer" />
          <Tooltip content={checked.size ? "The ticked elements" : "Every element the filter shows"} relationship="description">
            <Button size="small" icon={<Cursor16Regular />} disabled={!targets.length} onClick={() => void selectOrIsolate(false)}>
              Select in Revit ({targets.length})
            </Button>
          </Tooltip>
          <Button size="small" icon={<Eye16Regular />} disabled={!targets.length} onClick={() => void selectOrIsolate(true)}>
            Isolate
          </Button>
        </div>
      )}

      {elements.length > 0 && (
        <div className="selection-bar wrap">
          <Text className="small">Set</Text>
          <Dropdown
            size="small"
            placeholder="parameter"
            value={fill.parameter}
            selectedOptions={fill.parameter ? [fill.parameter] : []}
            onOptionSelect={(_, data) => setFill({ ...fill, parameter: data.optionValue ?? "" })}
          >
            {parameterNames.map((name) => (
              <Option key={name} value={name}>
                {name}
              </Option>
            ))}
          </Dropdown>
          <Text className="small">to</Text>
          <Input size="small" value={fill.value} placeholder="value" onChange={(_, data) => setFill({ ...fill, value: data.value })} />
          <Button size="small" disabled={!fill.parameter || !targets.length} onClick={fillChecked}>
            for {checked.size ? "ticked" : "all shown"} ({targets.length})
          </Button>
          <span className="spacer" />
          <Dropdown
            size="small"
            className="mode-box"
            value={modes.find((item) => item.value === mode)?.label}
            selectedOptions={[mode]}
            onOptionSelect={(_, data) => data.optionValue && setMode(data.optionValue as SetDataMode)}
          >
            {modes.map((item) => (
              <Option key={item.value} value={item.value}>
                {item.label}
              </Option>
            ))}
          </Dropdown>
          {edits.size > 0 && (
            <Button size="small" appearance="subtle" onClick={() => setEdits(new Map())}>
              Discard
            </Button>
          )}
          <Button
            size="small"
            appearance="primary"
            icon={<Save16Regular />}
            disabled={!edits.size || write.isPending}
            onClick={writeEdits}
          >
            Write {edits.size ? plural(edits.size, "change") : "changes"}
          </Button>
        </div>
      )}

      {write.isPending && (
        <div className="selection-bar">
          <ProgressBar className="grow" value={write.progress?.fraction} />
          <Text className="small">{write.progress?.message ?? "Writing…"}</Text>
          <Button size="small" onClick={write.cancel}>
            Cancel
          </Button>
        </div>
      )}

      {lastWrite && (
        <MessageBar intent={lastWrite.ok ? (lastWrite.problems?.length ? "warning" : "success") : "error"} layout="multiline">
          <MessageBarBody>
            <MessageBarTitle>
              {lastWrite.ok ? `Wrote ${plural(lastWrite.written, "value")}, skipped ${lastWrite.skipped}` : "Nothing was written"}
            </MessageBarTitle>
            {lastWrite.error}
            {lastWrite.problems?.slice(0, 10).map((problem, index) => (
              <div key={index} className="small">
                Element {problem.elementId}, parameter {problem.parameterId}: {problem.reason}
              </div>
            ))}
            {lastWrite.warnings.slice(0, 5).map((warning, index) => (
              <div key={`w${index}`} className="small muted">
                Revit: {warning.description}
              </div>
            ))}
          </MessageBarBody>
          <MessageBarActions>
            <Button appearance="transparent" onClick={() => setLastWrite(null)}>
              Close
            </Button>
          </MessageBarActions>
        </MessageBar>
      )}

      {load.isError && <div className="error-text pad">{errorText(load.error)}</div>}
      {!category && !load.isPending && <Empty>Pick a category to list its elements.</Empty>}
      {category && load.isSuccess && !elements.length && <Empty>No elements of {category}.</Empty>}

      {filtered.length > 0 && (
        <div className="grid-scroll">
          <table className="data-grid">
            <thead>
              <tr>
                <th className="check-col">
                  <Checkbox
                    checked={checked.size === 0 ? false : checked.size === filtered.length ? true : "mixed"}
                    onChange={() => setChecked(checked.size === filtered.length ? new Set() : new Set(filtered.map((e) => e.id)))}
                    aria-label="Tick all"
                  />
                </th>
                <th>Id</th>
                <th>Name</th>
                <th>Level</th>
                <th>Kind</th>
                {columns.map((column) => (
                  <th key={column}>{column}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {filtered.slice(0, MAX_ROWS).map((element) => (
                <tr key={element.id} className={checked.has(element.id) ? "selected" : ""}>
                  <td className="check-col">
                    <Checkbox
                      checked={checked.has(element.id)}
                      onChange={(_, data) =>
                        setChecked((current) => {
                          const next = new Set(current);
                          if (data.checked) next.add(element.id);
                          else next.delete(element.id);
                          return next;
                        })
                      }
                      aria-label={`Tick ${element.id}`}
                    />
                  </td>
                  <td className="mono">{element.id}</td>
                  <td>{element.name}</td>
                  <td>{element.level}</td>
                  <td className="muted">{element.isElementType ? "Type" : "Instance"}</td>
                  {columns.map((column) => {
                    const parameter = element.parameters.find((item) => item.name === column);
                    if (!parameter) return <td key={column} className="muted">—</td>;
                    const key = cellKey(element.id, parameter.id);
                    const edit = edits.get(key);
                    const value = edit ? edit.value : parameter.value;
                    if (editing === key) {
                      return (
                        <td key={column} className="editing">
                          <input
                            autoFocus
                            defaultValue={value}
                            onBlur={(event) => {
                              setEdit(element, parameter, event.target.value);
                              setEditing(null);
                            }}
                            onKeyDown={(event) => {
                              if (event.key === "Enter") (event.target as HTMLInputElement).blur();
                              if (event.key === "Escape") setEditing(null);
                            }}
                          />
                        </td>
                      );
                    }
                    return (
                      <td
                        key={column}
                        className={`${edit ? "edited" : ""} ${parameter.isReadOnly ? "read-only" : "editable"}`}
                        title={
                          parameter.isReadOnly
                            ? "Read-only"
                            : `${parameter.isTypeParameter ? "Type parameter — " : ""}double-click to edit${edit ? ` (was «${parameter.value}»)` : ""}`
                        }
                        onDoubleClick={() => !parameter.isReadOnly && setEditing(key)}
                      >
                        {value}
                      </td>
                    );
                  })}
                </tr>
              ))}
            </tbody>
          </table>
          {filtered.length > MAX_ROWS && (
            <div className="muted small pad">
              Showing the first {MAX_ROWS} of {filtered.length} — filter to narrow them down. Select and Isolate act on all.
            </div>
          )}
        </div>
      )}
    </div>
  );
}
