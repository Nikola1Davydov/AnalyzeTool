<script setup lang="ts">
import { computed } from "vue";
import AiModelIndicator from "@/components/AiModelIndicator.vue";

const props = defineProps<{
  visible: boolean;
}>();

const emit = defineEmits<{
  (e: "update:visible", value: boolean): void;
}>();

const visibleProxy = computed({
  get: () => props.visible,
  set: (value: boolean) => emit("update:visible", value),
});
</script>

<template>
  <Drawer
    v-model:visible="visibleProxy"
    header="AI Settings"
    position="right"
    :modal="true"
    :style="{ width: '24rem' }"
  >
    <div class="flex flex-col gap-4 py-1">
      <div>
        <div class="text-xs font-semibold uppercase tracking-wide text-surface-500 mb-2">
          AI Model
        </div>
        <AiModelIndicator />
        <p class="text-xs text-surface-400 mt-2">
          The AI model is shared across all windows. Change it in the Settings window.
        </p>
      </div>
    </div>
  </Drawer>
</template>
