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
  MessageBar,
  MessageBarBody,
  Option,
  Radio,
  RadioGroup,
  Spinner,
  Switch,
  Textarea,
} from "@fluentui/react-components";
import { Search16Regular } from "@fluentui/react-icons";
import { useEffect, useMemo, useState } from "react";
import type { BindResult, BindingOptionsResult, SharedParameterDto } from "../../../api/tools";
import { useNotify } from "../../../components/notify";
import { type ParameterRow, DATA_TYPES, useSharedParameters } from "./SharedParametersContext";

function bindMessage(notify: ReturnType<typeof useNotify>, result: BindResult, prefix = "") {
  const done = result.added.length + result.updated.length;
  if (done) notify.success(`${prefix}Added ${result.added.length}, updated ${result.updated.length}`);
  if (result.problems.length) notify.warning("Some parameters were not added", result.problems.map((p) => `${p.name}: ${p.reason}`).join("\n"));
  return done;
}

// ---- Create or edit one parameter of the file --------------------------------------------------------

/**
 * The GUID is the parameter's identity: shown, never edited — a renamed parameter stays the same parameter in every
 * project that uses it.
 */
export function ParameterDialog({
  open,
  parameter,
  groupId,
  onClose,
}: {
  open: boolean;
  parameter: SharedParameterDto | null;
  groupId?: number;
  onClose: () => void;
}) {
  const store = useSharedParameters();
  const isNew = !parameter;
  const [draft, setDraft] = useState<SharedParameterDto | null>(null);
  const [newGroup, setNewGroup] = useState("");

  useEffect(() => {
    if (!open) return;
    setNewGroup("");
    setDraft(
      parameter
        ? { ...parameter }
        : {
            guid: crypto.randomUUID(),
            name: "",
            dataType: "TEXT",
            dataCategory: "",
            groupId: groupId ?? store.groups[0]?.id ?? 0,
            visible: true,
            description: "",
            userModifiable: true,
            hideWhenNoValue: false,
          },
    );
  }, [open, parameter, groupId]);

  if (!draft) return null;
  const update = (changes: Partial<SharedParameterDto>) => setDraft({ ...draft, ...changes });
  const types = DATA_TYPES.some((type) => type.value === draft.dataType)
    ? DATA_TYPES
    : [...DATA_TYPES, { value: draft.dataType, label: draft.dataType }];
  const name = draft.name.trim().toLowerCase();
  const nameTaken = !!name && store.parameters.some((item) => item.guid !== draft.guid && item.name.trim().toLowerCase() === name);
  const groupMissing = !store.groups.some((group) => group.id === draft.groupId) && !newGroup.trim();
  const canSave = !!name && !nameTaken && !groupMissing;
  const groupName = store.groups.find((group) => group.id === draft.groupId)?.name ?? "";

  function apply() {
    if (!canSave || !draft) return;
    const target = newGroup.trim() ? store.addGroup(newGroup).id : draft.groupId;
    store.upsertParameter({ ...draft, groupId: target, name: draft.name.trim() });
    onClose();
  }

  return (
    <Dialog open={open} onOpenChange={(_, data) => !data.open && onClose()}>
      <DialogSurface style={{ maxWidth: "min(34rem, 95vw)" }}>
        <DialogBody>
          <DialogTitle>{isNew ? "New shared parameter" : "Edit shared parameter"}</DialogTitle>
          <DialogContent className="form">
            <Field
              label="Name"
              required
              validationState={nameTaken ? "error" : "none"}
              validationMessage={nameTaken ? "Another parameter in this file has this name." : undefined}
            >
              <Input
                autoFocus
                value={draft.name}
                onChange={(_, data) => update({ name: data.value })}
                onKeyDown={(event) => event.key === "Enter" && apply()}
              />
            </Field>
            <div className="form-row">
              <Field label="Data type" hint={isNew ? undefined : "Revit cannot change the type of an existing parameter."}>
                <Dropdown
                  disabled={!isNew}
                  value={types.find((type) => type.value === draft.dataType)?.label ?? draft.dataType}
                  selectedOptions={[draft.dataType]}
                  onOptionSelect={(_, data) => data.optionValue && update({ dataType: data.optionValue })}
                >
                  {types.map((type) => (
                    <Option key={type.value} value={type.value}>
                      {type.label}
                    </Option>
                  ))}
                </Dropdown>
              </Field>
              <Field label="Group">
                <Dropdown
                  disabled={!!newGroup.trim()}
                  placeholder="Pick a group"
                  value={groupName}
                  selectedOptions={[String(draft.groupId)]}
                  onOptionSelect={(_, data) => data.optionValue && update({ groupId: Number(data.optionValue) })}
                >
                  {store.groups.map((group) => (
                    <Option key={group.id} value={String(group.id)}>
                      {group.name}
                    </Option>
                  ))}
                </Dropdown>
              </Field>
            </div>
            <Field label="…or a new group">
              <Input value={newGroup} placeholder="New group name" onChange={(_, data) => setNewGroup(data.value)} />
            </Field>
            <Field label="Description" hint="The tooltip in Revit.">
              <Textarea value={draft.description} onChange={(_, data) => update({ description: data.value })} resize="vertical" />
            </Field>
            <div className="switch-row">
              <Switch label="Visible" checked={draft.visible} onChange={(_, data) => update({ visible: data.checked })} />
              <Switch
                label="User modifiable"
                checked={draft.userModifiable}
                onChange={(_, data) => update({ userModifiable: data.checked })}
              />
              <Switch
                label="Hide when empty"
                checked={draft.hideWhenNoValue}
                onChange={(_, data) => update({ hideWhenNoValue: data.checked })}
              />
            </div>
            <div className="mono muted small">GUID {draft.guid}</div>
          </DialogContent>
          <DialogActions>
            <Button onClick={onClose}>Cancel</Button>
            <Button appearance="primary" disabled={!canSave} onClick={apply}>
              {isNew ? "Add" : "Apply"}
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
}

// ---- Add file parameters to the project: categories, instance or type, group -------------------------

export function BindDialog({ open, rows, onClose }: { open: boolean; rows: ParameterRow[]; onClose: () => void }) {
  const store = useSharedParameters();
  const notify = useNotify();
  const [options, setOptions] = useState<BindingOptionsResult | null>(null);
  const [categoryIds, setCategoryIds] = useState<Set<number>>(new Set());
  const [isInstance, setIsInstance] = useState(true);
  const [groupTypeId, setGroupTypeId] = useState("");
  const [showAnnotation, setShowAnnotation] = useState(false);
  const [filter, setFilter] = useState("");
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (!open) return;
    let cancelled = false;
    // Start from what the parameters already have: adding a category to a bound parameter is the common case
    const bound = rows.map((row) => row.project).filter((project) => project?.bound);
    if (bound.length) setIsInstance(bound[0]!.isInstance);
    void store.loadBindingOptions().then((loaded) => {
      if (cancelled || !loaded) return;
      setOptions(loaded);
      setGroupTypeId((current) => current || loaded.defaultGroup);
      const already = new Set(bound.flatMap((project) => project!.categories));
      setCategoryIds(new Set(loaded.categories.filter((category) => already.has(category.name)).map((category) => category.id)));
    });
    setFilter("");
    return () => {
      cancelled = true;
    };
  }, [open, rows]);

  const categories = useMemo(
    () =>
      (options?.categories ?? [])
        .filter((category) => showAnnotation || category.type === "Model")
        .filter((category) => !filter.trim() || category.name.toLowerCase().includes(filter.trim().toLowerCase())),
    [options, showAnnotation, filter],
  );

  const toggle = (id: number, on: boolean) =>
    setCategoryIds((current) => {
      const next = new Set(current);
      if (on) next.add(id);
      else next.delete(id);
      return next;
    });

  async function apply() {
    setBusy(true);
    try {
      const result = await store.bind(
        rows.map((row) => row.guid!).filter(Boolean),
        [...categoryIds],
        isInstance,
        groupTypeId,
      );
      if (bindMessage(notify, result)) onClose();
    } catch (error) {
      notify.failed(error, "Could not add to the project");
    } finally {
      setBusy(false);
    }
  }

  const groupLabel = options?.groups.find((group) => group.typeId === groupTypeId)?.label ?? "";

  return (
    <Dialog open={open} onOpenChange={(_, data) => !data.open && onClose()}>
      <DialogSurface style={{ maxWidth: "min(38rem, 95vw)" }}>
        <DialogBody>
          <DialogTitle>Add to project</DialogTitle>
          <DialogContent className="form">
            <div className="muted">{rows.length === 1 ? rows[0].name : `${rows.length} parameters`}</div>
            {!options ? (
              <Spinner size="small" label="Reading the categories…" />
            ) : (
              <>
                <div className="form-row">
                  <Input
                    size="small"
                    className="grow"
                    contentBefore={<Search16Regular />}
                    placeholder="Filter categories"
                    value={filter}
                    onChange={(_, data) => setFilter(data.value)}
                  />
                  <Checkbox
                    label="Annotation categories"
                    checked={showAnnotation}
                    onChange={(_, data) => setShowAnnotation(!!data.checked)}
                  />
                </div>
                <div className="check-list">
                  {categories.map((category) => (
                    <Checkbox
                      key={category.id}
                      label={category.name}
                      checked={categoryIds.has(category.id)}
                      onChange={(_, data) => toggle(category.id, !!data.checked)}
                    />
                  ))}
                  {!categories.length && <div className="muted small pad">No category matches.</div>}
                </div>
                <div className="muted small">{categoryIds.size} selected</div>
                <div className="form-row">
                  <Field label="Binding">
                    <RadioGroup
                      layout="horizontal"
                      value={isInstance ? "instance" : "type"}
                      onChange={(_, data) => setIsInstance(data.value === "instance")}
                    >
                      <Radio value="instance" label="Instance" />
                      <Radio value="type" label="Type" />
                    </RadioGroup>
                  </Field>
                  <Field label="Group in Properties">
                    <Dropdown
                      value={groupLabel}
                      selectedOptions={[groupTypeId]}
                      onOptionSelect={(_, data) => data.optionValue && setGroupTypeId(data.optionValue)}
                    >
                      {options.groups.map((group) => (
                        <Option key={group.typeId} value={group.typeId}>
                          {group.label}
                        </Option>
                      ))}
                    </Dropdown>
                  </Field>
                </div>
              </>
            )}
          </DialogContent>
          <DialogActions>
            <Button onClick={onClose}>Cancel</Button>
            <Button appearance="primary" disabled={!categoryIds.size || busy} onClick={() => void apply()}>
              {busy ? "Adding…" : "Add to project"}
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
}

