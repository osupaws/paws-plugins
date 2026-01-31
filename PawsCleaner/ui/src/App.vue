<script setup lang="ts">
import { ref } from "vue";
// Import generic API helper
import { Paws } from "./paws-api";

// Import components from Paws UI
// Note: Ensure @osupaws/paws-ui is installed
import { PawsButton, PawsCard } from "@osupaws/paws-ui";

const responseText = ref("Waiting for action...");
const loading = ref(false);

async function sendGreet() {
  loading.value = true;
  try {
    const res = await Paws.sendCommand("greet", "Hello from Vue!");
    responseText.value = JSON.stringify(res);
  } catch (err) {
    responseText.value = "Error: " + err;
  } finally {
    loading.value = false;
  }
}
</script>

<template>
  <div class="plugin-container">
    <h1>Vue 3 + Paws UI</h1>

    <PawsCard title="Live Demo" class="demo-card">
      <p class="mb-4">This button uses the Paws Design System:</p>

      <div class="flex-row">
        <PawsButton
          @click="sendGreet"
          :disabled="loading"
          label="Send Command"
        />
      </div>

      <div class="response-box">
        {{ responseText }}
      </div>
    </PawsCard>
  </div>
</template>

<style scoped>
.plugin-container {
  max-width: 600px;
  margin: 0 auto;
  padding: 20px;
  color: var(--paws-color-text-primary);
}

.demo-card {
  margin-top: 20px;
}

.mb-4 {
  margin-bottom: 1rem;
}

.flex-row {
  display: flex;
  gap: 10px;
  margin-bottom: 20px;
}

.response-box {
  background: var(--paws-color-bg-tertiary);
  padding: 10px;
  border-radius: 4px;
  font-family: monospace;
}
</style>
