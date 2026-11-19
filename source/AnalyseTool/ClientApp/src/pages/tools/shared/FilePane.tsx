import { Badge, Button, Checkbox, Input, Tooltip } from "@fluentui/react-components";
import {
  Add16Regular,
  ChevronDown12Regular,
  ChevronRight12Regular,
  Delete16Regular,
  Edit16Regular,
  FolderAdd16Regular,
  Search16Regular,
  TextCollapse16Regular,
  TextExpand16Regular,
} from "@fluentui/react-icons";
import { type DragEvent, useMemo, useState } from "react";
import type { SharedParameterDto } from "../../../api/tools";
import { useConfirm } from "../../../components/confirm";
import { dropHandlers, startDrag } from "./dnd";
import { dataTypeLabel, fileKey, useSharedParameters } from "./SharedParametersContext";

/**
 * The shared parameter file as a tree: its groups, each with its parameters. Organising is dragging — parameters onto
 * another group, or onto «new group». The selection is shared with the project pane.
 */
export function FilePane({ onEdit }: { onEdit: (parameter: SharedParameterDto | null, groupId?: number) => void }) {
  const store = useSharedParameters();
  const confirm = useConfirm();
  const { file, groups, parameters, project, selection, setSelection } = store;
  const [search, setSearch] = useState("");
  const [notInProjectOnly, setNotInProjectOnly] = useState(false);
  const [collapsed, setCollapsed] = useState<Set<number>>(new Set());
  const [dropTarget, setDropTarget] = useState<number | "new" | null>(null);
  const [renaming, setRenaming] = useState<{ id: number; name: string } | null>(null);

  const projectByGuid = useMemo(
    () => new Map(project.filter((item) => item.guid).map((item) => [item.guid!.toLowerCase(), item])),
    [project],
  );
  const selected = useMemo(() => new Set(selection), [selection]);
  const query = search.trim().toLowerCase();

  const tree = useMemo(() => {
    const filtering = !!query || notInProjectOnly;
    return [...groups]
      .sort((a, b) => a.name.localeCompare(b.name))
      .map((group) => {
        const all = parameters.filter((item) => item.groupId === group.id);
        const items = all
          .filter((item) => !query || `${item.name} ${item.description} ${item.guid}`.toLowerCase().includes(query))
          .filter((item) => !notInProjectOnly || !projectByGuid.get(item.guid.toLowerCase())?.bound)
          .sort((a, b) => a.name.localeCompare(b.name));
        return { group, items, total: all.length };
      })
      .filter((node) => !filtering || node.items.length || node.group.name.toLowerCase().includes(query));
  }, [groups, parameters, query, notInProjectOnly, projectByGuid]);

  const isOpen = (id: number) => !collapsed.has(id) || !!query;
  const toggle = (id: number) =>
    setCollapsed((current) => {
      const next = new Set(current);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  const allCollapsed = groups.length > 0 && groups.every((group) => collapsed.has(group.id));

  const setSelected = (items: SharedParameterDto[], on: boolean) => {
    const keys = new Set(items.map(fileKey));
    setSelection((current) => (on ? [...new Set([...current, ...keys])] : current.filter((key) => !keys.has(key))));
  };

  function projectBadge(parameter: SharedParameterDto) {
    const inProject = projectByGuid.get(parameter.guid.toLowerCase());
    if (!inProject) return null;
    if (!inProject.bound) return { text: "families", tip: "In the project only through loaded families" };
    return {
      text: `${inProject.isInstance ? "Inst" : "Type"} · ${inProject.categories.length}`,
      tip: `${inProject.isInstance ? "Instance" : "Type"}: ${inProject.categories.join(", ")}`,
    };
  }

  function uniqueGroupName(base: string) {
    const taken = new Set(groups.map((group) => group.name.toLowerCase()));
    let name = base;
    for (let index = 2; taken.has(name.toLowerCase()); index++) name = `${base} ${index}`;
    return name;
  }

  const renameError = (() => {
    if (!renaming) return "";
    const name = renaming.name.trim().toLowerCase();
    if (!name) return "A group needs a name";
    return groups.some((group) => group.id !== renaming.id && group.name.trim().toLowerCase() === name) ? "Name already used" : "";
  })();

  function commitRename() {
    if (renaming && !renameError) store.renameGroup(renaming.id, renaming.name);
    setRenaming(null);
  }

  function addGroup() {
    const group = store.addGroup(uniqueGroupName("New group"));
    setRenaming({ id: group.id, name: group.name });
  }

  function drop(guids: string[], target: number | "new") {
    let groupId: number;
    if (target === "new") {
      const group = store.addGroup(uniqueGroupName("New group"));
      groupId = group.id;
      setRenaming({ id: group.id, name: group.name });
    } else groupId = target;
    store.moveParameters(guids, groupId);
    setCollapsed((current) => {
      const next = new Set(current);
      next.delete(groupId);
      return next;
    });
  }

  async function deleteParameter(parameter: SharedParameterDto) {
    const inProject = projectByGuid.has(parameter.guid.toLowerCase());
    const ok = await confirm({
      title: `Delete «${parameter.name}» from the file?`,
      message: inProject ? "It stays in the project — this only removes it from the shared parameter file." : "It is removed when you save the file.",
      confirmLabel: "Delete",
      danger: true,
    });
    if (ok) store.deleteParameters([parameter.guid]);
  }

  function onDragStart(event: DragEvent, parameter: SharedParameterDto) {
    const guids = selected.has(fileKey(parameter))
      ? parameters.filter((item) => selected.has(fileKey(item))).map((item) => item.guid)
      : [parameter.guid];
    startDrag(event, guids);
  }

  return (
    <section className="pane">
      <header className="pane-header">
        <strong>File</strong>
        <Input
          size="small"
          className="grow"
          contentBefore={<Search16Regular />}
          placeholder="Search the file…"
          value={search}
          onChange={(_, data) => setSearch(data.value)}
        />
        <Tooltip content="Only parameters the project has not bound" relationship="description">
          <Checkbox
            size="medium"
            label="Not in project"
            checked={notInProjectOnly}
            onChange={(_, data) => setNotInProjectOnly(!!data.checked)}
          />
        </Tooltip>
        <Tooltip content={allCollapsed ? "Expand all" : "Collapse all"} relationship="label">
          <Button
            size="small"
            appearance="subtle"
            icon={allCollapsed ? <TextExpand16Regular /> : <TextCollapse16Regular />}
            onClick={() => setCollapsed(allCollapsed ? new Set() : new Set(groups.map((group) => group.id)))}
          />
        </Tooltip>
        <Tooltip content="New group" relationship="label">
          <Button size="small" appearance="subtle" icon={<FolderAdd16Regular />} disabled={!file?.path} onClick={addGroup} />
        </Tooltip>
        <Button size="small" icon={<Add16Regular />} disabled={!file?.path} onClick={() => onEdit(null)}>
          Parameter
        </Button>
      </header>

      <div className="pane-body">
        {tree.map((node) => {
          const picked = node.items.filter((item) => selected.has(fileKey(item))).length;
          return (
            <div
              key={node.group.id}
              className={`tree-group ${dropTarget === node.group.id ? "drop-target" : ""}`}
              {...dropHandlers(node.group.id, dropTarget, setDropTarget, (guids) => drop(guids, node.group.id), "move")}
            >
              <div className="tree-group-header">
                <button type="button" className="link-button" onClick={() => toggle(node.group.id)}>
                  {isOpen(node.group.id) ? <ChevronDown12Regular /> : <ChevronRight12Regular />}
                </button>
                {node.items.length > 0 && (
                  <Checkbox
                    checked={picked === 0 ? false : picked === node.items.length ? true : "mixed"}
                    onChange={() => setSelected(node.items, picked !== node.items.length)}
                    aria-label="Select the group"
                  />
                )}
                {renaming?.id === node.group.id ? (
                  <>
                    <Input
                      size="small"
                      autoFocus
                      className="grow"
                      value={renaming.name}
                      onChange={(_, data) => setRenaming({ id: renaming.id, name: data.value })}
                      onKeyDown={(event) => {
                        if (event.key === "Enter") commitRename();
                        if (event.key === "Escape") setRenaming(null);
                      }}
                      onBlur={commitRename}
                      onFocus={(event) => event.target.select()}
                    />
                    {renameError && <span className="error-text small">{renameError}</span>}
                  </>
                ) : (
                  <span
                    className="tree-group-name"
                    title="Double-click to rename"
                    onClick={() => toggle(node.group.id)}
                    onDoubleClick={() => setRenaming({ id: node.group.id, name: node.group.name })}
                  >
                    {node.group.name}
                  </span>
                )}
                <span className="muted small">{node.total}</span>
                <span className="hover-actions">
                  <Tooltip content="New parameter in this group" relationship="label">
                    <Button size="small" appearance="subtle" icon={<Add16Regular />} onClick={() => onEdit(null, node.group.id)} />
                  </Tooltip>
                  <Tooltip content="Rename group" relationship="label">
                    <Button
                      size="small"
                      appearance="subtle"
                      icon={<Edit16Regular />}
                      onClick={() => setRenaming({ id: node.group.id, name: node.group.name })}
                    />
                  </Tooltip>
                  <Tooltip content={node.total > 0 ? "Move or delete its parameters first" : "Delete group"} relationship="label">
                    <Button
                      size="small"
                      appearance="subtle"
                      icon={<Delete16Regular />}
                      disabled={node.total > 0}
                      onClick={() => store.deleteGroup(node.group.id)}
                    />
                  </Tooltip>
                </span>
              </div>

              {isOpen(node.group.id) && (
                <>
                  {node.items.map((parameter) => {
                    const badge = projectBadge(parameter);
                    return (
                      <div
                        key={parameter.guid}
                        draggable
                        className={`tree-row ${selected.has(fileKey(parameter)) ? "selected" : ""}`}
                        onDragStart={(event) => onDragStart(event, parameter)}
                        onDoubleClick={() => onEdit(parameter)}
                      >
                        <Checkbox
                          checked={selected.has(fileKey(parameter))}
                          onChange={(_, data) => setSelected([parameter], !!data.checked)}
                          aria-label={`Select ${parameter.name}`}
                        />
                        <span className="tree-row-name">
                          <span className="truncate">{parameter.name}</span>
                          {parameter.description && <span className="truncate muted small">{parameter.description}</span>}
                        </span>
                        <span className="muted small type-col">{dataTypeLabel(parameter.dataType)}</span>
                        <span className="badge-col">
                          {badge && (
                            <Tooltip content={badge.tip} relationship="description">
                              <Badge appearance="tint" color="success" size="small">
                                {badge.text}
                              </Badge>
                            </Tooltip>
                          )}
                        </span>
                        <span className="hover-actions">
                          <Button size="small" appearance="subtle" icon={<Edit16Regular />} onClick={() => onEdit(parameter)} aria-label="Edit" />
                          <Button
                            size="small"
                            appearance="subtle"
                            icon={<Delete16Regular />}
                            onClick={() => void deleteParameter(parameter)}
                            aria-label="Delete"
                          />
                        </span>
                      </div>
                    );
                  })}
                  {!node.items.length && (
                    <div className="tree-empty muted small">{node.total ? "No match here." : "Empty — drag parameters here."}</div>
                  )}
                </>
              )}
            </div>
          );
        })}

        {file?.path ? (
          <div
            className={`drop-zone ${dropTarget === "new" ? "active" : ""}`}
            {...dropHandlers<number | "new">("new", dropTarget, setDropTarget, (guids) => drop(guids, "new"), "move")}
          >
            <FolderAdd16Regular /> Drop here to put them in a new group
          </div>
        ) : (
          <div className="empty">No shared parameter file — open one or create a new one above.</div>
        )}
      </div>
    </section>
  );
}
