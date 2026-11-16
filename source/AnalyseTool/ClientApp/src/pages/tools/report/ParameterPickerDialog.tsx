import {
  Button,
  Checkbox,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  Input,
  Tab,
  TabList,
} from "@fluentui/react-components";
import { ArrowDown16Regular, ArrowUp16Regular, Dismiss16Regular, Search16Regular } from "@fluentui/react-icons";
import { useEffect, useMemo, useState } from "react";
import type { ParameterRef } from "../../../api/tools";
import { refKey, useSharedParameters } from "../shared/SharedParametersContext";

type Item = { key: string; ref: ParameterRef; name: string; binding: string; categories: string[]; fileGroup: string };
type GroupBy = "fileGroup" | "category" | "binding";

/**
 * Choose the report's parameters. Left: everything the project has bound, grouped by the file's groups, by category or
 * by binding, a whole group tickable at once. Right: the chosen ones in report order. Works on a copy until Apply.
 */
export function ParameterPickerDialog({
  open,
  value,
  onApply,
  onClose,
}: {
  open: boolean;
  value: ParameterRef[];
  onApply: (refs: ParameterRef[]) => void;
  onClose: () => void;
}) {
  const { project, parameters, groups } = useSharedParameters();
  const [chosen, setChosen] = useState<ParameterRef[]>([]);
  const [search, setSearch] = useState("");
  const [groupBy, setGroupBy] = useState<GroupBy>("fileGroup");

  useEffect(() => {
    if (open) {
      setChosen(value.map((ref) => ({ ...ref })));
      setSearch("");
    }
  }, [open, value]);

  const items = useMemo<Item[]>(() => {
    const groupName = new Map(groups.map((group) => [group.id, group.name]));
    const fileGroupByGuid = new Map(parameters.map((item) => [item.guid.toLowerCase(), groupName.get(item.groupId) ?? ""]));
    return project
      .filter((item) => item.bound)
      .map((item) => {
        const ref: ParameterRef = { guid: item.guid, name: item.name };
        const guid = item.guid?.toLowerCase();
        return {
          key: refKey(ref),
          ref,
          name: item.name,
          binding: item.isInstance ? "Instance" : "Type",
          categories: item.categories,
          fileGroup: guid ? fileGroupByGuid.get(guid) || "Shared, from another file" : "Project parameters",
        };
      })
      .sort((a, b) => a.name.localeCompare(b.name));
  }, [project, parameters, groups]);

  const known = useMemo(() => new Set(items.map((item) => item.key)), [items]);
  const chosenKeys = new Set(chosen.map(refKey));

  const grouped = useMemo(() => {
    const query = search.trim().toLowerCase();
    const map = new Map<string, Item[]>();
    for (const item of items) {
      if (query && !`${item.name} ${item.categories.join(" ")} ${item.fileGroup}`.toLowerCase().includes(query)) continue;
      const names =
        groupBy === "category"
          ? item.categories.length
            ? item.categories
            : ["(no category)"]
          : [groupBy === "binding" ? item.binding : item.fileGroup || "(no group)"];
      for (const name of names) map.set(name, [...(map.get(name) ?? []), item]);
    }
    return [...map.entries()].sort(([a], [b]) => a.localeCompare(b));
  }, [items, search, groupBy]);

  function toggle(item: Item, on: boolean) {
    setChosen((current) => {
      const has = current.some((ref) => refKey(ref) === item.key);
      if (on && !has) return [...current, { ...item.ref }];
      if (!on) return current.filter((ref) => refKey(ref) !== item.key);
      return current;
    });
  }

  function move(index: number, delta: number) {
    const to = index + delta;
    if (to < 0 || to >= chosen.length) return;
    const next = [...chosen];
    [next[index], next[to]] = [next[to], next[index]];
    setChosen(next);
  }

  const categoriesText = (categories: string[]) =>
    categories.length <= 3 ? categories.join(", ") : `${categories.length} categories`;

  return (
    <Dialog open={open} onOpenChange={(_, data) => !data.open && onClose()}>
      <DialogSurface style={{ maxWidth: "min(64rem, 96vw)", width: "64rem" }}>
        <DialogBody>
          <DialogTitle>Choose parameters</DialogTitle>
          <DialogContent>
            <div className="picker">
              <div className="picker-side">
                <div className="picker-toolbar">
                  <Input
                    size="small"
                    className="grow"
                    autoFocus
                    contentBefore={<Search16Regular />}
                    placeholder="Search name, category, group…"
                    value={search}
                    onChange={(_, data) => setSearch(data.value)}
                  />
                  <TabList size="small" selectedValue={groupBy} onTabSelect={(_, data) => setGroupBy(data.value as GroupBy)}>
                    <Tab value="fileGroup">File group</Tab>
                    <Tab value="category">Category</Tab>
                    <Tab value="binding">Binding</Tab>
                  </TabList>
                </div>
                <div className="picker-list">
                  {grouped.map(([name, list]) => {
                    const picked = list.filter((item) => chosenKeys.has(item.key)).length;
                    return (
                      <div key={name}>
                        <div className="tree-group-header">
                          <Checkbox
                            checked={picked === 0 ? false : picked === list.length ? true : "mixed"}
                            onChange={() => list.forEach((item) => toggle(item, picked !== list.length))}
                            label={<strong>{name}</strong>}
                          />
                          <span className="spacer" />
                          <span className="muted small">
                            {picked}/{list.length}
                          </span>
                        </div>
                        {list.map((item) => (
                          <div key={name + item.key} className="tree-row indent">
                            <Checkbox
                              checked={chosenKeys.has(item.key)}
                              onChange={(_, data) => toggle(item, !!data.checked)}
                              label={
                                <span className="cell-stack">
                                  <span>{item.name}</span>
                                  <span className="muted small">
                                    {item.binding} · {categoriesText(item.categories)}
                                  </span>
                                </span>
                              }
                            />
                          </div>
                        ))}
                      </div>
                    );
                  })}
                  {!grouped.length && (
                    <div className="empty">{items.length ? "Nothing matches." : "The project has no bound shared or project parameters."}</div>
                  )}
                </div>
              </div>
              <div className="picker-side">
                <div className="picker-toolbar">
                  <strong className="grow">In the report ({chosen.length})</strong>
                  <Button
                    size="small"
                    appearance="subtle"
                    disabled={chosen.length < 2}
                    onClick={() => setChosen([...chosen].sort((a, b) => (a.name ?? "").localeCompare(b.name ?? "")))}
                  >
                    A–Z
                  </Button>
                  <Button size="small" appearance="subtle" disabled={!chosen.length} onClick={() => setChosen([])}>
                    Clear
                  </Button>
                </div>
                <div className="picker-list">
                  {chosen.map((ref, index) => (
                    <div key={refKey(ref)} className={`tree-row ${known.has(refKey(ref)) ? "" : "muted"}`}>
                      <span className="muted small order">{index + 1}</span>
                      <span className="tree-row-name">
                        <span className="truncate">{ref.name}</span>
                        {!known.has(refKey(ref)) && <span className="small">not bound in this project — skipped</span>}
                      </span>
                      <Button size="small" appearance="subtle" icon={<ArrowUp16Regular />} disabled={index === 0} onClick={() => move(index, -1)} aria-label="Up" />
                      <Button
                        size="small"
                        appearance="subtle"
                        icon={<ArrowDown16Regular />}
                        disabled={index === chosen.length - 1}
                        onClick={() => move(index, 1)}
                        aria-label="Down"
                      />
                      <Button
                        size="small"
                        appearance="subtle"
                        icon={<Dismiss16Regular />}
                        onClick={() => setChosen(chosen.filter((_, i) => i !== index))}
                        aria-label="Remove"
                      />
                    </div>
                  ))}
                  {!chosen.length && <div className="empty">Tick parameters on the left; they appear here in report order.</div>}
                </div>
              </div>
            </div>
          </DialogContent>
          <DialogActions>
            <Button onClick={onClose}>Cancel</Button>
            <Button
              appearance="primary"
              onClick={() => {
                onApply(chosen);
                onClose();
              }}
            >
              Apply
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
}
