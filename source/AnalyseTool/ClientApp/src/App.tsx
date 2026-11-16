import { Spinner } from "@fluentui/react-components";
import { lazy, type ReactNode, Suspense } from "react";
import { AppFrame } from "./components/AppFrame";
import { Empty, Page } from "./components/Page";
import { useRoute } from "./router";

// Every window (and the dock pane) shows one route, so each page is its own chunk: the narrow dock pane does not
// parse the code of the report or the diff viewer.
const DevPage = lazy(() => import("./dev/DevPage").then((m) => ({ default: m.DevPage })));
const MainWindow = lazy(() => import("./pages/tools/MainWindow").then((m) => ({ default: m.MainWindow })));
const ExtensionsPage = lazy(() => import("./pages/extensions/ExtensionsPage").then((m) => ({ default: m.ExtensionsPage })));
const NewExtensionPage = lazy(() =>
  import("./pages/extensions/NewExtensionPage").then((m) => ({ default: m.NewExtensionPage })),
);
const ExtensionDetailPage = lazy(() =>
  import("./pages/extensions/ExtensionDetailPage").then((m) => ({ default: m.ExtensionDetailPage })),
);
const SettingsPage = lazy(() => import("./pages/settings/SettingsPage").then((m) => ({ default: m.SettingsPage })));
const AboutPage = lazy(() => import("./pages/about/AboutPage").then((m) => ({ default: m.AboutPage })));
const DockPage = lazy(() => import("./pages/dock/DockPage").then((m) => ({ default: m.DockPage })));

function routePage(path: string, query: URLSearchParams): ReactNode {
  switch (path) {
    case "/":
    case "/report":
    case "/elements":
      return <MainWindow path={path} query={query} />;
    case "/dev":
      return <DevPage />;
    case "/dock":
      return <DockPage />;
    case "/extensions":
      return <ExtensionsPage />;
    case "/extensions/new":
      return <NewExtensionPage />;
    case "/settings":
      return <SettingsPage />;
    case "/about":
      return <AboutPage />;
  }
  if (path.startsWith("/extensions/")) {
    const id = decodeURIComponent(path.slice("/extensions/".length));
    return <ExtensionDetailPage key={id} id={id} query={query} />;
  }
  return (
    <Page title="Not found" back={{ label: "AnalyseTool", path: "/" }}>
      <Empty>There is no page at {path}.</Empty>
    </Page>
  );
}

/** The page of this window, by its hash route (IWindows.Open / ShowDockPane on the C# side, OpenWindow from a page). */
export function App() {
  const { path, query } = useRoute();
  return (
    <AppFrame>
      <Suspense fallback={<Spinner className="page-spinner" />}>{routePage(path, query)}</Suspense>
    </AppFrame>
  );
}
