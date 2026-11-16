import { Badge, Link, Text } from "@fluentui/react-components";
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

/**
 * A page of its own window, in the look of the dock pane: a frosted navigation bar with «‹ back» when there is a way back,
 * a large title with the actions beside it, the content on the grouped background.
 */
export function Page({ title, subtitle, back, actions, children, narrow, compact }: PageProps) {
  return (
    <div className={`app-page${compact ? " compact" : ""}`}>
      {back && (
        <nav className="app-nav glass-bar">
          <Link href={href(back.path)} className="app-nav-back">
            <span className="chevron">‹</span>
            {back.label}
          </Link>
        </nav>
      )}
      <div className={narrow ? "app-page-inner narrow" : "app-page-inner"}>
        <header className="app-page-header">
          <div className="app-page-title">
            <Text as="h1" className="large-title">
              {title}
            </Text>
            {!bridge.inRevit && (
              <Badge appearance="tint" color="informative" size="small">
                mock
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
