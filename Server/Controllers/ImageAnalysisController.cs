using Microsoft.AspNetCore.Mvc;
using ImageGc.Shared.Models;
using Server.Services;
using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Serilog;

namespace Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ImageAnalysisController : ControllerBase
{
    private readonly ILogger<ImageAnalysisController> _logger;
    private readonly IServiceProvider _serviceProvider;

    public ImageAnalysisController(
        ILogger<ImageAnalysisController> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }
    [HttpPost("analyze")]
    [AllowAnonymous] // Allow anonymous access for debugging
    public async Task<ActionResult<ImageAnalysisResult>> AnalyzeImage([FromBody] ImageAnalysisRequest request)
    {
        try
        {
            // Try to get services from DI container
            var computerVisionService = _serviceProvider.GetService<IComputerVisionService>();
            var openAIService = _serviceProvider.GetService<IOpenAIService>();
            
            if (computerVisionService == null || openAIService == null)
            {
                _logger.LogError("Required services not available. ComputerVision: {CV}, OpenAI: {AI}", 
                    computerVisionService != null, openAIService != null);
                return StatusCode(503, new { error = "Required AI services are not available" });
            }

            // Get the authenticated user (allow anonymous for now)
            var userId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
            var userName = User?.FindFirst(ClaimTypes.Name)?.Value ?? "anonymous";

            Log.Information("=== USER ACTION: Image Analysis Started ===");
            Log.Information("User: {UserId} ({UserName})", userId, userName);
            Log.Information("File: {FileName}, Content Type: {ContentType}, Description Length: {Length} words",
                request.FileName, request.ContentType, request.DescriptionLength);

            _logger.LogInformation("Image analysis request received from user {UserId} ({UserName}). File: {FileName}, Description Length: {Length} words",
                userId, userName, request.FileName, request.DescriptionLength);

            // Prepare the result object
            var result = new ImageAnalysisResult();

            // Convert base64 image data to bytes
            var stopwatch = Stopwatch.StartNew();
            byte[] imageBytes;
            try
            {
                // Extract the actual base64 data if it contains the data URL prefix
                string base64Data = request.ImageData;
                if (base64Data.Contains(","))
                {
                    base64Data = base64Data.Split(',')[1];
                }

                imageBytes = Convert.FromBase64String(base64Data);
                _logger.LogInformation("Successfully converted image data: {Size} bytes", imageBytes.Length);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decode base64 image data");
                return BadRequest(new { error = "Invalid image data format" });
            }

            // Server-side validation for file type and size
            const int MaxFileSize = 20 * 1024 * 1024; // 20MB
            if (imageBytes.Length > MaxFileSize)
            {
                _logger.LogWarning("Received image exceeds maximum size limit. Size: {Size} bytes", imageBytes.Length);
                return BadRequest(new { error = $"File size exceeds the maximum allowed ({MaxFileSize / 1024 / 1024}MB)." });
            }

            if (request.ContentType != "image/jpeg" && request.ContentType != "image/png")
            {
                _logger.LogWarning("Received image with unsupported content type: {ContentType}", request.ContentType);
                return BadRequest(new { error = "Only JPG and PNG files are supported." });
            }

            // Step 1: Analyze image with Computer Vision for tags and confidence
            _logger.LogInformation("Step 1: Analyzing image with Computer Vision for tags");
            List<string> tags;
            double confidenceScore;
            try
            {
                var (description, imageTags, confidence, processingTime) =
                    await computerVisionService.AnalyzeImageAsync(imageBytes);

                // We'll only use tags and confidence, not the basic description
                tags = imageTags;
                confidenceScore = confidence;
                result.Metrics.ImageAnalysisTimeMs = processingTime;

                _logger.LogInformation("Computer Vision analysis completed. Tags: {TagCount}, Confidence: {Confidence}",
                    tags.Count, confidenceScore);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during Computer Vision analysis");
                result.Metrics.ErrorInfo = $"Computer Vision analysis failed: {ex.Message}";
                return StatusCode(500, result);
            }

            // Step 2: Generate detailed description directly from tags with OpenAI
            _logger.LogInformation("Step 2: Generating detailed description with OpenAI");
            string detailedDescription;
            try
            {
                var (description, tokensUsed, processingTime) =
                    await openAIService.GenerateDetailedDescriptionAsync(tags, request.DescriptionLength, confidenceScore);

                detailedDescription = description;
                result.Metrics.DescriptionGenerationTimeMs = processingTime;
                result.Metrics.DescriptionTokensUsed = tokensUsed;

                _logger.LogInformation("Detailed description generation completed. Length: {Length} characters, Tokens: {Tokens}",
                    detailedDescription.Length, tokensUsed);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during detailed description generation");
                // Fall back to a basic description from tags
                detailedDescription = $"Image contains: {string.Join(", ", tags)}";
                result.Metrics.ErrorInfo = $"Detailed description generation failed: {ex.Message}";
            }

            // Step 3: Generate new image with DALL-E
            _logger.LogInformation("Step 3: Generating image with DALL-E");
            try
            {
                var (imageData, contentType, tokensUsed, processingTime) =
                    await openAIService.GenerateImageAsync(detailedDescription);

                result.RegeneratedImageData = Convert.ToBase64String(imageData);
                result.RegeneratedImageContentType = contentType;
                result.Metrics.ImageRegenerationTimeMs = processingTime;
                result.Metrics.RegenerationTokensUsed = tokensUsed;

                _logger.LogInformation("Image generation completed. Size: {Size} bytes, Tokens: {Tokens}",
                    imageData.Length, tokensUsed);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during image generation");
                result.Metrics.ErrorInfo = $"Image generation failed: {ex.Message}";
                // We'll return what we have so far
            }

            // Complete the result
            result.Description = detailedDescription;
            result.Tags = tags;
            result.ConfidenceScore = confidenceScore;

            stopwatch.Stop();
            var totalTime = stopwatch.ElapsedMilliseconds;
            
            Log.Information("=== USER ACTION: Image Analysis Completed ===");
            Log.Information("Total processing time: {TotalTime}ms", totalTime);
            Log.Information("Description length: {DescriptionLength} characters", detailedDescription.Length);
            Log.Information("Tags found: {TagCount}", tags.Count);
            
            _logger.LogInformation("Image analysis completed in {TotalTime}ms for user {UserName}", totalTime, userName);

            return Ok(result);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "=== ERROR: Image Analysis Failed ===");
            _logger.LogError(ex, "Unhandled error processing image analysis request");

            return StatusCode(500, new ImageAnalysisResult
            {
                Metrics = new ProcessingMetrics
                {
                    ErrorInfo = $"Error: {ex.Message}"
                }
            });
        }
    }

    [HttpGet("test")]
    public ActionResult<object> TestConnection()
    {
        _logger.LogInformation("Test endpoint called");
        return Ok(new
        {
            message = "ImageAnalysis API is working",
            timestamp = DateTime.UtcNow,
            environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Unknown"
        });
    }

    [HttpGet("debug")]
    [AllowAnonymous]
    public ActionResult<object> DebugInfo()
    {
        _logger.LogInformation("Debug endpoint called");

        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Unknown";
        var isAuthenticated = User?.Identity?.IsAuthenticated ?? false;
        var claims = User?.Claims?.Select(c => new { c.Type, c.Value })?.ToArray() ?? Array.Empty<object>();

        return Ok(new
        {
            message = "Debug info for ImageAnalysis API",
            timestamp = DateTime.UtcNow,
            environment = environment,
            isAuthenticated = isAuthenticated,
            userClaims = claims,
            requestHeaders = Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString()),
            baseUrl = $"{Request.Scheme}://{Request.Host}",
            path = Request.Path
        });
    }
}
