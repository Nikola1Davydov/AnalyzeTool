<script setup>
import { action } from "@primeuix/themes/aura/image";

defineProps({
  list: [],
});
function sendSelectedToRevit() {
  const Ids = Array.from(list);
  window.chrome?.webview?.postMessage({ actio: "selection", ids });
}
</script>

<template>
  <div>
    <button>Send to Revit</button>
  </div>
</template>
