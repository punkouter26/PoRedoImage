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
