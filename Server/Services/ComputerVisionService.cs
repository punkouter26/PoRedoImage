using Azure;
using Azure.AI.Vision.ImageAnalysis;
using Microsoft.ApplicationInsights;
using Serilog;

namespace Server.Services;

/// <summary>
/// Interface for the Azure Computer Vision service
/// </summary>
public interface IComputerVisionService
{
    /// <summary>
    /// Analyzes an image and generates a description
    /// </summary>
    /// <param name="imageData">The binary image data to analyze</param>
    /// <returns>Analysis result with description and tags</returns>
    Task<(string Description, List<string> Tags, double ConfidenceScore, long ProcessingTimeMs)> AnalyzeImageAsync(
        byte[] imageData);
}

/// <summary>
/// Implementation of Computer Vision service using Azure AI Vision
/// </summary>
public class ComputerVisionService : IComputerVisionService
{
    private readonly ILogger<ComputerVisionService> _logger;
    private readonly TelemetryClient _telemetryClient;
    private readonly string _endpoint;
    private readonly string _key;
    private readonly string _apiVersion;

    public ComputerVisionService(
        IConfiguration configuration,
        ILogger<ComputerVisionService> logger,
        TelemetryClient telemetryClient)
    {
        _logger = logger;
        _telemetryClient = telemetryClient;

        // Debug: Log all ComputerVision configuration values
        var allCvConfig = configuration.GetSection("ComputerVision").GetChildren();
        _logger.LogInformation("=== DEBUG: All ComputerVision configuration values ===");
        foreach (var config in allCvConfig)
        {
            _logger.LogInformation("ComputerVision:{Key} = {Value}", config.Key, config.Value);
        }
        _logger.LogInformation("=== End ComputerVision configuration ===");

        _endpoint = configuration["ComputerVision:Endpoint"] ??
            throw new ArgumentNullException("ComputerVision:Endpoint is not configured");
        _key = configuration["ComputerVision:ApiKey"] ?? configuration["ComputerVision:Key"] ??
            throw new ArgumentNullException("ComputerVision:ApiKey or ComputerVision:Key is not configured");
        _apiVersion = configuration["ComputerVision:ApiVersion"] ?? "2023-10-01";

        _logger.LogInformation("Computer Vision Service initialized with endpoint: {Endpoint}, API version: {ApiVersion}",
            _endpoint, _apiVersion);
    }

    /// <summary>
    /// Analyzes an image and generates a description using Azure Computer Vision
    /// </summary>
    public async Task<(string Description, List<string> Tags, double ConfidenceScore, long ProcessingTimeMs)> AnalyzeImageAsync(
        byte[] imageData)
    {
        // Validate inputs
        if (imageData == null)
            throw new ArgumentNullException(nameof(imageData));
        if (imageData.Length == 0)
            throw new ArgumentException("Image data cannot be empty", nameof(imageData));

        _logger.LogInformation("Starting image analysis with Azure Computer Vision");
        
        Log.Information("=== STATE CHANGE: Computer Vision Analysis Started ===");
        Log.Information("Image size: {Size} bytes", imageData.Length);
        
        var startTime = DateTime.UtcNow;

        try
        {
            // Create the client and set the analysis options
            var credential = new AzureKeyCredential(_key);
            var client = new ImageAnalysisClient(new Uri(_endpoint), credential);

            // Update: specify visual features instead of analysis options
            // The API has changed and now uses VisualFeatures enum instead of ImageAnalysisOptions.Features
            // Using Caption and Tags features (supported in East US region)
            var visualFeatures = VisualFeatures.Caption | VisualFeatures.Tags;

            // Analyze the image
            var response = await client.AnalyzeAsync(
                BinaryData.FromBytes(imageData),
                visualFeatures,
                new ImageAnalysisOptions { Language = "en", GenderNeutralCaption = true });

            // Extract description and tags
            var description = response.Value.Caption?.Text ?? "No description available";
            var confidenceScore = response.Value.Caption?.Confidence ?? 0;

            var tags = new List<string>();
            if (response.Value.Tags != null)
            {
                // Access the Tags collection through its Values property
                foreach (var tag in response.Value.Tags.Values)
                {
                    tags.Add(tag.Name);
                }
            }

            var processingTime = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;

            Log.Information("=== STATE CHANGE: Computer Vision Analysis Completed ===");
            Log.Information("Processing time: {ProcessingTime}ms", processingTime);
            Log.Information("Confidence score: {Confidence:F2}", confidenceScore);
            Log.Information("Description: {Description}", description);
            Log.Information("Tags found: {TagCount} - {Tags}", tags.Count, string.Join(", ", tags));

            _logger.LogInformation("Image analysis completed in {ProcessingTime}ms, confidence: {Confidence}",
                processingTime, confidenceScore);

            // Track metrics in Application Insights
            _telemetryClient.TrackMetric("ComputerVisionProcessingTime", processingTime);
            _telemetryClient.TrackMetric("ComputerVisionConfidence", confidenceScore);

            return (description, tags, confidenceScore, processingTime);
        }
        catch (Exception ex)
        {
            var processingTime = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
            
            Log.Error(ex, "=== ERROR: Computer Vision Analysis Failed ===");
            Log.Information("Processing time before error: {ProcessingTime}ms", processingTime);
            
            _logger.LogError(ex, "Error analyzing image with Azure Computer Vision after {ProcessingTime}ms", processingTime);
            _telemetryClient.TrackException(ex, new Dictionary<string, string>
            {
                { "Service", "AzureComputerVision" },
                { "ProcessingTime", processingTime.ToString() }
            });

            throw; // Rethrow to be handled by the controller
        }
    }
}