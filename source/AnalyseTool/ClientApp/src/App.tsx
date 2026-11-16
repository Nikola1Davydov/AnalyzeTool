import { useEffect, useState } from "react";
import { bridge } from "revit-bridge";
import { DevPage } from "./dev/DevPage";
import { useActiveDocument, useCommandMutation } from "./revit/hooks";

type CategoryCount = { category: string; count: number };

/** The route of the page: `#/` in a window, `#/dock` in the dock pane (IWindows.Open / ShowDockPane). */
function useRoute() {
  const [route, setRoute] = useState(() => location.hash.slice(1) || "/");
  useEffect(() => {
    const update = () => setRoute(location.hash.slice(1) || "/");
    window.addEventListener("hashchange", update);
    return () => window.removeEventListener("hashchange", update);
  }, []);
  return route;
}

/** The sample page: the active document and one command with progress. */
export function App() {
  const route = useRoute();
  const document = useActiveDocument();
  const count = useCommandMutation<CategoryCount[]>("CountElementsByCategory");

  if (route === "/dev") return <DevPage />;

  return (
    <main className={route === "/dock" ? "page compact" : "page"}>
      <header>
        <h1>{route === "/dock" ? "Palette" : "Sample page"}</h1>
        {!bridge.inRevit && <span className="badge">mock — outside Revit</span>}
      </header>

      <section>
        <h2>Active document</h2>
        {document.isPending && <p>Loading…</p>}
        {document.isError && <p className="error">{document.error.message}</p>}
        {document.isSuccess && <p>{document.data?.title ?? "No document is open."}</p>}
        <button onClick={() => void document.refetch()}>Refresh</button>
      </section>

      <section>
        <h2>Elements by category</h2>
        <button disabled={!document.data || count.isPending} onClick={() => count.mutate(undefined)}>
          Count elements
        </button>
        {count.isPending && <button onClick={count.cancel}>Cancel</button>}
        {count.progress && (
          <p>
            <progress value={count.progress.fraction} /> {count.progress.message}
          </p>
        )}
        {count.isError && <p className="error">{count.error.message}</p>}
        {count.isSuccess && (
          <table>
            <tbody>
              {count.data.map((item) => (
                <tr key={item.category}>
                  <td>{item.category}</td>
                  <td>{item.count}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>
    </main>
  );
}
