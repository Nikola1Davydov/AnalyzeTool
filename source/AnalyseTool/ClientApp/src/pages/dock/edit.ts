import { createContext } from "react";

/** The edit mode: the right mouse button starts it and, clicked again, ends it — as «Done» or Escape do. Tools and Tests share it. */
export const EditContext = createContext<{ editing: boolean; start: () => void }>({ editing: false, start: () => {} });