// ---- The one question a drop onto a category leaves open: instance or type -----------------------------

export function QuickBindDialog({
  open,
  guids,
  categoryName,
  onClose,
}: {
  open: boolean;
  guids: string[];
  categoryName: string;
  onClose: () => void;
}) {
  const store = useSharedParameters();
  const notify = useNotify();
  const [isInstance, setIsInstance] = useState(true);
  const [busy, setBusy] = useState(false);
  const wanted = new Set(guids.map((guid) => guid.toLowerCase()));
  const names = store.parameters.filter((item) => wanted.has(item.guid.toLowerCase())).map((item) => item.name);
  const existing = store.project.filter((item) => item.bound && item.guid && wanted.has(item.guid.toLowerCase()));
  // Only «save first» can be solved from here; the others are about which file is open
  const blocker = store.bindBlocker && !store.dirty ? store.bindBlocker : null;

  useEffect(() => {
    if (open) setIsInstance(existing.length ? existing[0].isInstance : true);
  }, [open]);

  async function apply() {
    setBusy(true);
    try {
      if (store.dirty && !(await store.save())) return;
      const options = await store.loadBindingOptions();
      const category = options?.categories.find((item) => item.name === categoryName);
      if (!category) {
        notify.error(`«${categoryName}» does not accept parameters`);
        return;
      }
      const result = await store.bind(guids, [category.id], isInstance, options!.defaultGroup);
      bindMessage(notify, result, `${categoryName}: `);
      onClose();
    } catch (error) {
      notify.failed(error, "Could not add to the project");
    } finally {
      setBusy(false);
    }
  }

  return (
    <Dialog open={open} onOpenChange={(_, data) => !data.open && onClose()}>
      <DialogSurface style={{ maxWidth: "min(28rem, 95vw)" }}>
        <DialogBody>
          <DialogTitle>Add to {categoryName}</DialogTitle>
          <DialogContent className="form">
            <div>
              <strong>{names.length === 1 ? names[0] : `${names.length} parameters`}</strong>
              {names.length > 1 && <div className="muted small">{names.join(", ")}</div>}
            </div>
            {blocker ? (
              <MessageBar intent="warning">
                <MessageBarBody>{blocker}</MessageBarBody>
              </MessageBar>
            ) : (
              <>
                <RadioGroup
                  layout="horizontal"
                  value={isInstance ? "instance" : "type"}
                  onChange={(_, data) => setIsInstance(data.value === "instance")}
                >
                  <Radio value="instance" label="Instance" />
                  <Radio value="type" label="Type" />
                </RadioGroup>
                {existing.length > 0 && (
                  <div className="muted small">
                    {existing.length === 1 ? "Already in the project" : `${existing.length} already in the project`} — they keep
                    their categories and gain {categoryName}.
                  </div>
                )}
                {store.dirty && <div className="warning-text small">The file has unsaved changes — it is saved first.</div>}
              </>
            )}
          </DialogContent>
          <DialogActions>
            <Button onClick={onClose}>Cancel</Button>
            <Button appearance="primary" disabled={!!blocker || busy} onClick={() => void apply()}>
              {busy ? "Adding…" : store.dirty ? "Save file and add" : "Add"}
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
}
