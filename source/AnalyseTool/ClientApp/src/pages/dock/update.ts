import { useQuery } from "@tanstack/react-query";
import { bridge } from "revit-bridge";
import { type UpdateInfo, appCommands } from "../../api/app";

/** Whether a newer AnalyseTool is released. The host asks GitHub at most once a day; the pane asks the host once an hour. */
export function useUpdate() {
  return useQuery({
    queryKey: ["update"],
    queryFn: ({ signal }) => bridge.invoke<UpdateInfo>(appCommands.GetUpdate, null, { signal }),
    staleTime: 60 * 60 * 1000,
    refetchInterval: 60 * 60 * 1000,
    retry: false,
  });
}
