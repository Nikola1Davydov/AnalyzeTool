// Naming rules (templates) + the office-wide abbreviation dictionary, persisted in localStorage.
// Rules are scoped: "type" rules compose type names from parameters, "family" rules (later) compose
// family names. The dictionary is SHARED across all rules — abbreviations are an office convention,
// not part of one template. Export/import lets a convention travel between machines.

import { reactive, watch } from "vue";
import type { AbbreviationDict } from "./namingEngine";

export type NamingScope = "type" | "family";

export interface NamingRule {
  id: string;
  name: string;
  scope: NamingScope;
  template: string;
}

interface NamingStore {
  rules: NamingRule[];
  abbreviations: AbbreviationDict; // full value (lowercased) -> abbreviation
}

const STORAGE_KEY = "analysetool.namingRules.v1";

function load(): NamingStore {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (raw) {
      const parsed = JSON.parse(raw);
      return {
        rules: Array.isArray(parsed.rules) ? parsed.rules : [],
        abbreviations: parsed.abbreviations && typeof parsed.abbreviations === "object" ? parsed.abbreviations : {},
      };
    }
  } catch {
    /* corrupted storage — start clean */
  }
  return { rules: [], abbreviations: {} };
}

const store = reactive<NamingStore>(load());
watch(store, (s) => localStorage.setItem(STORAGE_KEY, JSON.stringify(s)), { deep: true });

export function useNamingRules() {
  function rulesFor(scope: NamingScope): NamingRule[] {
    return store.rules.filter((r) => r.scope === scope);
  }

  function saveRule(rule: NamingRule) {
    const i = store.rules.findIndex((r) => r.id === rule.id);
    if (i >= 0) store.rules[i] = { ...rule };
    else store.rules.push({ ...rule });
  }

  function deleteRule(id: string) {
    const i = store.rules.findIndex((r) => r.id === id);
    if (i >= 0) store.rules.splice(i, 1);
  }

  function setAbbreviation(full: string, abbr: string) {
    const key = full.trim().toLowerCase();
    if (!key) return;
    if (abbr.trim()) store.abbreviations[key] = abbr.trim();
    else delete store.abbreviations[key];
  }

  function newId(): string {
    return `nr-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`;
  }

  /** The whole convention (rules + dictionary) as a JSON string, for sharing across the office. */
  function exportJson(): string {
    return JSON.stringify({ rules: store.rules, abbreviations: store.abbreviations }, null, 2);
  }

  /** Merges an exported convention: rules are added (id-collisions replaced), abbreviations merged. */
  function importJson(json: string): { rules: number; abbreviations: number } {
    const parsed = JSON.parse(json);
    let rules = 0;
    if (Array.isArray(parsed.rules)) {
      for (const r of parsed.rules) {
        if (r && typeof r.template === "string" && typeof r.name === "string") {
          saveRule({ id: r.id ?? newId(), name: r.name, scope: r.scope === "family" ? "family" : "type", template: r.template });
          rules++;
        }
      }
    }
    let abbrs = 0;
    if (parsed.abbreviations && typeof parsed.abbreviations === "object") {
      for (const [k, v] of Object.entries(parsed.abbreviations)) {
        if (typeof v === "string") {
          store.abbreviations[k.toLowerCase()] = v;
          abbrs++;
        }
      }
    }
    return { rules, abbreviations: abbrs };
  }

  return {
    store,
    rulesFor,
    saveRule,
    deleteRule,
    setAbbreviation,
    newId,
    exportJson,
    importJson,
  };
}
