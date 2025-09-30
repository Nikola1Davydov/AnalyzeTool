<script setup>
defineProps({
  title: String,
});
</script>

<template>
  <div class="flex m-5 flex-col shadow p-5 relativ rounded-xl">
    <div class="pl-10 py-5">
      <b>{{ title }}</b>
    </div>
    <slot />
  </div>
</template>
