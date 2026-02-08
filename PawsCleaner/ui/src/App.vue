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
const customBgPng = ref<string | null>(null);
const customBgJpg = ref<string | null>(null);
const customBgPngName = ref<string | null>(null);
const customBgJpgName = ref<string | null>(null);

function handleFileSelect(event: Event, type: "png" | "jpg") {
  const input = event.target as HTMLInputElement;
  if (!input.files || input.files.length === 0) return;
  const file = input.files[0];
  const reader = new FileReader();

  reader.onload = (e) => {
    const base64 = e.target?.result as string;
    // Strip prefix like "data:image/png;base64," if needed
    // But sending full data URI is safer for backend to parse MIME if needed
    // We'll send full string.
    if (type === "png") {
      customBgPng.value = base64;
      customBgPngName.value = file.name;
    } else {
      customBgJpg.value = base64;
      customBgJpgName.value = file.name;
    }
  };
  reader.readAsDataURL(file);
}

async function startCleaning() {
  if (isLoading.value) return;
  isLoading.value = true;
  progress.value = 0;

  const payload = {
    // Mode is now handled by Backend using IsLegacyMode check
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
      BackgroundMode: assets.value.background, // "keep", "white", "custom"
      CustomBackgroundPng: customBgPng.value,
      CustomBackgroundJpg: customBgJpg.value,
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
        <!-- Row 1: skins, sounds, background dropdown -->
        <div class="checkbox-group">
          <PawsCheckbox label="skins" v-model="assets.skins" />
          <PawsCheckbox label="sounds" v-model="assets.sounds" />
          <div style="width: 200px">
            <PawsDropdown
              label="bgs"
              :options="backgroundOptions"
              v-model="assets.background"
              size="compact"
              defaultValue="keep"
            />
          </div>
        </div>

        <!-- Custom Background Files -->
        <div
          v-if="assets.background === 'custom'"
          class="custom-files-container"
        >
          <div class="file-row">
            <span class="file-label">PNG:</span>
            <input
              type="file"
              accept=".png"
              @change="(e) => handleFileSelect(e, 'png')"
            />
            <span v-if="customBgPngName" class="file-name">{{
              customBgPngName
            }}</span>
          </div>
          <div class="file-row">
            <span class="file-label">JPG:</span>
            <input
              type="file"
              accept=".jpg,.jpeg"
              @change="(e) => handleFileSelect(e, 'jpg')"
            />
            <span v-if="customBgJpgName" class="file-name">{{
              customBgJpgName
            }}</span>
          </div>
        </div>

        <!-- Row 2: videos, storyboards, previews -->
        <div class="checkbox-group">
          <PawsCheckbox label="videos" v-model="assets.videos" />
          <PawsCheckbox label="storyboards" v-model="assets.storyboards" />
        </div>
      </div>
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
