import { Badge, Link, Text } from "@fluentui/react-components";
import { ArrowLeft16Regular } from "@fluentui/react-icons";
import type { ReactNode } from "react";
import { bridge } from "revit-bridge";
import { href } from "../router";

type PageProps = {
  title: ReactNode;
  subtitle?: ReactNode;
  /** A link back, e.g. to the list. */
  back?: { label: string; path: string };
  actions?: ReactNode;
  children: ReactNode;
  /** Narrow content column (forms, About). */
  narrow?: boolean;
  compact?: boolean;
};

/** A page of its own window: title, actions on the right, the content below. */
export function Page({ title, subtitle, back, actions, children, narrow, compact }: PageProps) {
  return (
    <div className={`app-page${compact ? " compact" : ""}`}>
      <div className={narrow ? "app-page-inner narrow" : "app-page-inner"}>
        {back && (
          <Link href={href(back.path)} className="back-link">
            <ArrowLeft16Regular /> {back.label}
          </Link>
        )}
        <header className="app-page-header">
          <div className="app-page-title">
            <Text as="h1" size={compact ? 400 : 600} weight="semibold">
              {title}
            </Text>
            {!bridge.inRevit && (
              <Badge appearance="tint" color="informative" size="small">
                mock — outside Revit
              </Badge>
            )}
          </div>
          {actions && <div className="app-page-actions">{actions}</div>}
        </header>
        {subtitle && <div className="app-page-subtitle">{subtitle}</div>}
        {children}
      </div>
    </div>
  );
}

/** A grey line of text for an empty list or a hint. */
export function Empty({ children }: { children: ReactNode }) {
  return <div className="empty">{children}</div>;
}
