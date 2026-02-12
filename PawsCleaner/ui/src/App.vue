<script setup lang="ts">
import { ref } from "vue";
import { Paws } from "./paws-api";
import {
  PawsCard,
  PawsCheckbox,
  PawsHeading,
  PawsDropdown,
  PawsButton,
  PawsProgressbar,
  PawsSubButton,
  ResetImageIcon,
} from "@osupaws/paws-ui";

const rulesets = ref({
  osu: true,
  taiko: true,
  catch: true,
  mania: true,
});

const assets = ref({
  skins: false,
  sounds: false,
  videos: false,
  storyboards: false,
  background: "keep",
});

const dryRun = ref(true);
const isLoading = ref(false);
const progress = ref(0);

const backgroundOptions = ["keep", "white", "custom"];
const fileInput = ref<HTMLInputElement | null>(null);

async function onBackgroundChange(val: string) {
  if (val === "custom") {
    try {
      const result = (await Paws.sendCommand("checkCustomBackground")) as any;
      const exists = result?.Exists ?? result?.exists;
      if (!exists) {
        fileInput.value?.click();
      }
    } catch (err) {
      console.error("Failed to check background", err);
      fileInput.value?.click();
    }
  }
}

async function handleFileCancel() {
  // Revert to keep if no background is actually saved
  try {
    const result = (await Paws.sendCommand("checkCustomBackground")) as any;
    const exists = result?.Exists ?? result?.exists;
    if (!exists && assets.value.background === "custom") {
      assets.value.background = "keep";
    }
  } catch {
    assets.value.background = "keep";
  }
}

async function handleFileSelect(event: Event) {
  const input = event.target as HTMLInputElement;
  if (!input.files || input.files.length === 0) {
    await handleFileCancel();
    return;
  }

  const file = input.files[0];
  try {
    isLoading.value = true;

    // 1. Native Bridge Upload (Bypasses CORS, avoids Base64)
    let tempHandle: string;

    // Use fast path if available (direct file path access)
    if ((file as any).path) {
      const result = await (window as any).api.storage.uploadTempPath(
        (file as any).path,
      );
      tempHandle = result.tempHandle || result.TempHandle;
    } else {
      // Fallback for non-local files or restrictive environments
      const buffer = await file.arrayBuffer();
      const result = await (window as any).api.storage.uploadTemp(buffer);
      tempHandle = result.tempHandle || result.TempHandle;
    }

    console.log("File handled via native bridge, handle:", tempHandle);

    if (!tempHandle) {
      throw new Error("Failed to get TempHandle from native bridge");
    }

    // 2. Tell the plugin to process this temp file (Plugin will resize it to 1080p)
    const result = (await Paws.sendCommand("importCustomBackgroundTemp", {
      tempHandle: tempHandle,
    })) as any;

    const success = result?.Success ?? result?.success;
    const message = result?.Message ?? result?.message;

    if (success) {
      console.log("Custom background imported via Native Bridge.");
    } else {
      console.error("Plugin import failed", message);
      assets.value.background = "keep";
    }
  } catch (err) {
    console.error("Upload error", err);
    assets.value.background = "keep";
  } finally {
    isLoading.value = false;
    if (input) input.value = "";
  }
}

async function startCleaning() {
  if (isLoading.value) return;
  isLoading.value = true;
  progress.value = 0;

  const payload = {
    DryRun: dryRun.value,
    Rulesets: {
      Osu: rulesets.value.osu,
      Taiko: rulesets.value.taiko,
      Catch: rulesets.value.catch,
      Mania: rulesets.value.mania,
    },
    Assets: {
      Skins: assets.value.skins,
      Sounds: assets.value.sounds,
      Videos: assets.value.videos,
      Storyboards: assets.value.storyboards,
      BackgroundMode: assets.value.background,
    },
  };

  try {
    const result = await Paws.sendCommand("clean", payload);
    console.log("Cleanup Result:", result);
    // Simulate progress for now as we don't have real-time events hooked up yet in backend fully
    progress.value = 100;
  } catch (e) {
    console.error("Cleanup Error:", e);
  } finally {
    isLoading.value = false;
  }
}
</script>

