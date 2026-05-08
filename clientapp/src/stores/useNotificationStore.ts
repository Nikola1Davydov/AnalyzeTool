import { defineStore } from "pinia";
import { ref } from "vue";

export type NotifSeverity = "error" | "warn" | "info" | "success";

export interface Notification {
  severity: NotifSeverity;
  summary: string;
  detail?: string;
}

export const useNotificationStore = defineStore("notification", () => {
  const pending = ref<Notification | null>(null);

  function notify(severity: NotifSeverity, summary: string, detail?: string) {
    pending.value = { severity, summary, detail };
  }

  function error(detail: string) {
    notify("error", "Fehler", detail);
  }
  function warn(detail: string) {
    notify("warn", "Warnung", detail);
  }
  function success(detail: string) {
    notify("success", "Erfolg", detail);
  }
  function info(detail: string) {
    notify("info", "Info", detail);
  }

  return { pending, notify, error, warn, success, info };
});
