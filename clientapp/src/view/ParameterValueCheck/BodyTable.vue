<script setup lang="ts">
import { computed } from "vue";
import type { ElementItem } from "@/stores/types";

const props = defineProps<{
  items: ElementItem[];
}>();

// Group by parameter and parameter value for the table view
const tableData = computed(() => {
  if (!props.items || !Array.isArray(props.items)) return [];

  const grouped = new Map<string, Map<string, Set<number>>>();

  for (const element of props.items) {
    const elementId = Number((element as any).id ?? (element as any).elementId);
    const parameters = (element as any).parameters ?? [];

    for (const param of parameters) {
      const paramName = param?.name ?? "Unknown";
      const rawValue = param?.value;
      const valueLabel =
        rawValue === undefined || rawValue === null || rawValue === "" ? "(empty)" : String(rawValue);

      if (!grouped.has(paramName)) grouped.set(paramName, new Map());
      const valueMap = grouped.get(paramName)!;

      if (!valueMap.has(valueLabel)) valueMap.set(valueLabel, new Set());
      valueMap.get(valueLabel)!.add(Number(param.elementId ?? elementId));
    }
  }

  return Array.from(grouped.entries()).map(([parameter, values]) => {
    const children = Array.from(values.entries())
      .map(([value, ids]) => ({
        key: `${parameter}-${value}`,
        data: {
          parameter,
          value,
          count: ids.size,
          elementIds: Array.from(ids),
        },
      }))
      .sort((a, b) => b.data.count - a.data.count);

    return {
      key: parameter,
      data: {
        parameter,
        value: "",
        count: children.reduce((sum, child) => sum + (child.data.count ?? 0), 0),
      },
      children,
    };
  });
});
</script>

<template>
  <div class="app-container flex flex-col h-full w-full">
    <TreeTable tableStyle="min-width: 40rem" :value="tableData">
      <Column field="parameter" header="Parameter" />
      <Column field="value" header="Value" />
      <Column field="count" header="Count" />
    </TreeTable>
  </div>
</template>
