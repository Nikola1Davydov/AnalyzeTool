<script setup>
import { computed, ref } from "vue";
import { storeToRefs } from "pinia";
import { useElements } from "@/stores/useElements";
const { filtered, selectedCategory } = storeToRefs(useElements());

const categories = computed(() => {
  const list = Array.isArray(filtered.value) ? filtered.value : [];
  const set = new Set(list.map((e) => e?.CategoryName).filter(Boolean));
  return Array.from(set); // ["Walls","Floors", ...]
});

// агрегированная таблица для TreeTable
const tableData = computed(() => {
  console.log(selectedCategory.value);
  if (!selectedCategory.value) return [];
  // берём только элементы выбранной категории
  const elements = filtered.value.filter(
    (e) => e.CategoryName === selectedCategory.value
  );

  // собираем все параметры в один массив
  const allParams = elements.flatMap((el) => el.Parameters || []);

  // группировка по имени параметра
  const groupedByName = {};
  for (const p of allParams) {
    if (!groupedByName[p.Name]) groupedByName[p.Name] = [];
    groupedByName[p.Name].push(p);
  }

  // верхний уровень (параметры)
  return Object.entries(groupedByName).map(([paramName, params]) => {
    const filled = params.filter((p) => p.Value).map((p) => p.elementId);
    const empty = params.filter((p) => !p.Value).map((p) => p.elementId);

    const percent =
      params.length > 0
        ? Math.round((filled.length / params.length) * 10000) / 100
        : 0;

    return {
      key: paramName,
      data: {
        parameter: paramName ?? "",
        count: params.length ?? 0,
        parameterEmpty: empty.length ?? 0,
        parameterFilled: filled.length ?? 0,
        parameterFilledPercent: percent,
      },
    };
  });
});
</script>

<template>
  <div class="app-container flex flex-col h-full w-full">
    <TreeTable
      tableStyle="min-width: 50rem"
      :value="tableData"
      :filters="filters"
    >
      <Column field="parameter" header="Parameter"></Column>
      <Column field="count" header="Count"></Column>
      <Column field="parameterEmpty" header="Parameter Empty"></Column>
      <Column field="parameterFilled" header="Parameter Filled"> </Column>
      <Column field="parameterFilledPercent" header="Parameter Filled in %">
      </Column>
    </TreeTable>
  </div>
</template>
