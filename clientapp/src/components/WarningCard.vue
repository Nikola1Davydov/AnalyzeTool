<script lang="ts">
import { defineComponent } from "vue";

export default defineComponent({
  name: "WarningCard",
  props: {
    title: String,
    message: String,
    maxValue: {
      type: Number,
      default: 100,
    },
  },
});
</script>

<template>
  <div
    class="flex m-5 flex-col items-center text-center shadow-sm p-4 relative rounded-xl size-40 border border-surface-300 bg-surface-0 text-color"
  >
    <div class="h-full w-full text-3xl content-center">
      <p>{{ message }}</p>
    </div>
    <div class="mb-2">
      <p>{{ title }}</p>
    </div>
  </div>
</template>
