<script setup>
import { computed, ref } from "vue";

const props = defineProps({
  items: { type: Array, default: () => [] },
});

function SelectionInRevit(data) {
  const message = {
    commandsEnum: "Isolation",
    jsonData: data,
  };

  console.log("Send to Revit", message);

  // Отправить сообщение обратно в Revit
  if (window.chrome?.webview) {
    window.chrome.webview.postMessage(message);
  } else {
    console.warn("WebView not available");
  }
}

// агрегированная таблица для TreeTable
const tableData = computed(() => {
  if (!props.items || !Array.isArray(props.items)) return [];

  // собираем все параметры в один массив
  const allParams = props.items.flatMap((el) => el.Parameters || []);

  // группировка по имени параметра
  const groupedByName = {};
  for (const p of allParams) {
    if (!groupedByName[p.Name]) groupedByName[p.Name] = [];
    groupedByName[p.Name].push(p);
  }

  // верхний уровень (параметры)
  const result = Object.entries(groupedByName).map(([paramName, params]) => {
    const filled = params.filter((p) => p.Value).map((p) => p.ElementId);
    const empty = params.filter((p) => !p.Value).map((p) => p.ElementId);

    const percent =
      params.length > 0 ? Math.round((filled.length / params.length) * 10000) / 100 : 0;

    console.log(`Param: ${paramName}`, {
      totalCount: params.length,
      filledCount: filled.length,
      emptyCount: empty.length,
      filled,
      empty,
    });

    return {
      key: paramName,
      data: {
        parameter: paramName ?? "",
        count: params.length ?? 0,
        parameterEmpty: empty.length ?? 0,
        parameterFilled: filled.length ?? 0,
        parameterFilledPercent: percent,
        filledIds: filled, // массив ID заполненных элементов
        emptyIds: empty, // массив ID пустых элементов
      },
    };
  });

  console.log("Table data computed:", result);
  return result;
});
</script>

<template>
  <div class="app-container flex flex-col h-full w-full">
    <TreeTable tableStyle="min-width: 50rem" :value="tableData">
      <Column field="parameter" header="Parameter"></Column>
      <Column field="count" header="Count"></Column>
      <Column field="parameterEmpty" header="Parameter Empty"></Column>
      <Column field="parameterFilled" header="Parameter Filled"> </Column>
      <Column field="parameterFilledPercent" header="Parameter Filled in %"> </Column>
      <Column header="Selection">
        <template #body="slotProps">
          <div class="flex flex-wrap gap-2">
            <Button
              type="button"
              rounded
              icon="pi pi-check"
              title="Select filled"
              @click="SelectionInRevit(slotProps.node.data.filledIds)"
            />
            <Button
              type="button"
              rounded
              severity="danger"
              icon="pi pi-times"
              title="Select empty"
              @click="SelectionInRevit(slotProps.node.data.emptyIds)"
            />
          </div>
        </template>
      </Column>
    </TreeTable>
  </div>
</template>
