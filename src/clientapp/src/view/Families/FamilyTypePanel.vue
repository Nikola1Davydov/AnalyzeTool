<script setup lang="ts">
import { ref, computed, watch, onMounted } from "vue";
import { invoke } from "@/RevitBridge";

interface TypeParam {
  name: string;
  value: string;
}
interface FamilyType {
  id: number;
  name: string;
  instanceCount: number;
  parameters: TypeParam[];
}
interface FamilyTypes {
  familyId: number;
  name: string;
  category: string;
  types: FamilyType[];
}

const props = defineProps<{ familyId: number }>();

const data = ref<FamilyTypes | null>(null);
const loading = ref(false);
const selectedId = ref<number | null>(null);
const paramSearch = ref("");

async function load() {
  loading.value = true;
  try {
    data.value = await invoke<FamilyTypes>("GetFamilyTypes", { id: props.familyId });
    selectedId.value = data.value?.types?.[0]?.id ?? null;
  } catch (e) {
    console.error("Failed to load family types", e);
    data.value = null;
  } finally {
    loading.value = false;
  }
}

const selectedType = computed(
  () => data.value?.types.find((t) => t.id === selectedId.value) ?? null,
);
const filteredParams = computed(() => {
  const t = selectedType.value;
  if (!t) return [];
  const q = paramSearch.value.trim().toLowerCase();
  if (!q) return t.parameters;
  return t.parameters.filter(
    (p) => p.name.toLowerCase().includes(q) || p.value.toLowerCase().includes(q),
  );
});

watch(() => props.familyId, load);
onMounted(load);
</script>

<template>
  <div class="flex flex-col h-full min-h-0">
    <div v-if="loading" class="text-surface-500 text-sm p-3">Loading types…</div>
    <template v-else-if="data">
      <div class="text-xs text-surface-500">{{ data.category || "—" }}</div>
      <div class="font-bold mb-3 truncate" :title="data.name">{{ data.name }}</div>

      <div class="text-xs font-semibold text-surface-500 mb-1">Types ({{ data.types.length }})</div>
      <div class="flex flex-col gap-1 overflow-auto max-h-40 mb-3 pr-1">
        <button
          v-for="t in data.types"
          :key="t.id"
          type="button"
          class="text-left text-sm rounded px-2 py-1 flex items-center justify-between gap-2 border transition-colors"
          :class="
            t.id === selectedId
              ? 'bg-primary-50 border-primary-200 text-primary-700'
              : 'border-transparent hover:bg-surface-100'
          "
          @click="selectedId = t.id"
        >
          <span class="truncate">{{ t.name }}</span>
          <Tag
            :value="String(t.instanceCount)"
            :severity="t.instanceCount === 0 ? 'warn' : 'secondary'"
          />
        </button>
      </div>

      <div v-if="selectedType" class="flex items-center justify-between mb-2 gap-2">
        <div class="text-xs font-semibold text-surface-500">
          Parameters ({{ selectedType.parameters.length }})
        </div>
        <InputText v-model="paramSearch" placeholder="Filter…" class="w-32" />
      </div>
      <div class="overflow-auto flex-1 min-h-0">
        <table class="w-full text-sm">
          <tbody>
            <tr v-for="p in filteredParams" :key="p.name" class="border-b border-surface-100">
              <td class="py-1 pr-3 text-surface-500 align-top w-1/2 break-words">{{ p.name }}</td>
              <td class="py-1 align-top break-words">{{ p.value }}</td>
            </tr>
          </tbody>
        </table>
        <div v-if="!filteredParams.length" class="text-surface-400 text-sm p-2">No parameters.</div>
      </div>
    </template>
    <div v-else class="text-surface-500 text-sm p-3">No type info.</div>
  </div>
</template>
