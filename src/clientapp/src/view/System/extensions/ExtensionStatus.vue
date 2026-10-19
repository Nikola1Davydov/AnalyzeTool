<script setup lang="ts">
/** The "Status" cell: silent while all is well, a tag only for a problem or an update. */
import { buildState, type ExtensionRow } from "./types";
import { useExtensionManager } from "./useExtensionManager";

defineProps<{ row: ExtensionRow }>();
const { updateChecks, data } = useExtensionManager();
</script>

<template>
  <!-- Silent while all is well: a tag here means there is something to do. -->
  <Tag
    v-if="updateChecks[row.id]?.updateAvailable"
    :value="`Update → ${updateChecks[row.id]?.latest}`"
    severity="success"
    class="mr-1"
    v-tooltip.top="'An update is available — the arrow button installs it'"
  />
  <!-- Independent of the update tag: an update that FAILS leaves updateAvailable true,
       so an v-else-if here would hide the very error the user needs to see. -->
  <Tag
    v-if="updateChecks[row.id]?.error"
    value="Update failed"
    severity="danger"
    class="mr-1"
    v-tooltip.top="updateChecks[row.id]?.error"
  />
  <Tag
    v-if="!row.compatible"
    :value="buildState(row, data?.hostRevit).label"
    severity="danger"
    v-tooltip.top="row.compileError || buildState(row, data?.hostRevit).tip"
  />
  <Tag
    v-else-if="row.compileError"
    value="Error"
    severity="danger"
    v-tooltip.top="row.compileError"
  />
</template>
