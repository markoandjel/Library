window.libraryAccessUpload = {
    fileInfo(input) {
        const file = input?.files?.[0];
        return file ? { name: file.name, size: file.size } : null;
    },

    async upload(input, dotnetRef) {
        const file = input?.files?.[0];
        if (!file) throw new Error("Izaberite Access fajl.");

        const startResponse = await fetch("/access-uploads", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ fileName: file.name, size: file.size })
        });
        if (!startResponse.ok) throw new Error(await this.errorText(startResponse));

        const session = await startResponse.json();
        const chunkSize = 8 * 1024 * 1024;
        let offset = session.receivedBytes || 0;

        try {
            while (offset < file.size) {
                const chunk = file.slice(offset, Math.min(offset + chunkSize, file.size));
                let response;
                for (let attempt = 0; attempt < 5; attempt++) {
                    try {
                        response = await fetch(`/access-uploads/${session.id}?offset=${offset}`, {
                            method: "PUT",
                            headers: { "Content-Type": "application/octet-stream" },
                            body: chunk
                        });
                        if (response.ok) break;
                        if (response.status === 409) {
                            const state = await response.json();
                            offset = state.receivedBytes;
                            response = null;
                            break;
                        }
                        throw new Error(await this.errorText(response));
                    } catch (error) {
                        if (attempt === 4) throw error;
                        await new Promise(resolve => setTimeout(resolve, 1000 * 2 ** attempt));
                    }
                }

                if (response?.ok) {
                    const state = await response.json();
                    offset = state.receivedBytes;
                }
                await dotnetRef.invokeMethodAsync(
                    "ReportChunkUploadProgress",
                    Math.min(100, Math.floor(offset * 100 / Math.max(file.size, 1))));
            }

            const completeResponse = await fetch(`/access-uploads/${session.id}/complete`, { method: "POST" });
            if (!completeResponse.ok) throw new Error(await this.errorText(completeResponse));
            return await completeResponse.json();
        } catch (error) {
            throw error;
        }
    },

    async errorText(response) {
        const text = await response.text();
        if (!text) return `Upload nije uspeo (${response.status}).`;
        try {
            const json = JSON.parse(text);
            return json.error || json.detail || text;
        } catch {
            return text;
        }
    }
};
