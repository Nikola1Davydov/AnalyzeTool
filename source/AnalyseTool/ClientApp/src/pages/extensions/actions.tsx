import {
  type ExtensionList,
  type ExtensionSummary,
  type InstallExtensionPackageRequest,
  type PackageInstallResult,
  type RemoveExtensionResult,
  type SetExtensionEnabledRequest,
  extensionCommands,
} from "../../api/extensions";
import { useConfirm } from "../../components/confirm";
import { useNotify } from "../../components/notify";
import { useAppAction } from "../../components/useAppAction";

/** Turn on/off, reload and remove — the actions of the list and of the detail page. */
export function useExtensionActions(onRemoved?: () => void) {
  const confirm = useConfirm();

  const setEnabled = useAppAction<ExtensionSummary | null, SetExtensionEnabledRequest>(extensionCommands.SetExtensionEnabled, {
    success: (_, request) => (request.enabled ? "Turned on" : "Turned off — its buttons and commands are gone until you turn it on"),
  });

  const reload = useAppAction<ExtensionList, { id?: string }>(extensionCommands.ReloadExtensions, {
    success: (_, request) => (request.id ? `Reloaded ${request.id}` : "Extensions reloaded"),
    failure: "Could not reload",
  });

  const removeAction = useAppAction<RemoveExtensionResult, { id: string }>(extensionCommands.RemoveExtension, {
    success: (result) => `Removed. The folder is now in ${result.movedTo}`,
    failure: "Could not remove",
    onSuccess: () => onRemoved?.(),
  });

  async function remove(extension: ExtensionSummary) {
    const ok = await confirm({
      title: `Remove «${extension.name}»?`,
      message: (
        <>
          <p>Its ribbon buttons and commands go away.</p>
          <p>
            The folder <code className="path">{extension.directory}</code> moves to the AnalyseTool trash (
            <code>%LOCALAPPDATA%\AnalyseTool\trash</code>) together with its saved versions — nothing is deleted for good, you
            can move it back.
          </p>
        </>
      ),
      confirmLabel: "Remove",
      danger: true,
    });
    if (ok) removeAction.mutate({ id: extension.id });
  }

  return { setEnabled, reload, remove, removing: removeAction.isPending };
}

/** Installs a .zip package into the managed folder; asks before replacing an installed version. */
export function useInstallPackage() {
  const confirm = useConfirm();
  const notify = useNotify();
  const install = useAppAction<PackageInstallResult, InstallExtensionPackageRequest>(extensionCommands.InstallExtensionPackage, {
    failure: "Could not install the package",
  });

  async function run(path: string) {
    const result = await install.mutateAsync({ path });
    if (result.alreadyInstalled) {
      const ok = await confirm({
        title: `«${result.extension?.name ?? result.id}» is installed already`,
        message: (
          <p>
            Replace it with version {result.version} from the package? The previous version stays in the history of its folder,
            so it can be restored.
          </p>
        ),
        confirmLabel: "Replace",
      });
      if (!ok) return;
      const replaced = await install.mutateAsync({ path, overwrite: true });
      notify.success(`Replaced ${replaced.id} with version ${replaced.version}`);
      return;
    }
    notify.success(`Installed ${result.id} ${result.version}`, result.directory);
  }

  return {
    busy: install.isPending,
    run: (path: string) => run(path).catch(() => undefined),
  };
}
