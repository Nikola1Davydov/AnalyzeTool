import { defineStore } from "pinia";
import { ref } from "vue";
import { Commands, invoke } from "@/RevitBridge";

export interface UpdateInfo {
  currentVersion?: string;
  latestVersion?: string;
  isUpdateAvailable?: boolean;
  releaseUrl?: string;
  lastChecked?: Date;
}

export const useUpdateStore = defineStore("updateInfo", () => {
  const updateInfo = ref<UpdateInfo>({});

  const setUpdateInfo = (info: UpdateInfo) => {
    console.log("Setting update info:", info);
    updateInfo.value = {
      ...updateInfo.value,
      ...info,
      lastChecked: new Date(),
    };
  };

  async function loadUpdateData(): Promise<void> {
    try {
      const info = await invoke<UpdateInfo>(Commands.CheckUpdate, null);
      if (info) setUpdateInfo(info);
    } catch (err) {
      console.error("Failed to check for updates", err);
    }
  }

  const resetUpdateInfo = () => {
    updateInfo.value = {};
  };

  return {
    updateInfo,
    setUpdateInfo,
    resetUpdateInfo,
    loadUpdateData,
  };
});
