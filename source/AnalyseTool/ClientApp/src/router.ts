import { useEffect, useState } from "react";

// Hash routing: every window (and the dock pane) of the add-in shows one route, `#/route?query`.
// IWindows.Open("/route") on the C# side, OpenWindow({ route }) from a page.

export type Route = { path: string; query: URLSearchParams };

function readRoute(): Route {
  const hash = location.hash.slice(1) || "/";
  const question = hash.indexOf("?");
  const path = question < 0 ? hash : hash.slice(0, question);
  return { path: path || "/", query: new URLSearchParams(question < 0 ? "" : hash.slice(question + 1)) };
}

/** The route of this window; changes with the hash. */
export function useRoute(): Route {
  const [route, setRoute] = useState(readRoute);
  useEffect(() => {
    const update = () => setRoute(readRoute());
    window.addEventListener("hashchange", update);
    return () => window.removeEventListener("hashchange", update);
  }, []);
  return route;
}

/** Goes to another route in this window. */
export function navigate(path: string, query?: Record<string, string | number | undefined | null>) {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(query ?? {})) {
    if (value !== undefined && value !== null && value !== "") params.set(key, String(value));
  }
  const text = params.toString();
  location.hash = text ? `${path}?${text}` : path;
}

/** The address of a route, for links. */
export function href(path: string) {
  return `#${path}`;
}
