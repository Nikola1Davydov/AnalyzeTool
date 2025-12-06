<script setup>
defineProps({
  title: String,
  message: String,
  maxValue: {
    type: Number,
    default: 100,
  },
});
</script>

<template>
  <div
    class="flex m-5 flex-col items-center text-center shadow p-4 relative rounded-xl size-40 border-2 border-orange-400 bg-orange-100"
  >
    <div class="h-full w-full text-3xl content-center">
      <p>{{ message }}</p>
    </div>
    <div class="mb-2">
      <p>{{ title }}</p>
    </div>
  </div>
</template>
