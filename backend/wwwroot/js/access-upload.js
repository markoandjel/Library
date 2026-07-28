window.libraryAccessUpload = {
    logError(stage, error, details = {}) {
        console.error("[Access import]", {
            stage,
            message: error?.message || String(error),
            ...details,
            error
        });
    },

    fileInfo(input) {
        const file = input?.files?.[0];
        return file ? { name: file.name, size: file.size } : null;
    },

    release(input) {
        if (input) input.value = "";
    },

    async upload(input, dotnetRef) {
        const file = input?.files?.[0];
        if (!file) throw new Error("Izaberite Access fajl.");

        const fileDetails = { fileName: file.name, fileSize: file.size };
        const chunkSize = 8 * 1024 * 1024;
        let session = null;

        try {
            const startResponse = await fetch("/access-uploads", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ fileName: file.name, size: file.size })
            });
            if (!startResponse.ok) {
                const error = new Error(await this.errorText(startResponse));
                this.logError("start", error, { ...fileDetails, httpStatus: startResponse.status });
                throw error;
            }

            session = await startResponse.json();
            let offset = session.receivedBytes || 0;

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
                        this.logError("chunk", error, {
                            ...fileDetails,
                            uploadId: session.id,
                            offset,
                            attempt: attempt + 1,
                            httpStatus: response?.status
                        });
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
            if (!completeResponse.ok) {
                const error = new Error(await this.errorText(completeResponse));
                this.logError("complete", error, {
                    ...fileDetails,
                    uploadId: session.id,
                    httpStatus: completeResponse.status
                });
                throw error;
            }
            return await completeResponse.json();
        } catch (error) {
            this.logError("upload", error, { ...fileDetails, uploadId: session?.id });
            if (session?.id) {
                try {
                    await fetch(`/access-uploads/${session.id}`, { method: "DELETE" });
                } catch (cleanupError) {
                    this.logError("cleanup", cleanupError, { ...fileDetails, uploadId: session.id });
                }
            }
            throw error;
        } finally {
            // Releasing the input drops the browser's reference to the selected File.
            this.release(input);
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
