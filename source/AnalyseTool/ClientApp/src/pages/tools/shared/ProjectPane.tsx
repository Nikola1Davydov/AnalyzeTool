import { Badge, Checkbox, Input, Spinner, Tooltip } from "@fluentui/react-components";
import { ArrowEnter16Regular, ChevronDown12Regular, ChevronRight12Regular, Search16Regular } from "@fluentui/react-icons";
import { useMemo, useState } from "react";
import type { ProjectParameterDto } from "../../../api/tools";
import { dropHandlers, startDrag } from "./dnd";
import { projectKey, useSharedParameters } from "./SharedParametersContext";

/**
 * What the open project has: its bound parameters by category, then the shared ones only loaded families carry.
 * Dropping file parameters onto a category adds them there; onto the zone at the top, the full dialog opens.
 */
export function ProjectPane({
  onQuickBind,
  onBind,
}: {
  onQuickBind: (guids: string[], categoryName: string) => void;
  onBind: (guids: string[]) => void;
}) {
  const { project, parameters, selection, setSelection, projectLoading, projectError } = useSharedParameters();
  const [search, setSearch] = useState("");
  const [toggled, setToggled] = useState<Set<string>>(new Set());
  const [dropTarget, setDropTarget] = useState<string | null>(null);

  const inFile = useMemo(() => new Set(parameters.map((item) => item.guid.toLowerCase())), [parameters]);
  const selected = useMemo(() => new Set(selection), [selection]);
  const query = search.trim().toLowerCase();

  function origin(parameter: ProjectParameterDto) {
    if (!parameter.guid) return { text: "project", color: "subtle" as const, tip: "Project parameter — no shared parameter file has it" };
    if (inFile.has(parameter.guid.toLowerCase())) return { text: "file", color: "success" as const, tip: "From the open shared parameter file" };
    return { text: "other file", color: "warning" as const, tip: "Shared, but from another shared parameter file" };
  }

  const matches = (parameter: ProjectParameterDto) =>
    !query || `${parameter.name} ${parameter.categories.join(" ")}`.toLowerCase().includes(query);

  const byCategory = useMemo(() => {
    const map = new Map<string, ProjectParameterDto[]>();
    for (const parameter of project) {
      if (!parameter.bound || !matches(parameter)) continue;
      for (const category of parameter.categories) map.set(category, [...(map.get(category) ?? []), parameter]);
    }
    if (query) {
      for (const parameter of project) {
        if (!parameter.bound) continue;
        for (const category of parameter.categories) {
          if (category.toLowerCase().includes(query) && !map.has(category)) {
            map.set(category, project.filter((item) => item.bound && item.categories.includes(category)));
          }
        }
      }
    }
    return [...map.entries()]
      .sort(([a], [b]) => a.localeCompare(b))
      .map(([name, items]) => ({ name, items: [...items].sort((a, b) => a.name.localeCompare(b.name)) }));
  }, [project, query]);
  const familiesOnly = project.filter((item) => !item.bound && matches(item));

  // Many categories start folded; then a click unfolds one, otherwise a click folds one
  const startFolded = byCategory.length > 12;
  const isOpen = (name: string) => !!query || (startFolded ? toggled.has(name) : !toggled.has(name));
  const toggle = (name: string) =>
    setToggled((current) => {
      const next = new Set(current);
      if (next.has(name)) next.delete(name);
      else next.add(name);
      return next;
    });

  const setSelected = (parameter: ProjectParameterDto, on: boolean) => {
    const key = projectKey(parameter);
    setSelection((current) => (on ? [...current.filter((item) => item !== key), key] : current.filter((item) => item !== key)));
  };

  const fromFile = (parameter: ProjectParameterDto) => !!parameter.guid && inFile.has(parameter.guid.toLowerCase());

  return (
    <section className="pane">
      <header className="pane-header">
        <strong>Project</strong>
        <Input
          size="small"
          className="grow"
          contentBefore={<Search16Regular />}
          placeholder="Search parameter or category…"
          value={search}
          onChange={(_, data) => setSearch(data.value)}
        />
        {projectLoading && <Spinner size="extra-tiny" />}
      </header>

      <div className="pane-body">
        {projectError && <div className="error-text small pad">{projectError}</div>}
        <div
          className={`drop-zone ${dropTarget === "*" ? "active" : ""}`}
          {...dropHandlers("*", dropTarget, setDropTarget, onBind, "copy")}
        >
          <ArrowEnter16Regular /> Drop parameters from the file here to add them to the project — or onto a category below
        </div>

        {byCategory.map((category) => (
          <div
            key={category.name}
            className={`tree-group ${dropTarget === category.name ? "drop-target" : ""}`}
            {...dropHandlers(category.name, dropTarget, setDropTarget, (guids) => onQuickBind(guids, category.name), "copy")}
          >
            <div className="tree-group-header clickable" onClick={() => toggle(category.name)}>
              {isOpen(category.name) ? <ChevronDown12Regular /> : <ChevronRight12Regular />}
              <span className="tree-group-name">{category.name}</span>
              <span className="muted small">{category.items.length}</span>
            </div>
            {isOpen(category.name) &&
              category.items.map((parameter) => {
                const tag = origin(parameter);
                return (
                  <div
                    key={category.name + projectKey(parameter)}
                    draggable={fromFile(parameter)}
                    onDragStart={(event) => (fromFile(parameter) ? startDrag(event, [parameter.guid!]) : event.preventDefault())}
                    className={`tree-row ${selected.has(projectKey(parameter)) ? "selected" : ""}`}
                  >
                    <Checkbox
                      checked={selected.has(projectKey(parameter))}
                      onChange={(_, data) => setSelected(parameter, !!data.checked)}
                      aria-label={`Select ${parameter.name}`}
                    />
                    <span className="tree-row-name truncate">{parameter.name}</span>
                    <span className="muted small">{parameter.isInstance ? "Instance" : "Type"}</span>
                    <Tooltip content={tag.tip} relationship="description">
                      <Badge appearance="tint" color={tag.color} size="small" className="origin-badge">
                        {tag.text}
                      </Badge>
                    </Tooltip>
                  </div>
                );
              })}
          </div>
        ))}

        {familiesOnly.length > 0 && (
          <div className="tree-group">
            <div className="tree-group-header clickable" onClick={() => toggle("__families")}>
              {isOpen("__families") ? <ChevronDown12Regular /> : <ChevronRight12Regular />}
              <span className="tree-group-name muted">Only in loaded families</span>
              <span className="muted small">{familiesOnly.length}</span>
            </div>
            {isOpen("__families") &&
              familiesOnly.map((parameter) => (
                <div key={projectKey(parameter)} className="tree-row muted">
                  <span className="tree-row-name truncate">{parameter.name}</span>
                  <Badge appearance="tint" color={origin(parameter).color} size="small" className="origin-badge">
                    {origin(parameter).text}
                  </Badge>
                </div>
              ))}
          </div>
        )}

        {!byCategory.length && !familiesOnly.length && !projectLoading && (
          <div className="empty">{search ? "Nothing matches." : "The project has no shared or project parameters yet."}</div>
        )}
      </div>
    </section>
  );
}
