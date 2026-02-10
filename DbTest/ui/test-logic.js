document.addEventListener("DOMContentLoaded", () => {
	const resultDiv = document.getElementById("result");
	let currentMode = "stable";

	const urlParams = new URLSearchParams(window.location.search);
	const pluginId = urlParams.get("pluginId");

	// Check if paws API is loaded
	if (window.paws) {
		window.paws.onNotice(notice => {
			if (notice.type === "mode-changed") {
				currentMode = notice.mode;
				resultDiv.textContent = `Mode changed to: ${currentMode}. Ready to test.`;
			}
		});
	} else {
		resultDiv.textContent = "Error: window.paws API not found. The paws-frontend-api.js script might have failed to load.";
	}


	async function executeTest(command) {
		if (!window.paws) {
			resultDiv.textContent = "Error: Paws API is not available.";
			return;
		}
		if (!pluginId) {
			resultDiv.textContent = "Error: Could not determine plugin ID. Cannot send command.";
			return;
		}

		resultDiv.textContent = `Asking backend to '${command}' for ${currentMode} mode...`;

		try {
			const endpoint = `/api/plugins/execute/${pluginId}`;
			const body = {
				commandName: command,
				payload: { mode: currentMode }
			};

			resultDiv.textContent = await window.paws.post(endpoint, body);
		} catch (error) {
			resultDiv.textContent = `Error: ${error.message}`;
		}
	}

	// --- API Button Bindings ---
	
	function bindBtn(id, command) {
		const btn = document.getElementById(id);
		if (btn) btn.addEventListener("click", () => executeTest(command));
	}

	bindBtn("stable-db-btn", "test-stable-db");
	bindBtn("stable-scores-btn", "test-stable-scores");
	bindBtn("stable-parse-btn", "test-stable-parse");
	bindBtn("stable-scan-btn", "test-stable-scan");
	
	bindBtn("lazer-sets-btn", "test-lazer-db");
	bindBtn("lazer-files-btn", "test-lazer-files");

	// Security tests removed by user request
});
