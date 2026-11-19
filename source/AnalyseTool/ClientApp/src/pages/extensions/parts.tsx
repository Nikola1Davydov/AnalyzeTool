import { Badge, Tooltip } from "@fluentui/react-components";
import { ErrorCircle12Regular, Warning12Regular } from "@fluentui/react-icons";
import {
  type CompileDiagnostic,
  type ExtensionSummary,
  type ExtensionVersions,
  extensionCommands,
  kindLabels,
} from "../../api/extensions";
import { plural } from "../../components/format";
import { useAppQuery } from "../../revit/hooks";

export function KindBadge({ extension }: { extension: ExtensionSummary }) {
  return (
    <span className="badges">
      <Badge appearance="outline" color={extension.kind === "Source" ? "brand" : "informative"} size="small">
        {kindLabels[extension.kind]}
      </Badge>
      {extension.zone === "Managed" && (
        <Tooltip content="Installed by the extension manager" relationship="description">
          <Badge appearance="outline" color="subtle" size="small">
            installed
          </Badge>
        </Tooltip>
      )}
    </span>
  );
}

/** The versions of one extension: read for the badge of its row. */
export function useExtensionVersions(id: string, enabled = true) {
  return useAppQuery<ExtensionVersions>(extensionCommands.GetExtensionVersions, { id }, { enabled });
}

/** Pinned / unsaved changes / saved: what the versions of an extension say at a glance. */
export function VersionBadge({ extension }: { extension: ExtensionSummary }) {
  const versions = useExtensionVersions(extension.id, extension.zone === "Dev");
  if (extension.loadedVersion) {
    return (
      <Tooltip content="The button runs a saved version, not the current files" relationship="description">
        <Badge appearance="filled" color="warning" size="small">
          pinned {extension.loadedVersion.slice(0, 7)}
        </Badge>
      </Tooltip>
    );
  }
  const info = versions.data?.info;
  if (!info?.available) return null;
  if (info.changes.length) {
    return (
      <Tooltip content={info.changes.map((change) => `${change.state}: ${change.file}`).join("\n")} relationship="description">
        <Badge appearance="tint" color="important" size="small">
          {plural(info.changes.length, "unsaved change")}
        </Badge>
      </Tooltip>
    );
  }
  if (!info.hasHistory) {
    return (
      <Badge appearance="tint" color="subtle" size="small">
        no versions
      </Badge>
    );
  }
  return (
    <Tooltip content={info.latest ? `Latest: ${info.latest.message}` : "Saved"} relationship="description">
      <Badge appearance="tint" color="success" size="small">
        saved
      </Badge>
    </Tooltip>
  );
}

/** Problems of an extension at a glance: errors, compiler errors and warnings, an older build. */
export function StatusBadges({ extension }: { extension: ExtensionSummary }) {
  const errors = extension.diagnostics.filter((diagnostic) => diagnostic.severity === "error").length;
  const warnings = extension.diagnostics.length - errors;
  return (
    <span className="badges">
      {!extension.enabled && (
        <Badge appearance="tint" color="subtle" size="small">
          off
        </Badge>
      )}
      {!extension.compatible && (
        <Badge appearance="tint" color="danger" size="small">
          not for this Revit
        </Badge>
      )}
      {extension.errors.length > 0 && (
        <Badge appearance="filled" color="danger" size="small">
          {plural(extension.errors.length, "error")}
        </Badge>
      )}
      {errors > 0 && (
        <Badge appearance="tint" color="danger" size="small" icon={<ErrorCircle12Regular />}>
          {errors}
        </Badge>
      )}
      {warnings > 0 && (
        <Badge appearance="tint" color="warning" size="small" icon={<Warning12Regular />}>
          {warnings}
        </Badge>
      )}
      {extension.usesOlderBuild && (
        <Tooltip content="The current sources do not compile; the last build that did is loaded" relationship="description">
          <Badge appearance="tint" color="warning" size="small">
            older build
          </Badge>
        </Tooltip>
      )}
    </span>
  );
}

/** `file(line,col): code message`, each clickable to show the line. */
export function DiagnosticsList({
  diagnostics,
  onOpen,
  max,
}: {
  diagnostics: CompileDiagnostic[];
  onOpen?: (diagnostic: CompileDiagnostic) => void;
  max?: number;
}) {
  const shown = max ? diagnostics.slice(0, max) : diagnostics;
  return (
    <ul className="diagnostics">
      {shown.map((diagnostic, index) => (
        <li key={index} className={diagnostic.severity === "error" ? "error" : "warning"}>
          {diagnostic.severity === "error" ? <ErrorCircle12Regular /> : <Warning12Regular />}
          {onOpen ? (
            <button type="button" className="link-button mono" onClick={() => onOpen(diagnostic)}>
              {diagnostic.file}({diagnostic.line},{diagnostic.column})
            </button>
          ) : (
            <span className="mono">
              {diagnostic.file}({diagnostic.line},{diagnostic.column})
            </span>
          )}
          <span className="mono muted">{diagnostic.code}</span>
          <span>{diagnostic.message}</span>
        </li>
      ))}
      {max && diagnostics.length > max && <li className="muted">… and {diagnostics.length - max} more</li>}
    </ul>
  );
}

export function ChangeStateBadge({ state }: { state: string }) {
  const color = state === "added" ? "success" : state === "deleted" ? "danger" : state === "renamed" ? "informative" : "warning";
  return (
    <Badge appearance="tint" color={color} size="small" className="state-badge">
      {state}
    </Badge>
  );
}
