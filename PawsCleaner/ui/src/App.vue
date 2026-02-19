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
import { nextTick, watch, onMounted } from "vue";

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

const isLoading = ref(false);
const progress = ref(0);
const workerLogs = ref<
  { id: number; message: string; timestamp: string; category: string }[]
>([]);

const backgroundOptions = ["keep", "white", "custom"];
const fileInput = ref<HTMLInputElement | null>(null);

function addLog(message: string, category: string = "info") {
  workerLogs.value.push({
    id: Date.now() + Math.random(),
    message,
    category,
    timestamp: new Date().toLocaleTimeString([], {
      hour: "2-digit",
      minute: "2-digit",
      second: "2-digit",
    }),
  });
}

onMounted(async () => {
  // Check custom background status on load
  try {
    const result = (await Paws.sendCommand("checkCustomBackground")) as any;
    if (result?.Exists || result?.exists) {
      addLog("Cached custom background found.", "good");
    } else {
      addLog("No custom background in cache.", "info");
    }
  } catch (e) {
    addLog("Failed to check background cache.", "warning");
  }

  // Lifecycle Listeners (V3)
  (window as any).Paws?.on("lifecycle", (event: string) => {
    if (event === "focus") {
      console.log("[UI] Focused - Resuming animations");
      // If we had heavy animations, we'd resume them here
    } else if (event === "blur") {
      console.log("[UI] Blurred - Pausing animations");
      // If we had heavy animations, we'd pause them here
    }
  });
});

watch(
  () => workerLogs.value.length,
  async () => {
    // CSS column-reverse handles anchoring automatically,
    // but we can still ensure visibility if needed for specific cases.
    await nextTick();
  },
);

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
      addLog("Custom background imported successfully.", "good");
    } else {
      console.error("Plugin import failed", message);
      addLog(`Background import failed: ${message}`, "fail");
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

  // Add a visual separator for the new run
  addLog("--- New Cleanup Run Started ---", "scan");
  addLog("Cleaning engine initialized.", "info");

  const rulesetList = Object.entries(rulesets.value)
    .filter(([_, v]) => v)
    .map(([k, _]) => k)
    .join(", ");
  addLog(`Rulesets: ${rulesetList || "none"}`, "info");

  const assetList = Object.entries(assets.value)
    .filter(([k, v]) => v && k !== "background")
    .map(([k, _]) => k)
    .join(", ");
  addLog(`Assets: ${assetList || "none"}`, "info");
  addLog(`Background mode: ${assets.value.background}`, "info");

  const payload = {
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
    addLog("Analyzing beatmap database...", "info");
    const result = (await Paws.sendCommand("clean", payload)) as any;
    console.log("Cleanup Result:", result);

    if (result?.Message) {
      // Backend usually returns a summary like "Cleanup Complete. Processed X sets. Deleted Y maps. (stats)"
      addLog(result.Message, "good");
    }

    addLog("Finalizing assets changes...", "info");
    progress.value = 100;
    addLog("Cleanup finished successfully.", "good");
  } catch (e: any) {
    console.error("Cleanup Error:", e);
    addLog(`Error: ${e.message || "Unknown error"}`, "fail");
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

    <PawsCard class="worker-card">
      <template #heading>
        <PawsHeading size="lg">worker</PawsHeading>
      </template>

      <PawsCard variant="compact" class="inner-card">
        <div class="log-scroll-area">
          <template v-if="workerLogs.length === 0">
            <div class="empty-logs">Worker is idle.</div>
          </template>
          <template v-else>
            <TransitionGroup name="log-list" tag="div" class="log-list-wrapper">
              <div
                v-for="log in workerLogs"
                :key="log.id"
                class="log-entry"
                :class="`log-${log.category}`"
              >
                <div class="log-header">
                  <span class="log-time">{{ log.timestamp }}</span>
                  <span class="log-category"
                    >[{{ log.category.toUpperCase() }}]</span
                  >
                </div>
                <div class="log-message">{{ log.message }}</div>
              </div>
            </TransitionGroup>
          </template>
        </div>
      </PawsCard>
    </PawsCard>

    <PawsButton
      label="clean it up!"
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

.worker-card {
  width: 100%;
  box-sizing: border-box;
  flex-shrink: 0;
  height: 292px;
  display: flex;
  flex-direction: column;
}

.worker-card :deep([data-paws-part="content"]) {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-height: 0;
  margin-top: 12px !important;
}

.inner-card {
  flex: 1;
  background-color: var(--paws-color-bg-primary) !important;
  display: flex;
  flex-direction: column;
  width: 100%;
  box-sizing: border-box;
  margin-top: 0;
  min-height: 0;
  overflow: hidden;
}

/* Ensure PawsCard internal content filler allows scrolling */
.inner-card :deep([data-paws-part="content"]) {
  flex: 1;
  display: flex;
  flex-direction: column;
  height: 100%;
  padding: 0; /* Remove default padding to handle it in log-scroll-area */
  min-height: 0;
}

.log-scroll-area {
  flex: 1;
  overflow-y: auto;
  height: 100%;
  display: flex;
  flex-direction: column-reverse; /* The Magic Anchor */
}

.log-list-wrapper {
  display: flex;
  flex-direction: column; /* Normal order within the anchored block */
  gap: 2px;
  padding: 0;
  width: 100%;
}

/* Animations */
.log-list-enter-active,
.log-list-move {
  transition: all 0.2s ease;
}

.log-list-enter-from {
  opacity: 0;
  transform: translateY(10px); /* Slide in from bottom */
}

/* Ensure moving items don't jitter */
.log-list-leave-active {
  position: absolute;
}

.empty-logs {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
  opacity: 0.5;
  font-size: 14px;
  font-family: var(--paws-font-mono);
}

.log-entry {
  width: 100%;
  flex-shrink: 0;
  display: flex;
  flex-direction: row;
  align-items: center; /* Fixed vertical alignment */
  gap: 8px;
  padding: 4px 12px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.03);
  font-family: var(--paws-font-mono);
  box-sizing: border-box;
}

.log-header {
  display: flex;
  align-items: center;
  gap: 6px;
  opacity: 0.5;
  flex-shrink: 0;
}

.log-time {
  white-space: nowrap;
  font-size: 12px;
  font-weight: var(--paws-font-weight-medium);
}

.log-category {
  font-weight: var(--paws-font-weight-medium);
  font-size: 12px;
  min-width: 50px; /* Ensure labels align horizontally */
}

.log-message {
  font-size: 12px;
  font-weight: var(--paws-font-weight-normal);
  line-height: 1.4;
  color: var(--paws-color-text-primary);
  word-break: break-word; /* Better than break-all for readability */
  flex: 1;
}

/* Category Highlights */
.log-good .log-category {
  color: #4ade80;
}
.log-fail .log-category,
.log-fail .log-message {
  color: #f87171;
}
.log-warning .log-category {
  color: #fbbf24;
}
.log-scan .log-category {
  color: #60a5fa;
  opacity: 0.8;
}
.log-scan {
  border-top: 1px solid rgba(255, 255, 255, 0.1);
  margin-top: 4px;
  background: rgba(255, 255, 255, 0.02);
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
