/**
 * Image processing utility functions
 */

/**
 * Downloads an image from a data URL
 * @param {string} dataUrl - The data URL of the image to download
 * @param {string} fileName - The filename to use for the download
 */
function downloadImage(dataUrl, fileName) {
    try {
        const link = document.createElement('a');
        link.href = dataUrl;
        link.download = fileName;
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
        return true;
    } catch (error) {
        console.error("Error downloading image:", error);
        return false;
    }
}

/**
 * Creates a side-by-side comparison image and initiates its download
 * @param {string} originalImageUrl - The data URL of the original image
 * @param {string} regeneratedImageUrl - The data URL of the regenerated image
 * @param {string} fileName - The filename to use for the download
 */
function createSideBySideComparison(originalImageUrl, regeneratedImageUrl, fileName) {
    return new Promise((resolve, reject) => {
        if (!originalImageUrl || !regeneratedImageUrl) {
            reject(new Error("Both images are required for comparison"));
            return;
        }

        const canvas = document.createElement('canvas');
        const ctx = canvas.getContext('2d');
        
        // Create image elements for loading the images
        const originalImage = new Image();
        const regeneratedImage = new Image();
        
        // Set a timeout to avoid hanging if images don't load
        const timeout = setTimeout(() => {
            reject(new Error("Image loading timed out"));
        }, 15000); // 15 second timeout
        
        // Count loaded images
        let loadedImages = 0;
        const onImageLoad = () => {
            loadedImages++;
            
            // Once both images are loaded, create the comparison
            if (loadedImages === 2) {
                clearTimeout(timeout);
                try {
                    // Set canvas size to fit both images side by side
                    const maxHeight = Math.max(originalImage.height, regeneratedImage.height);
                    const totalWidth = originalImage.width + regeneratedImage.width;
                    
                    // Ensure reasonable dimensions (max 3000px width or height)
                    const maxDimension = 3000;
                    let scale = 1;
                    
                    if (totalWidth > maxDimension || maxHeight > maxDimension) {
                        scale = Math.min(maxDimension / totalWidth, maxDimension / maxHeight);
                    }
                    
                    const scaledWidth = Math.floor(totalWidth * scale);
                    const scaledHeight = Math.floor(maxHeight * scale);
                    
                    canvas.width = scaledWidth;
                    canvas.height = scaledHeight;
                    
                    // Fill canvas with white background
                    ctx.fillStyle = 'white';
                    ctx.fillRect(0, 0, canvas.width, canvas.height);
                    
                    // Draw original image on the left (scaled if necessary)
                    const originalWidth = Math.floor(originalImage.width * scale);
                    const originalHeight = Math.floor(originalImage.height * scale);
                    ctx.drawImage(originalImage, 0, 0, originalWidth, originalHeight);
                    
                    // Draw regenerated image on the right (scaled if necessary)
                    const regenWidth = Math.floor(regeneratedImage.width * scale);
                    const regenHeight = Math.floor(regeneratedImage.height * scale);
                    ctx.drawImage(regeneratedImage, originalWidth, 0, regenWidth, regenHeight);
                    
                    // Add separator line
                    ctx.beginPath();
                    ctx.moveTo(originalWidth, 0);
                    ctx.lineTo(originalWidth, scaledHeight);
                    ctx.strokeStyle = "#333";
                    ctx.lineWidth = 2;
                    ctx.stroke();
                    
                    // Add labels with nice styling
                    ctx.fillStyle = 'rgba(0, 0, 0, 0.7)';
                    ctx.fillRect(10, 10, 100, 30);
                    ctx.fillRect(originalWidth + 10, 10, 120, 30);
                    
                    ctx.fillStyle = 'white';
                    ctx.font = 'bold 16px Arial';
                    ctx.fillText('Original', 20, 30);
                    ctx.fillText('Regenerated', originalWidth + 20, 30);
                    
                    // Convert to data URL and download
                    const dataUrl = canvas.toDataURL('image/png');
                    downloadImage(dataUrl, fileName);
                    
                    resolve();
                } catch (error) {
                    reject(error);
                }
            }
        };
        
        // Handle load errors
        const onImageError = (error) => {
            clearTimeout(timeout);
            reject(new Error(`Failed to load image: ${error.message || 'Unknown error'}`));
        };
        
        // Set up event handlers
        originalImage.onload = onImageLoad;
        regeneratedImage.onload = onImageLoad;
        originalImage.onerror = onImageError;
        regeneratedImage.onerror = onImageError;
        
        // Set crossOrigin to anonymous to prevent tainted canvas issues
        originalImage.crossOrigin = 'anonymous';
        regeneratedImage.crossOrigin = 'anonymous';
        
        // Trigger image loading
        originalImage.src = originalImageUrl;
        regeneratedImage.src = regeneratedImageUrl;
    });
}
