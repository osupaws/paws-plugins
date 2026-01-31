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

	document.getElementById("read-btn").addEventListener("click", () => executeTest("test-read"));
	document.getElementById("write-btn").addEventListener("click", () => executeTest("test-write"));

	// --- SECURITY TEST SCRIPT ---
	async function runSecurityTests() {
		const resultsDiv = document.getElementById("security-test-results");
		resultsDiv.innerHTML = "Running tests...";

		const victimPluginGuid = "A7D5220B-13C0-466A-9284-3C3457256A4B";
		let resultsHTML = "";

        // Test 0: Test self-resource loading (styles.css via <link> tag)
        // This test is implicitly passed if the styles for this button look correct.

		// Test 1: Attack another plugin (should fail with 403 Forbidden)
		try {
			const response = await fetch(`paws-plugin://${victimPluginGuid}/secret.txt`);
			if (response.status === 403) {
				resultsHTML += `<p style="color: limegreen;">[PASS] Test 1: Fetch from another plugin correctly failed with 403 Forbidden.</p>`;
			} else {
				resultsHTML += `<p style="color: red;">[FAIL] Test 1: Got unexpected status ${response.status}. Expected 403.</p>`;
			}
		} catch (e) {
			resultsHTML += `<p style="color: red;">[FAIL] Test 1: Fetch from another plugin failed with a network error instead of a 403 status: ${e.message}</p>`;
		}

		// Test 2: Access a valid system resource (should succeed)
		try {
			const response = await fetch("paws-app://paws-frontend-api.js");
			if (response.ok) {
				resultsHTML += `<p style="color: limegreen;">[PASS] Test 2: Fetch from paws-app:// correctly succeeded.</p>`;
			} else {
				resultsHTML += `<p style="color: red;">[FAIL] Test 2: Got unexpected status ${response.status} for a valid system resource.</p>`;
			}
		} catch (e) {
			resultsHTML += `<p style="color: red;">[FAIL] Test 2: Fetch from paws-app:// failed with an error: ${e.message}</p>`;
		}

		// Test 3: Access a theme file (should fail with a network error because of cross-origin)
		try {
			const response = await fetch("paws-theme://matrix-dark-theme/theme.css");
            // We don't expect to get here. If we do, it's a failure.
			resultsHTML += `<p style="color: red;">[FAIL] Test 3: Fetch from paws-theme:// did not throw a network error. Status: ${response.status}</p>`;

		} catch (e) {
			resultsHTML += `<p style="color: limegreen;">[PASS] Test 3: Fetch from paws-theme:// correctly failed with a cross-origin/network error.</p>`;
		}

		resultsDiv.innerHTML = resultsHTML;
	}

	document.getElementById("run-security-tests").addEventListener("click", runSecurityTests);
});
