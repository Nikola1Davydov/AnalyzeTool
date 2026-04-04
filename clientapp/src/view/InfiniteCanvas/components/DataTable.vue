<script setup lang="ts">
import { computed } from "vue";
import type { ElementItem } from "@/stores/types";

const props = defineProps<{
  items: ElementItem[];
  selectedParameter?: string | null;
}>();

const rows = computed(() => {
  return (props.items || []).map((element) => {
    const selectedValue = (element.parameters || []).find(
      (p) => p?.name === props.selectedParameter,
    )?.value;

    return {
      id: Number((element as any).id ?? 0),
      name: String((element as any).name ?? ""),
      level: String((element as any).level ?? ""),
      category: String((element as any).categoryName ?? ""),
      parameterValue:
        selectedValue === undefined || selectedValue === null || selectedValue === ""
          ? "(empty)"
          : String(selectedValue),
    };
  });
});

const totalCount = computed(() => rows.value.length);
</script>

<template>
  <div class="table-wrap">
    <table class="table">
      <thead>
        <tr>
          <th>ID</th>
          <th>Name</th>
          <th>Level</th>
          <th>Category</th>
          <th>{{ selectedParameter || "Selected Parameter" }}</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="row in rows" :key="row.id">
          <td>{{ row.id }}</td>
          <td>{{ row.name }}</td>
          <td>{{ row.level }}</td>
          <td>{{ row.category }}</td>
          <td>{{ row.parameterValue }}</td>
        </tr>
      </tbody>
      <tfoot>
        <tr>
          <td colspan="5">Total elements: {{ totalCount }}</td>
        </tr>
      </tfoot>
    </table>
  </div>
</template>

<style scoped>
.table-wrap {
  width: 100%;
  height: 100%;
  overflow: auto;
}

.table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.78rem;
}

th,
td {
  border-bottom: 1px solid var(--p-surface-200, #e2e8f0);
  padding: 0.35rem 0.3rem;
  text-align: left;
  vertical-align: top;
}

th {
  position: sticky;
  top: 0;
  z-index: 1;
  background: var(--p-surface-50, #f8fafc);
  color: var(--p-surface-700, #334155);
  font-weight: 600;
}

td {
  color: var(--p-surface-800, #1e293b);
}

tfoot td {
  position: sticky;
  bottom: 0;
  background: var(--p-surface-100, #f1f5f9);
  font-weight: 700;
  color: var(--p-surface-800, #1e293b);
}
</style>
