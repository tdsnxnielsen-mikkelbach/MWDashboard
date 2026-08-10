window.mwApiDocs = {
    // POSTs the API key to the gated unlock endpoint; on success the server sets a
    // session cookie so the same-origin Scalar UI (and its OpenAPI fetch) pass the gate.
    unlock: async function (key) {
        try {
            const res = await fetch('/api-docs/unlock', {
                method: 'POST',
                headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
                body: 'key=' + encodeURIComponent(key)
            });
            return res.ok;
        } catch {
            return false;
        }
    }
};
