using Microsoft.AspNetCore.Mvc;
using ImageGc.Shared.Models;
using Server.Services;
using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Serilog;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;

namespace Server.Controllers;

/// <summary>
/// Controller for image analysis workflow.
/// Uses Application Insights to track custom events and metrics for the complete pipeline.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ImageAnalysisController : ControllerBase
{
    private readonly ILogger<ImageAnalysisController> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly TelemetryClient _telemetryClient;

    public ImageAnalysisController(
        ILogger<ImageAnalysisController> logger,
        IServiceProvider serviceProvider,
        TelemetryClient telemetryClient)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _telemetryClient = telemetryClient;
    }
    [HttpPost("analyze")]
    [AllowAnonymous] // Allow anonymous access for debugging
    public async Task<ActionResult<ImageAnalysisResult>> AnalyzeImage([FromBody] ImageAnalysisRequest request)
    {
        // Start tracking this operation with Application Insights
        var operation = _telemetryClient.StartOperation<RequestTelemetry>("ImageAnalysisWorkflow");
        operation.Telemetry.Properties["FileName"] = request.FileName;
        operation.Telemetry.Properties["ContentType"] = request.ContentType;
        operation.Telemetry.Properties["DescriptionLength"] = request.DescriptionLength.ToString();

        try
        {
            // Try to get services from DI container
            var computerVisionService = _serviceProvider.GetService<IComputerVisionService>();
            var openAIService = _serviceProvider.GetService<IOpenAIService>();
            var memeGeneratorService = _serviceProvider.GetService<IMemeGeneratorService>();

            if (computerVisionService == null || openAIService == null)
            {
                _logger.LogError("Required services not available. ComputerVision: {CV}, OpenAI: {AI}",
                    computerVisionService != null, openAIService != null);
                
                // Track service unavailability event
                _telemetryClient.TrackEvent("ServiceUnavailable", new Dictionary<string, string>
                {
                    { "ComputerVisionAvailable", (computerVisionService != null).ToString() },
                    { "OpenAIAvailable", (openAIService != null).ToString() },
                    { "MemeGeneratorAvailable", (memeGeneratorService != null).ToString() }
                });

                operation.Telemetry.Success = false;
                return StatusCode(503, new { error = "Required AI services are not available" });
            }

            // Get the authenticated user (allow anonymous for now)
            var userId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
            var userName = User?.FindFirst(ClaimTypes.Name)?.Value ?? "anonymous";

            operation.Telemetry.Properties["UserId"] = userId;
            operation.Telemetry.Properties["UserName"] = userName;

            Log.Information("=== USER ACTION: Image Analysis Started ===");
            Log.Information("User: {UserId} ({UserName})", userId, userName);
            Log.Information("File: {FileName}, Content Type: {ContentType}, Description Length: {Length} words, Mode: {Mode}",
                request.FileName, request.ContentType, request.DescriptionLength, request.Mode);

            _logger.LogInformation("Image analysis request received from user {UserId} ({UserName}). File: {FileName}, Mode: {Mode}",
                userId, userName, request.FileName, request.Mode);

            // Track custom event: Image analysis started
            _telemetryClient.TrackEvent("ImageAnalysisStarted", new Dictionary<string, string>
            {
                { "UserId", userId },
                { "UserName", userName },
                { "FileName", request.FileName },
                { "ContentType", request.ContentType },
                { "DescriptionLength", request.DescriptionLength.ToString() },
                { "Mode", request.Mode.ToString() }
            });

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
                
                // Track image size metric
                _telemetryClient.TrackMetric("ImageSizeBytes", imageBytes.Length);
                operation.Telemetry.Properties["ImageSizeBytes"] = imageBytes.Length.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decode base64 image data");
                
                // Track decode failure
                _telemetryClient.TrackException(ex, new Dictionary<string, string>
                {
                    { "ErrorType", "Base64DecodeFailed" },
                    { "UserId", userId }
                });

                operation.Telemetry.Success = false;
                return BadRequest(new { error = "Invalid image data format" });
            }

            // Server-side validation for file type and size
            const int MaxFileSize = 20 * 1024 * 1024; // 20MB
            if (imageBytes.Length > MaxFileSize)
            {
                _logger.LogWarning("Received image exceeds maximum size limit. Size: {Size} bytes", imageBytes.Length);
                
                // Track validation failure
                _telemetryClient.TrackEvent("ValidationFailed", new Dictionary<string, string>
                {
                    { "Reason", "FileSizeTooLarge" },
                    { "SizeBytes", imageBytes.Length.ToString() },
                    { "MaxSizeBytes", MaxFileSize.ToString() }
                });

                operation.Telemetry.Success = false;
                return BadRequest(new { error = $"File size exceeds the maximum allowed ({MaxFileSize / 1024 / 1024}MB)." });
            }

            if (request.ContentType != "image/jpeg" && request.ContentType != "image/png")
            {
                _logger.LogWarning("Received image with unsupported content type: {ContentType}", request.ContentType);
                
                // Track validation failure
                _telemetryClient.TrackEvent("ValidationFailed", new Dictionary<string, string>
                {
                    { "Reason", "UnsupportedContentType" },
                    { "ContentType", request.ContentType }
                });

                operation.Telemetry.Success = false;
                return BadRequest(new { error = "Only JPG and PNG files are supported." });
            }

            // Step 1: Analyze image with Computer Vision for tags and confidence
            _logger.LogInformation("Step 1: Analyzing image with Computer Vision for tags");
            List<string> tags;
            double confidenceScore;
            try
            {
                var cvStopwatch = Stopwatch.StartNew();
                var (description, imageTags, confidence, processingTime) =
                    await computerVisionService.AnalyzeImageAsync(imageBytes);

                // We'll only use tags and confidence, not the basic description
                tags = imageTags;
                confidenceScore = confidence;
                result.Metrics.ImageAnalysisTimeMs = processingTime;

                _logger.LogInformation("Computer Vision analysis completed. Tags: {TagCount}, Confidence: {Confidence}",
                    tags.Count, confidenceScore);

                // Track Computer Vision metrics
                _telemetryClient.TrackMetric("ComputerVisionProcessingTimeMs", processingTime);
                _telemetryClient.TrackMetric("ComputerVisionConfidence", confidenceScore);
                _telemetryClient.TrackMetric("ComputerVisionTagCount", tags.Count);
                
                operation.Telemetry.Properties["TagCount"] = tags.Count.ToString();
                operation.Telemetry.Properties["ConfidenceScore"] = confidenceScore.ToString("F2");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during Computer Vision analysis");
                result.Metrics.ErrorInfo = $"Computer Vision analysis failed: {ex.Message}";
                
                // Track Computer Vision failure
                _telemetryClient.TrackException(ex, new Dictionary<string, string>
                {
                    { "ErrorType", "ComputerVisionAnalysisFailed" },
                    { "UserId", userId }
                });

                operation.Telemetry.Success = false;
                return StatusCode(500, result);
            }

            // Branch based on processing mode
            if (request.Mode == ProcessingMode.MemeGeneration)
            {
                // MEME GENERATION MODE
                _logger.LogInformation("Processing in MEME GENERATION mode");

                // Step 2: Generate funny meme caption with OpenAI
                _logger.LogInformation("Step 2 (Meme): Generating funny meme caption with OpenAI");
                string topText = "";
                string bottomText = "";
                try
                {
                    var (top, bottom, tokensUsed, processingTime) =
                        await openAIService.GenerateMemeCaptionAsync(tags, confidenceScore);

                    topText = top;
                    bottomText = bottom;
                    result.MemeCaption = $"{topText}\n{bottomText}";
                    result.Metrics.DescriptionGenerationTimeMs = processingTime;
                    result.Metrics.DescriptionTokensUsed = tokensUsed;

                    _logger.LogInformation("Meme caption generation completed. Top: '{Top}', Bottom: '{Bottom}', Tokens: {Tokens}",
                        topText, bottomText, tokensUsed);

                    // Track meme caption metrics
                    _telemetryClient.TrackMetric("OpenAIMemeCaptionProcessingTimeMs", processingTime);
                    _telemetryClient.TrackMetric("OpenAIMemeCaptionTokensUsed", tokensUsed);
                    
                    operation.Telemetry.Properties["MemeCaptionTokensUsed"] = tokensUsed.ToString();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during meme caption generation");
                    result.Metrics.ErrorInfo = $"Meme caption generation failed: {ex.Message}";
                    
                    // Track caption generation failure
                    _telemetryClient.TrackException(ex, new Dictionary<string, string>
                    {
                        { "ErrorType", "MemeCaptionGenerationFailed" },
                        { "UserId", userId }
                    });

                    // Use fallback captions
                    topText = "WHEN YOU UPLOAD";
                    bottomText = "AN AWESOME IMAGE";
                    result.MemeCaption = $"{topText}\n{bottomText}";
                }

                // Step 3: Overlay caption on original image
                _logger.LogInformation("Step 3 (Meme): Overlaying caption on image");
                try
                {
                    if (memeGeneratorService == null)
                    {
                        throw new InvalidOperationException("Meme generator service not available");
                    }

                    var memeStopwatch = Stopwatch.StartNew();
                    var memeImageBytes = memeGeneratorService.AddCaptionToImage(imageBytes, topText, bottomText);
                    memeStopwatch.Stop();

                    result.MemeImageData = Convert.ToBase64String(memeImageBytes);
                    result.RegeneratedImageContentType = "image/png";
                    result.Metrics.ImageRegenerationTimeMs = memeStopwatch.ElapsedMilliseconds;

                    _logger.LogInformation("Meme overlay completed. Size: {Size} bytes, Time: {Time}ms",
                        memeImageBytes.Length, memeStopwatch.ElapsedMilliseconds);

                    // Track meme generation metrics
                    _telemetryClient.TrackMetric("MemeOverlayProcessingTimeMs", memeStopwatch.ElapsedMilliseconds);
                    _telemetryClient.TrackMetric("MemeImageSizeBytes", memeImageBytes.Length);
                    
                    operation.Telemetry.Properties["MemeImageSizeBytes"] = memeImageBytes.Length.ToString();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during meme image generation");
                    result.Metrics.ErrorInfo = $"Meme image generation failed: {ex.Message}";
                    
                    // Track meme generation failure
                    _telemetryClient.TrackException(ex, new Dictionary<string, string>
                    {
                        { "ErrorType", "MemeImageGenerationFailed" },
                        { "UserId", userId }
                    });
                }

                // Set description to simple tag list for meme mode
                result.Description = $"Image contains: {string.Join(", ", tags)}";
            }
            else
            {
                // ORIGINAL IMAGE REGENERATION MODE
                _logger.LogInformation("Processing in IMAGE REGENERATION mode");

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

                    // Track OpenAI text generation metrics
                    _telemetryClient.TrackMetric("OpenAIDescriptionProcessingTimeMs", processingTime);
                    _telemetryClient.TrackMetric("OpenAIDescriptionTokensUsed", tokensUsed);
                    _telemetryClient.TrackMetric("DescriptionLength", detailedDescription.Length);
                    
                    operation.Telemetry.Properties["DescriptionTokensUsed"] = tokensUsed.ToString();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during detailed description generation");
                    // Fall back to a basic description from tags
                    detailedDescription = $"Image contains: {string.Join(", ", tags)}";
                    result.Metrics.ErrorInfo = $"Detailed description generation failed: {ex.Message}";
                    
                    // Track description generation failure
                    _telemetryClient.TrackException(ex, new Dictionary<string, string>
                    {
                        { "ErrorType", "DescriptionGenerationFailed" },
                        { "UserId", userId }
                    });
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

                    // Track DALL-E image generation metrics
                    _telemetryClient.TrackMetric("DALLEProcessingTimeMs", processingTime);
                    _telemetryClient.TrackMetric("DALLETokensUsed", tokensUsed);
                    _telemetryClient.TrackMetric("RegeneratedImageSizeBytes", imageData.Length);
                    
                    operation.Telemetry.Properties["RegenerationTokensUsed"] = tokensUsed.ToString();
                    operation.Telemetry.Properties["RegeneratedImageSizeBytes"] = imageData.Length.ToString();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during image generation");
                    result.Metrics.ErrorInfo = $"Image generation failed: {ex.Message}";
                    
                    // Track image generation failure
                    _telemetryClient.TrackException(ex, new Dictionary<string, string>
                    {
                        { "ErrorType", "ImageGenerationFailed" },
                        { "UserId", userId },
                        { "Description", detailedDescription }
                    });
                    
                    // We'll return what we have so far
                }

                // Complete the result
                result.Description = detailedDescription;
            }

            // Common result completion
            result.Tags = tags;
            result.ConfidenceScore = confidenceScore;

            stopwatch.Stop();
            var totalTime = stopwatch.ElapsedMilliseconds;

            Log.Information("=== USER ACTION: Image Analysis Completed ===");
            Log.Information("Total processing time: {TotalTime}ms", totalTime);
            Log.Information("Mode: {Mode}", request.Mode);
            if (request.Mode == ProcessingMode.MemeGeneration)
            {
                Log.Information("Meme caption: {Caption}", result.MemeCaption);
            }
            else
            {
                Log.Information("Description length: {DescriptionLength} characters", result.Description.Length);
            }
            Log.Information("Tags found: {TagCount}", tags.Count);

            _logger.LogInformation("Image analysis completed in {TotalTime}ms for user {UserName} (Mode: {Mode})", 
                totalTime, userName, request.Mode);

            // Track overall success and timing
            _telemetryClient.TrackMetric("ImageAnalysisTotalTimeMs", totalTime);
            _telemetryClient.TrackEvent("ImageAnalysisCompleted", new Dictionary<string, string>
            {
                { "UserId", userId },
                { "UserName", userName },
                { "TotalTimeMs", totalTime.ToString() },
                { "TagCount", tags.Count.ToString() },
                { "Mode", request.Mode.ToString() },
                { "Success", "true" }
            });

            operation.Telemetry.Properties["TotalTimeMs"] = totalTime.ToString();
            operation.Telemetry.Success = true;

            return Ok(result);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "=== ERROR: Image Analysis Failed ===");
            _logger.LogError(ex, "Unhandled error processing image analysis request");

            // Track unhandled exception
            _telemetryClient.TrackException(ex, new Dictionary<string, string>
            {
                { "ErrorType", "UnhandledImageAnalysisError" }
            });

            operation.Telemetry.Success = false;

            return StatusCode(500, new ImageAnalysisResult
            {
                Metrics = new ProcessingMetrics
                {
                    ErrorInfo = $"Error: {ex.Message}"
                }
            });
        }
        finally
        {
            // Stop the operation tracking
            _telemetryClient.StopOperation(operation);
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
