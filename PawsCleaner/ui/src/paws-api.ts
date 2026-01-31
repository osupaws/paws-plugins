/**
 * Paws Plugin API Helper (TypeScript)
 */
export const Paws = {
	sendCommand: (commandName: string, payload: any = null): Promise<any> => {
		return new Promise((resolve, reject) => {
			const requestId = Date.now() + Math.random();

			const handler = (event: MessageEvent) => {
				if (event.source !== window.parent) return;
				const { id, result, error } = event.data;
				if (id !== requestId) return;

				window.removeEventListener("message", handler);
				if (error) reject(error);
				else resolve(result);
			};
			window.addEventListener("message", handler);

			// Get Plugin ID from URL
			const urlParams = new URLSearchParams(window.location.search);
			const pluginId = urlParams.get("pluginId") || window.location.hostname;

			if (!pluginId) {
				reject("No Plugin ID found");
				return;
			}

			window.parent.postMessage(
				{
					channel: "post",
					id: requestId,
					payload: {
						endpoint: `/api/plugins/execute/${pluginId}`,
						body: { commandName, payload },
					},
				},
				"*",
			);
		});
	},

	onMessage: (callback: (payload: any) => void) => {
		window.addEventListener("message", (event) => {
			if (event.data.channel === "notice") {
				callback(event.data.payload);
			}
		});
	},
};
