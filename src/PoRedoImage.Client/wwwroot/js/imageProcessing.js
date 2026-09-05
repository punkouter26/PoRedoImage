// imageProcessing — client-side image processing helpers (resize, preview URL helpers).
window.imageProcessing = (function () {
    return {
        // Returns a data URL for a File/Blob scaled to maxDimension pixels on the longest side.
        resizeToDataUrl: function (file, maxDimension) {
            return new Promise((resolve, reject) => {
                const reader = new FileReader();
                reader.onload = function (e) {
                    const img = new Image();
                    img.onload = function () {
                        const scale = Math.min(1, maxDimension / Math.max(img.width, img.height));
                        const canvas = document.createElement('canvas');
                        canvas.width = Math.round(img.width * scale);
                        canvas.height = Math.round(img.height * scale);
                        canvas.getContext('2d').drawImage(img, 0, 0, canvas.width, canvas.height);
                        resolve(canvas.toDataURL('image/jpeg', 0.9));
                    };
                    img.onerror = reject;
                    img.src = e.target.result;
                };
                reader.onerror = reject;
                reader.readAsDataURL(file);
            });
        },

        // Downscales a data URL so its longest edge is at most maxEdge, preserving the source
        // format. Returns the ORIGINAL string unchanged when the image is already small enough or
        // when anything goes wrong — a failed optimisation must never become a failed upload.
        //
        // Why this is worth doing on every intake: vision models resolve detail at roughly 1-2
        // megapixels no matter what you send them, and Gemini's reference-image path is no
        // different. A 12MP phone photo is ~6x the pixels any model in this pipeline will use, and
        // it is carried as base64 (+33%) through analysis, generation and every bulk batch.
        //
        // The source format is preserved rather than always re-encoding to JPEG. Re-encoding a PNG
        // would flatten transparency to black, and pasted screenshots — a first-class intake path
        // here — are routinely PNGs with alpha. Pixel count is where the saving is anyway.
        downscale: function (dataUrl, maxEdge) {
            return new Promise(function (resolve) {
                try {
                    const img = new Image();
                    img.onload = function () {
                        const longest = Math.max(img.width, img.height);
                        if (!longest || longest <= maxEdge) { resolve(dataUrl); return; }

                        const scale = maxEdge / longest;
                        const canvas = document.createElement('canvas');
                        canvas.width = Math.round(img.width * scale);
                        canvas.height = Math.round(img.height * scale);
                        const ctx = canvas.getContext('2d');
                        ctx.imageSmoothingQuality = 'high';
                        ctx.drawImage(img, 0, 0, canvas.width, canvas.height);

                        let out;
                        const webp = canvas.toDataURL('image/webp', 0.85);
                        if (webp.startsWith('data:image/webp')) {
                            out = webp;
                        } else {
                            const isJpeg = dataUrl.startsWith('data:image/jpeg');
                            out = isJpeg
                                ? canvas.toDataURL('image/jpeg', 0.88)
                                : canvas.toDataURL('image/png');
                        }

                        // A re-encode can occasionally come out LARGER than the source (already
                        // well-compressed PNGs do this). Keep whichever is smaller.
                        resolve(out.length < dataUrl.length ? out : dataUrl);
                    };
                    img.onerror = function () { resolve(dataUrl); };
                    img.src = dataUrl;
                } catch {
                    resolve(dataUrl);
                }
            });
        }
    };
})();

// Trigger a browser file download from a URL (blob storage URL or data URL).
window.downloadImage = async function (url, filename) {
    try {
        const response = await fetch(url);
        if (!response.ok) throw new Error('Fetch failed: ' + response.status);
        const blob = await response.blob();
        const blobUrl = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = blobUrl;
        a.download = filename || 'image.png';
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(blobUrl);
        return true;
    } catch (e) {
        console.error('downloadImage failed:', e);
        return false;
    }
};
