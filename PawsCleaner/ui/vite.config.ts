import { defineConfig } from "vite";
import vue from "@vitejs/plugin-vue";
import path from "path";

// https://vite.dev/config/
export default defineConfig({
	plugins: [vue()],
	base: "./", // CRITICAL: Allows loading from paws-plugin:// protocol
	build: {
		outDir: "../ui-dist", // Build outside the ui folder for cleaner structure
		emptyOutDir: true,
	},
	resolve: {
		alias: {
			"@": path.resolve(__dirname, "./src"),
		},
	},
});
