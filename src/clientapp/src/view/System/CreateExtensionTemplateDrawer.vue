<script lang="ts">
import { defineComponent } from "vue";

export default defineComponent({
  name: "CreateExtensionTemplateDrawer",
});
</script>

<script setup lang="ts">
/**
 * The extension manager's way in to the create form: the same CreateExtensionForm the "New" ribbon
 * button shows in its own window, here as a drawer over the list so the result appears behind it.
 * The form is mounted only while the drawer is open — it loads roots and installed extensions on
 * mount, and that is exactly when they should be fresh.
 */
import { useNotificationStore } from "@/stores/useNotificationStore";
import CreateExtensionForm from "@/view/System/CreateExtensionForm.vue";

const props = defineProps<{ visible: boolean }>();

const emit = defineEmits<{
  (e: "update:visible", value: boolean): void;
  (e: "created"): void;
}>();

const notifications = useNotificationStore();

function close() {
  emit("update:visible", false);
}

function onCreated(directory: string) {
  notifications.success(`Created ${directory}`);
  emit("created");
  close();
}
</script>

<template>
  <Drawer
    :visible="props.visible"
    position="right"
    header="New extension"
    class="!w-full md:!w-[40rem]"
    @update:visible="emit('update:visible', !!$event)"
  >
    <CreateExtensionForm v-if="props.visible" @created="onCreated" @cancel="close" />
  </Drawer>
</template>
