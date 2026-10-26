(() => {
    const dbName = "hotelmanager-invoice-export";
    const dbVersion = 1;
    const storeName = "handles";
    const handleKey = "invoice-save-target";
    const activeHandles = new Map();

    function openDb() {
        return new Promise((resolve, reject) => {
            const request = indexedDB.open(dbName, dbVersion);

            request.onupgradeneeded = () => {
                const db = request.result;
                if (!db.objectStoreNames.contains(storeName)) {
                    db.createObjectStore(storeName);
                }
            };

            request.onsuccess = () => resolve(request.result);
            request.onerror = () => reject(request.error);
        });
    }

    async function getStoredHandle() {
        const db = await openDb();
        return await new Promise((resolve, reject) => {
            const tx = db.transaction(storeName, "readonly");
            const store = tx.objectStore(storeName);
            const getRequest = store.get(handleKey);
            getRequest.onsuccess = () => resolve(getRequest.result ?? null);
            getRequest.onerror = () => reject(getRequest.error);
        });
    }

    async function storeHandle(handle) {
        const db = await openDb();
        await new Promise((resolve, reject) => {
            const tx = db.transaction(storeName, "readwrite");
            const store = tx.objectStore(storeName);
            const putRequest = store.put(handle, handleKey);
            putRequest.onsuccess = () => resolve();
            putRequest.onerror = () => reject(putRequest.error);
        });
    }

    async function openSaveTarget(suggestedName) {
        if (!window.showSaveFilePicker) {
            throw new Error("showSaveFilePicker is not supported in this browser.");
        }

        const startInHandle = await getStoredHandle();
        const options = {
            id: "hotelmanager-invoice-export",
            suggestedName: suggestedName || "invoice.pdf",
            types: [
                {
                    description: "PDF invoice",
                    accept: {
                        "application/pdf": [".pdf"]
                    }
                }
            ]
        };

        if (startInHandle) {
            options.startIn = startInHandle;
        }

        const fileHandle = await window.showSaveFilePicker(options);
        const token = crypto.randomUUID();
        activeHandles.set(token, fileHandle);
        return token;
    }

    async function saveFromUrlToTarget(token, url) {
        const fileHandle = activeHandles.get(token);
        if (!fileHandle) {
            throw new Error("No active save target available.");
        }

        const response = await fetch(url, { credentials: "same-origin" });
        if (!response.ok) {
            throw new Error(`Failed to fetch invoice PDF (${response.status}).`);
        }

        const blob = await response.blob();
        const writable = await fileHandle.createWritable();
        await writable.write(blob);
        await writable.close();

        await storeHandle(fileHandle);
        activeHandles.delete(token);
    }

    function cancelSaveTarget(token) {
        activeHandles.delete(token);
    }

    async function triggerBrowserDownload(url, suggestedName) {
        const response = await fetch(url, { credentials: "same-origin" });
        if (!response.ok) {
            throw new Error(`Failed to fetch invoice PDF (${response.status}).`);
        }

        const blob = await response.blob();
        const blobUrl = URL.createObjectURL(blob);

        const anchor = document.createElement("a");
        anchor.href = blobUrl;
        anchor.download = suggestedName || "invoice.pdf";
        document.body.appendChild(anchor);
        anchor.click();
        anchor.remove();

        URL.revokeObjectURL(blobUrl);
    }

    function openPdfInNewTab(url) {
        const anchor = document.createElement("a");
        anchor.href = url;
        anchor.target = "_blank";
        anchor.rel = "noopener noreferrer";
        document.body.appendChild(anchor);
        anchor.click();
        anchor.remove();
    }

    window.invoiceExport = {
        openSaveTarget,
        saveFromUrlToTarget,
        cancelSaveTarget,
        triggerBrowserDownload,
        openPdfInNewTab
    };
})();
