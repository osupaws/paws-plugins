/**
 * Paws Plugin API Helper
 * Simplifies communication with the Paws Host and C# Backend.
 */

const Paws = {
    /**
     * Sends a command to your C# Backend's ExecuteCommandAsync method.
     * @param {string} commandName - The name of the command to execute.
     * @param {any} payload - Optional data to send.
     * @returns {Promise<any>} The result from the backend.
     */
    sendCommand: (commandName, payload = null) => {
        return new Promise((resolve, reject) => {
            const requestId = Date.now() + Math.random();

            // 1. Setup Response Handler
            const handler = (event) => {
                // Security check: Only accept messages from parent
                if (event.source !== window.parent) return;

                const { id, result, error } = event.data;
                if (id !== requestId) return;

                window.removeEventListener("message", handler);

                if (error) reject(error);
                else resolve(result);
            };
            window.addEventListener("message", handler);

            // 2. Determine Plugin ID from URL
            // URL format: paws-plugin://{pluginId}/...
            // or query param ?pluginId={id}
            const urlParams = new URLSearchParams(window.location.search);
            const pluginId = urlParams.get("pluginId") || window.location.hostname;

            if (!pluginId) {
                reject("Could not determine Plugin ID from environment.");
                return;
            }

            // 3. Send Request to Host
            window.parent.postMessage(
                {
                    channel: "post",
                    id: requestId,
                    payload: {
                        endpoint: `/api/plugins/execute/${pluginId}`,
                        body: {
                            commandName: commandName,
                            payload: payload
                        }
                    }
                },
                "*"
            );
        });
    },

    /**
     * Listens for environment changes (Theme, Mode).
     * @param {function} callback - Function receiving {type, ...data}
     */
    onMessage: (callback) => {
        window.addEventListener("message", (event) => {
            if (event.data.channel === "notice") {
                callback(event.data.payload);
            }
        });
    }
};

// Expose globally
window.Paws = Paws;
