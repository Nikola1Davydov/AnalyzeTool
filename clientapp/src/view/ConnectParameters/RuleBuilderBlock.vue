<script lang="ts">
import { defineComponent } from "vue";

export default defineComponent({
  name: "RuleBuilderBlock",
});
</script>

<script setup lang="ts">
import {
  getScopeTagSeverity,
  getStorageTypeTagSeverity,
  getValueTypeTagSeverity,
} from "@/view/ConnectParameters/tagSeverity";
import type { RuleBlock } from "@/view/ConnectParameters/ruleTypes";

type RuleBlockOption = {
  valueTypeLabel: string;
  storageTypeLabel: string;
  scopeLabel: string;
  originLabel: string;
};

const props = defineProps<{
  block: RuleBlock;
  index: number;
  blocksCount: number;
  targetKind: "Text" | "Number" | "Unknown";
  numericParameterNameOptions: string[];
  parameterNameOptions: string[];
  parameterOption?: RuleBlockOption | null;
  blockTitle: string;
}>();

const emit = defineEmits<{
  dragstart: [index: number];
  drop: [index: number];
  remove: [id: number];
  updateParameterName: [value: string | null];
  updateLiteral: [value: string];
}>();

function onUpdateParameterName(value: string | null | undefined) {
  emit("updateParameterName", value || null);
}

function onUpdateLiteral(value: string | null | undefined) {
  emit("updateLiteral", value || "");
}
</script>

<template>
  <div
    draggable="true"
    @dragstart="emit('dragstart', props.index)"
    @dragover.prevent
    @drop="emit('drop', props.index)"
    :class="[
      'rule-block border rounded-lg p-3 min-w-[220px] shadow-sm',
      props.block.kind === 'parameter'
        ? 'rule-block--parameter'
        : props.block.kind === 'number'
          ? 'rule-block--number'
          : 'rule-block--text',
    ]"
  >
    <div class="flex items-center justify-between gap-2 mb-2">
      <Select
        v-if="props.block.kind === 'parameter'"
        :options="
          props.targetKind === 'Number'
            ? props.numericParameterNameOptions
            : props.parameterNameOptions
        "
        :modelValue="props.block.parameterName"
        placeholder="Select parameter"
        @update:modelValue="onUpdateParameterName"
      />
      <InputText
        v-else
        :modelValue="props.block.literal"
        :placeholder="props.block.kind === 'number' ? 'Enter number' : 'Enter text (e.g. _)'"
        @update:modelValue="onUpdateLiteral"
      />
      <Button
        icon="pi pi-times"
        text
        severity="danger"
        :disabled="props.blocksCount === 1"
        @click="emit('remove', props.block.id)"
      />
    </div>
    <div></div>

    <div
      v-if="props.block.kind === 'parameter' && props.block.parameterName"
      class="mt-2 flex flex-wrap gap-1"
    >
      <Tag
        :value="props.parameterOption?.valueTypeLabel || 'Unknown'"
        :severity="getValueTypeTagSeverity(props.parameterOption?.valueTypeLabel)"
      />
      <Tag
        :value="props.parameterOption?.scopeLabel || 'Unknown'"
        :severity="getScopeTagSeverity(props.parameterOption?.scopeLabel)"
      />
      <Tag :value="props.parameterOption?.originLabel || 'Unknown'" severity="secondary" />
    </div>

    <div v-else class="text-[11px] text-surface-500 mt-2 truncate" :title="props.blockTitle">
      {{ props.blockTitle }}
    </div>
  </div>
</template>

<style scoped>
.rule-block {
  background: var(--p-surface-0, #ffffff);
  border-color: var(--p-surface-300, #d1d5db);
  transition:
    transform 120ms ease,
    box-shadow 120ms ease,
    border-color 120ms ease;
}

.rule-block:hover {
  transform: translateY(-1px);
  box-shadow: 0 12px 24px -18px rgba(0, 0, 0, 0.45);
}

.rule-block--parameter {
  border-top: 3px solid #60a5fa;
}

.rule-block--number {
  border-top: 3px solid #34d399;
}

.rule-block--text {
  border-top: 3px solid #f59e0b;
}
</style>