<template>
  <div class="plugin-container">
    <PawsCard class="cleaner-card">
      <template #heading>
        <PawsHeading size="lg">ruleset</PawsHeading>
      </template>
      <div class="checkbox-group">
        <PawsCheckbox label="osu" v-model="rulesets.osu" />
        <PawsCheckbox label="mania" v-model="rulesets.mania" />
        <PawsCheckbox label="fruits" v-model="rulesets.catch" />
        <PawsCheckbox label="taiko" v-model="rulesets.taiko" />
      </div>
    </PawsCard>

    <PawsCard class="cleaner-card">
      <template #heading>
        <PawsHeading size="lg">assets</PawsHeading>
      </template>

      <div class="assets-container">
        <div class="checkbox-group">
          <PawsCheckbox label="skins" v-model="assets.skins" />
          <PawsCheckbox label="sounds" v-model="assets.sounds" />
          <PawsCheckbox label="videos" v-model="assets.videos" />
        </div>

        <div class="checkbox-group">
          <PawsCheckbox label="storyboards" v-model="assets.storyboards" />
          <div class="bg-selection-wrapper">
            <PawsDropdown
              label="bgs"
              :options="backgroundOptions"
              v-model="assets.background"
              size="compact"
              defaultValue="keep"
              @update:modelValue="onBackgroundChange"
            />
            <PawsSubButton
              v-if="assets.background === 'custom'"
              size="medium"
              @click="fileInput?.click()"
            >
              <template #icon>
                <ResetImageIcon />
              </template>
            </PawsSubButton>
          </div>
        </div>
      </div>

      <!-- Hidden File Input for Custom BG -->
      <input
        type="file"
        ref="fileInput"
        style="display: none"
        accept="image/*"
        @change="handleFileSelect"
        @cancel="handleFileCancel"
      />
    </PawsCard>

    <div class="split-row">
      <PawsCard class="cleaner-card-half">
        <template #heading>
          <PawsHeading size="lg">options</PawsHeading>
        </template>
        <div
          style="padding: 16px; display: flex; flex-direction: column; gap: 8px"
        >
          <PawsCheckbox label="Dry Run (Simulate)" v-model="dryRun" />
          <p style="font-size: 12px; opacity: 0.7">
            Check console (Ctrl+Shift-I) for logs.
          </p>
        </div>
      </PawsCard>

      <PawsCard class="cleaner-card-half">
        <template #heading>
          <PawsHeading size="lg">filter rules</PawsHeading>
        </template>
        <p>TBD</p>
      </PawsCard>
    </div>

    <PawsButton
      :label="dryRun ? 'simulate cleanup' : 'clean it up!'"
      class="action-button"
      variant="primary"
      @click="startCleaning"
      :disabled="isLoading"
    />

    <PawsProgressbar :progress="progress" class="progress-bar" />
  </div>
</template>

<style scoped>
.plugin-container {
  box-sizing: border-box;
  display: flex;
  flex-direction: column;
  gap: 16px;
  width: 100%;
  height: 100vh;
  padding: 32px;
}

/* Deep selector to override child component styles if needed */
:deep([data-paws-part="content"]) {
  margin-top: 12px !important;
}

.cleaner-card {
  width: 100%;
  box-sizing: border-box;
  /* Let these shrink if space is tight, though usually they have intrinsic height */
  flex-shrink: 0;
}

.checkbox-group {
  display: flex;
  justify-content: center;
  align-items: center;
  gap: 32px;
  width: 100%;
}

.assets-container {
  display: flex;
  flex-direction: column;
  gap: 16px;
  width: 100%;
}

.split-row {
  display: flex;
  flex-direction: row;
  gap: 16px;
  width: 100%;
  flex-shrink: 0;
}

.bg-selection-wrapper {
  display: flex;
  align-items: center;
  gap: 10px;
  width: 200px; /* Base width remains for the group */
}
.bg-selection-wrapper > :first-child {
  flex: 1; /* Dropdown takes main space */
}

.cleaner-card-half {
  flex: 1;
  box-sizing: border-box;
  min-width: 0;
  height: 292px;
}

.action-button {
  width: 100%;
  flex: 1; /* Take all remaining height */
  /* Ensure it doesn't get too small if space is tight? */
  min-height: 48px;
  font-weight: var(--paws-font-weight-bold);
  font-size: 32px;
  border-radius: 16px;
}

.progress-bar {
  width: 100%;
  flex-shrink: 0; /* Keep valid height */
}

.custom-files-container {
  display: flex;
  flex-direction: column;
  gap: 8px;
  width: 100%;
  padding: 8px;
  background: rgba(0, 0, 0, 0.1);
  border-radius: 8px;
  margin-top: 8px;
  box-sizing: border-box;
}

.file-row {
  display: flex;
  align-items: center;
  gap: 8px;
}

.file-label {
  font-weight: bold;
  width: 40px;
}

.file-name {
  font-size: 0.8em;
  opacity: 0.8;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  max-width: 150px;
}
</style>
