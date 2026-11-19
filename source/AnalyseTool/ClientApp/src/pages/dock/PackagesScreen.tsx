import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { bridge } from "revit-bridge";
import { browseFile, openPath } from "../../api/app";
import {
  type ExtensionList,
  type ExtensionSummary,
  type InstallExtensionPackageRequest,
  type PackageInstallResult,
  type RemoveExtensionResult,
  extensionCommands,
} from "../../api/extensions";
import { messageOf, useActions } from "./Actions";
import { TileIcon } from "./Tile";

/** After a change: the pane, the lists and the statistics read again — not the profile (the pane owns it). */
function useRefresh() {
  const client = useQueryClient();
  return () => void client.invalidateQueries({ predicate: (query) => query.queryKey[0] !== "paneProfile" });
}

/**
 * Packages: the extensions someone shared as a .zip, installed into extensions-dist. Your own folders are managed where they
 * are — on the Folders screen; here only what comes from outside: install, turn off, remove.
 */
export function PackagesScreen() {
  const actions = useActions();
  const refresh = useRefresh();
  const list = useQuery({
    queryKey: ["packages"],
    queryFn: ({ signal }) => bridge.invoke<ExtensionList>(extensionCommands.GetExtensions, null, { signal }),
  });
  const packages = (list.data?.extensions ?? []).filter((extension) => extension.zone === "Managed");

  const install = useMutation({
    mutationFn: (request: InstallExtensionPackageRequest) => bridge.invoke<PackageInstallResult>(extensionCommands.InstallExtensionPackage, request),
  });
  const setEnabled = useMutation({
    mutationFn: (request: { id: string; enabled: boolean }) => bridge.invoke(extensionCommands.SetExtensionEnabled, request),
    onSuccess: refresh,
    onError: (error) => actions.toast(messageOf(error), "error"),
  });
  const remove = useMutation({
    mutationFn: (id: string) => bridge.invoke<RemoveExtensionResult>(extensionCommands.RemoveExtension, { id }),
    onSuccess: (result) => {
      refresh();
      actions.toast(`Removed — the folder is in ${result.movedTo}`);
    },
    onError: (error) => actions.toast(messageOf(error), "error"),
  });

  async function installPackage() {
    try {
      const path = await browseFile({ title: "Install a package", filter: "Extension package|*.zip" });
      if (!path) return;
      let result = await install.mutateAsync({ path });
      if (result.alreadyInstalled) {
        if (!confirm(`«${result.extension?.name ?? result.id}» is installed already. Replace it with version ${result.version}? The previous version stays in its history.`)) return;
        result = await install.mutateAsync({ path, overwrite: true });
      }
      refresh();
      actions.toast(`${result.replaced ? "Updated" : "Installed"} ${result.extension?.name ?? result.id} ${result.version}`);
    } catch (error) {
      actions.toast(messageOf(error), "error");
    }
  }

  return (
    <div className="profile packages">
      <section className="link-section">
        <button className="row-link" disabled={install.isPending} onClick={() => void installPackage()}>
          <span>📦 {install.isPending ? "Installing…" : "Install a package…"}</span>
          <span className="chevron-right">›</span>
        </button>
        <p className="muted small">A .zip of an extension someone shared. Its buttons come as a folder; every install is a version, so an update can be undone.</p>
      </section>

      <section>
        <h2>Installed</h2>
        {list.isPending && <p className="muted small">Loading…</p>}
        {list.isError && <p className="error small">{list.error.message}</p>}
        {list.isSuccess && packages.length === 0 && <p className="muted small">No packages yet.</p>}
        <ul className="dock-list package-list">
          {packages.map((extension) => (
            <PackageRow
              key={extension.id}
              extension={extension}
              busy={setEnabled.isPending || remove.isPending}
              onEnabled={(enabled) => setEnabled.mutate({ id: extension.id, enabled })}
              onRemove={() => {
                if (confirm(`Remove «${extension.name}»? Its buttons go; the folder moves to the AnalyseTool trash with its versions.`)) remove.mutate(extension.id);
              }}
            />
          ))}
        </ul>
      </section>
    </div>
  );
}

function PackageRow(props: { extension: ExtensionSummary; busy: boolean; onEnabled: (enabled: boolean) => void; onRemove: () => void }) {
  const { extension } = props;
  const actions = useActions();
  const problems = extension.errors.length + extension.diagnostics.filter((diagnostic) => diagnostic.severity === "error").length;
  return (
    <li className={extension.enabled ? undefined : "off"}>
      <TileIcon title={extension.name} />
      <span className="dock-name">
        {extension.name}
        <span className="muted small">
          {" "}
          · v{extension.version}
          {extension.publisher ? ` · ${extension.publisher}` : ""}
          {!extension.compatible ? " · not for this Revit" : ""}
        </span>
        {problems > 0 && <span className="error small"> · {problems} problem(s)</span>}
      </span>
      <button className="icon-button" title="Open its folder" onClick={() => void openPath(extension.directory).catch((error: unknown) => actions.toast(messageOf(error), "error"))}>
        📁
      </button>
      <button
        role="switch"
        aria-checked={extension.enabled}
        title={extension.enabled ? "Turn off" : "Turn on"}
        className={`switch small ${extension.enabled ? "on" : ""}`}
        disabled={props.busy}
        onClick={() => props.onEnabled(!extension.enabled)}
      >
        <span className="switch-knob" />
      </button>
      <button className="icon-button" title="Remove" disabled={props.busy} onClick={props.onRemove}>
        ✕
      </button>
    </li>
  );
}
