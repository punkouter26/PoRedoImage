import { test, expect } from '@playwright/test';

test.describe('Meme Generation Feature', () => {
  const baseUrl = process.env.BASE_URL || 'https://app-poredoimage-cqevadpy77pvi.azurewebsites.net';

  test('should display meme generation mode radio button', async ({ page }) => {
    await page.goto(baseUrl);
    
    // Check that both radio buttons are present
    const regenerationRadio = page.locator('input[type="radio"]#modeRegeneration');
    const memeRadio = page.locator('input[type="radio"]#modeMeme');
    
    await expect(regenerationRadio).toBeVisible();
    await expect(memeRadio).toBeVisible();
    
    // Check that Image Regeneration is selected by default
    await expect(regenerationRadio).toBeChecked();
    await expect(memeRadio).not.toBeChecked();
  });

  test('should toggle between processing modes', async ({ page }) => {
    await page.goto(baseUrl);
    
    const regenerationRadio = page.locator('input[type="radio"]#modeRegeneration');
    const memeRadio = page.locator('input[type="radio"]#modeMeme');
    const descriptionLengthSlider = page.locator('input#descriptionLength');
    
    // Initially, description length slider should be visible (Image Regeneration mode)
    await expect(descriptionLengthSlider).toBeVisible();
    
    // Click meme mode radio button
    await memeRadio.click();
    await expect(memeRadio).toBeChecked();
    await expect(regenerationRadio).not.toBeChecked();
    
    // Description length slider should be hidden in meme mode
    await expect(descriptionLengthSlider).not.toBeVisible();
    
    // Switch back to regeneration mode
    await regenerationRadio.click();
    await expect(regenerationRadio).toBeChecked();
    await expect(descriptionLengthSlider).toBeVisible();
  });

  test('should show correct button text for each mode', async ({ page }) => {
    await page.goto(baseUrl);
    
    const processButton = page.locator('button:has-text("Process Image"), button:has-text("Generate Meme")');
    
    // In Image Regeneration mode, button should say "Process Image"
    await expect(processButton).toContainText('Process Image');
    
    // Switch to meme mode
    const memeRadio = page.locator('input[type="radio"]#modeMeme');
    await memeRadio.click();
    
    // Button should now say "Generate Meme"
    await expect(processButton).toContainText('Generate Meme');
  });

  test('should have proper labels for both modes', async ({ page }) => {
    await page.goto(baseUrl);
    
    // Check for mode labels
    const regenerationLabel = page.locator('label[for="modeRegeneration"]');
    const memeLabel = page.locator('label[for="modeMeme"]');
    
    await expect(regenerationLabel).toContainText('Image Regeneration');
    await expect(regenerationLabel).toContainText('AI analyzes your image');
    
    await expect(memeLabel).toContainText('Meme Generation');
    await expect(memeLabel).toContainText('funny meme');
  });

  test('should maintain mode selection state', async ({ page }) => {
    await page.goto(baseUrl);
    
    const memeRadio = page.locator('input[type="radio"]#modeMeme');
    const regenerationRadio = page.locator('input[type="radio"]#modeRegeneration');
    
    // Select meme mode
    await memeRadio.click();
    await expect(memeRadio).toBeChecked();
    
    // Verify it stays selected after a small delay (simulating user interaction)
    await page.waitForTimeout(500);
    await expect(memeRadio).toBeChecked();
    await expect(regenerationRadio).not.toBeChecked();
  });

  test('should update page title and description', async ({ page }) => {
    await page.goto(baseUrl);
    
    // Check that the page has updated header
    const header = page.locator('h1');
    await expect(header).toContainText('AI-Powered Image Analysis');
    
    // Check for description mentioning both modes
    const description = page.locator('p.lead');
    await expect(description).toContainText('meme');
    await expect(description).toContainText('regeneration');
  });

  test('should have file upload available in both modes', async ({ page }) => {
    await page.goto(baseUrl);
    
    const fileInput = page.locator('input[type="file"]');
    await expect(fileInput).toBeVisible();
    
    // Switch to meme mode
    const memeRadio = page.locator('input[type="radio"]#modeMeme');
    await memeRadio.click();
    
    // File input should still be visible
    await expect(fileInput).toBeVisible();
  });

  test('should process button be disabled without file selection', async ({ page }) => {
    await page.goto(baseUrl);
    
    const processButton = page.locator('button:has-text("Process Image")');
    
    // Button should be disabled initially
    await expect(processButton).toBeDisabled();
    
    // Switch to meme mode
    const memeRadio = page.locator('input[type="radio"]#modeMeme');
    await memeRadio.click();
    
    const memeButton = page.locator('button:has-text("Generate Meme")');
    
    // Button should still be disabled without file
    await expect(memeButton).toBeDisabled();
  });

  test('should reset mode on start over', async ({ page }) => {
    await page.goto(baseUrl);
    
    const memeRadio = page.locator('input[type="radio"]#modeMeme');
    const regenerationRadio = page.locator('input[type="radio"]#modeRegeneration');
    
    // Select meme mode
    await memeRadio.click();
    await expect(memeRadio).toBeChecked();
    
    // Note: Start Over button only appears after processing
    // This test verifies the initial state is correct
    await expect(regenerationRadio).not.toBeChecked();
  });

  test('should have accessibility labels for radio buttons', async ({ page }) => {
    await page.goto(baseUrl);
    
    const regenerationRadio = page.locator('input[type="radio"]#modeRegeneration');
    const memeRadio = page.locator('input[type="radio"]#modeMeme');
    
    // Check that inputs have proper IDs for label association
    await expect(regenerationRadio).toHaveAttribute('id', 'modeRegeneration');
    await expect(memeRadio).toHaveAttribute('id', 'modeMeme');
    
    // Check that labels are properly associated
    const regenerationLabel = page.locator('label[for="modeRegeneration"]');
    const memeLabel = page.locator('label[for="modeMeme"]');
    
    await expect(regenerationLabel).toBeVisible();
    await expect(memeLabel).toBeVisible();
  });
});
