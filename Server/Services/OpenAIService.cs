using Azure.AI.OpenAI;
using OpenAI.Chat;
using OpenAI.Images;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Server.Services;

/// <summary>
/// Interface for OpenAI service operations
/// </summary>
public interface IOpenAIService
{
    /// <summary>
    /// Enhances a basic image description to a more detailed one
    /// </summary>
    /// <param name="basicDescription">The basic description from Computer Vision</param>
    /// <param name="tags">Related tags from Computer Vision</param>
    /// <param name="targetLength">Target word count for the enhanced description</param>
    /// <returns>Enhanced description and token usage</returns>
    Task<(string EnhancedDescription, int TokensUsed, long ProcessingTimeMs)> EnhanceDescriptionAsync(
        string basicDescription, List<string> tags, int targetLength);

    /// <summary>
    /// Generates a detailed image description directly from tags and analysis
    /// </summary>
    /// <param name="tags">Tags from Computer Vision analysis</param>
    /// <param name="targetLength">Target word count for the description</param>
    /// <param name="confidenceScore">Confidence score from Computer Vision</param>
    /// <returns>Detailed description and token usage</returns>
    Task<(string DetailedDescription, int TokensUsed, long ProcessingTimeMs)> GenerateDetailedDescriptionAsync(
        List<string> tags, int targetLength, double confidenceScore = 0);

    /// <summary>
    /// Generates a new image based on a description using DALL-E
    /// </summary>
    /// <param name="description">Description to base the image on</param>
    /// <returns>Generated image data, content type, and token usage</returns>
    Task<(byte[] ImageData, string ContentType, int TokensUsed, long ProcessingTimeMs)> GenerateImageAsync(
        string description);

    /// <summary>
    /// Generates a funny meme caption based on image tags and context
    /// </summary>
    /// <param name="tags">Tags from Computer Vision analysis</param>
    /// <param name="confidenceScore">Confidence score from Computer Vision</param>
    /// <returns>Top text, bottom text, tokens used, and processing time</returns>
    Task<(string TopText, string BottomText, int TokensUsed, long ProcessingTimeMs)> GenerateMemeCaptionAsync(
        List<string> tags, double confidenceScore);
}

/// <summary>
/// Implementation of OpenAI service using OpenAI SDK
/// </summary>
public class OpenAIService : IOpenAIService
{
    private readonly ILogger<OpenAIService> _logger;
    private readonly TelemetryClient _telemetryClient;
    private readonly string _endpoint;
    private readonly string _apiKey;
    private readonly string _imageEndpoint;
    private readonly string _imageApiKey;
    private readonly string _chatModel;
    private readonly string _imageModel;
    private readonly string _fallbackChatModel;
    private readonly string _chatDeployment;
    private readonly string _imageDeployment;

    public OpenAIService(
        IConfiguration configuration,
        ILogger<OpenAIService> logger,
        TelemetryClient telemetryClient)
    {
        _logger = logger;
        _telemetryClient = telemetryClient;

        _endpoint = configuration["OpenAI:Endpoint"] ??
            throw new ArgumentNullException("OpenAI:Endpoint is not configured");
        _apiKey = configuration["OpenAI:Key"] ??
            throw new ArgumentNullException("OpenAI:Key is not configured");
        
        // Allow separate endpoint/key for image generation (fallback to main endpoint if not specified)
        _imageEndpoint = configuration["OpenAI:ImageEndpoint"] ?? _endpoint;
        _imageApiKey = configuration["OpenAI:ImageKey"] ?? _apiKey;
        
        _chatModel = configuration["OpenAI:ChatModel"] ?? "gpt-4o";
        _imageModel = configuration["OpenAI:ImageModel"] ?? "dall-e-3";
        _fallbackChatModel = configuration["OpenAI:FallbackChatModel"] ?? "gpt-4o";
        _chatDeployment = configuration["OpenAI:ChatCompletionsDeployment"] ?? _chatModel;
        _imageDeployment = configuration["OpenAI:ImageGenerationDeployment"] ?? _imageModel;

        _logger.LogInformation("OpenAI Service initialized with chat model: {ChatModel}, image model: {ImageModel}, fallback model: {FallbackModel}",
            _chatModel, _imageModel, _fallbackChatModel);
        _logger.LogInformation("Using deployments - Chat: {ChatDeployment}, Image: {ImageDeployment}",
            _chatDeployment, _imageDeployment);
        _logger.LogInformation("Using endpoints - Chat: {ChatEndpoint}, Image: {ImageEndpoint}",
            _endpoint, _imageEndpoint);
    }

    /// <summary>
    /// Enhances a basic image description to be more detailed using OpenAI
    /// </summary>
    public async Task<(string EnhancedDescription, int TokensUsed, long ProcessingTimeMs)> EnhanceDescriptionAsync(
        string basicDescription, List<string> tags, int targetLength)
    {
        // Validate inputs
        if (basicDescription == null)
            throw new ArgumentNullException(nameof(basicDescription));
        if (tags == null)
            throw new ArgumentNullException(nameof(tags));
        if (targetLength <= 0)
            throw new ArgumentException("Target length must be greater than 0", nameof(targetLength));

        _logger.LogInformation("Enhancing description with OpenAI. Target length: {TargetLength} words", targetLength);

        Log.Information("=== STATE CHANGE: OpenAI Description Enhancement Started ===");
        Log.Information("Input description length: {Length} characters", basicDescription.Length);
        Log.Information("Target length: {TargetLength} words", targetLength);
        Log.Information("Tags count: {TagCount}", tags.Count);

        var startTime = DateTime.UtcNow;
        var attemptedModels = new List<string>();
        try
        {            // Create the Azure OpenAI client
            var client = new AzureOpenAIClient(new Uri(_endpoint), new Azure.AzureKeyCredential(_apiKey));
            var chatClient = client.GetChatClient(_chatDeployment);

            // Build the prompt
            var prompt = $@"I have an image with the following basic description:
""{basicDescription}""

The image has been tagged with these elements: {string.Join(", ", tags)}

Please enhance this description to be more detailed and comprehensive, focusing on the visual elements present.
The enhanced description should be approximately {targetLength} words long and suitable for image generation with DALL-E.
The description should be factual based on the information provided and not add fictional elements.

Enhanced description:";

            // Configure the chat completion options
            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(@"You are an expert image description enhancer. Your task is to take basic image descriptions and tags and expand them into detailed, vivid descriptions suitable for image generation."),
                new UserChatMessage(prompt)
            };

            var chatOptions = new ChatCompletionOptions
            {
                MaxOutputTokenCount = 800,
                Temperature = 0.7f,
                TopP = 0.95f
            };

            attemptedModels.Add(_chatModel);
            ChatCompletion response;

            try
            {
                // Try with primary model
                response = await chatClient.CompleteChatAsync(messages, chatOptions);
            }
            catch (Exception ex) when (ex.Message.Contains("unavailable") && _chatDeployment != _fallbackChatModel)
            {
                // If primary model unavailable, try fallback (using same deployment for now)
                _logger.LogWarning("Primary model {PrimaryModel} unavailable. Trying fallback model {FallbackModel}",
                    _chatDeployment, _fallbackChatModel);

                var fallbackChatClient = client.GetChatClient(_chatDeployment); // Use same deployment for fallback
                attemptedModels.Add(_fallbackChatModel);

                response = await fallbackChatClient.CompleteChatAsync(messages, chatOptions);

                _logger.LogInformation("Successfully used fallback model {FallbackModel}", _fallbackChatModel);
            }

            var enhancedDescription = response.Content[0].Text.Trim();
            var tokensUsed = response.Usage.TotalTokenCount;

            var processingTime = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;

            _logger.LogInformation("Description enhancement completed in {ProcessingTime}ms, tokens used: {TokensUsed}",
                processingTime, tokensUsed);

            Log.Information("=== STATE CHANGE: OpenAI Description Enhancement Completed ===");
            Log.Information("Processing time: {ProcessingTime}ms", processingTime);
            Log.Information("Tokens used: {TokensUsed}", tokensUsed);
            Log.Information("Output description length: {Length} characters", enhancedDescription.Length);
            Log.Information("Models attempted: {Models}", string.Join(", ", attemptedModels));

            // Track metrics in Application Insights
            _telemetryClient.TrackMetric("OpenAIEnhancementTime", processingTime);
            _telemetryClient.TrackMetric("OpenAIEnhancementTokens", tokensUsed);

            // Track model name as a custom property
            var enhancementTelemetry = new Microsoft.ApplicationInsights.DataContracts.TraceTelemetry("OpenAI Description Enhancement Completed");
            enhancementTelemetry.Properties["Model"] = _chatModel;
            _telemetryClient.TrackTrace(enhancementTelemetry);

            return (enhancedDescription, tokensUsed, processingTime);
        }
        catch (Exception ex)
        {
            var processingTime = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
            _logger.LogError(ex, "Error enhancing description with OpenAI after {ProcessingTime}ms. Attempted models: {Models}",
                processingTime, string.Join(", ", attemptedModels));

            _telemetryClient.TrackException(ex, new Dictionary<string, string>
            {
                { "Service", "OpenAIEnhancement" },
                { "ProcessingTime", processingTime.ToString() },
                { "AttemptedModels", string.Join(", ", attemptedModels) }
            });

            throw; // Rethrow to be handled by the controller
        }
    }

    /// <summary>
    /// Generates a detailed image description directly from tags and analysis
    /// </summary>
    public async Task<(string DetailedDescription, int TokensUsed, long ProcessingTimeMs)> GenerateDetailedDescriptionAsync(
        List<string> tags, int targetLength, double confidenceScore = 0)
    {
        // Validate inputs
        if (tags == null)
            throw new ArgumentNullException(nameof(tags));
        if (targetLength <= 0)
            throw new ArgumentException("Target length must be greater than 0", nameof(targetLength));

        _logger.LogInformation("Generating detailed description with OpenAI. Target length: {Length} words", targetLength);
        var startTime = DateTime.UtcNow;
        var attemptedModels = new List<string>();
        try
        {
            // Create the Azure OpenAI client
            var client = new AzureOpenAIClient(new Uri(_endpoint), new Azure.AzureKeyCredential(_apiKey));
            var chatClient = client.GetChatClient(_chatDeployment);

            // Build the prompt for detailed description generation
            var prompt = $@"Create a detailed visual description of an image based on the detected elements.

**DETECTED ELEMENTS**: {string.Join(", ", tags)}

**WORD COUNT REQUIREMENT**: You MUST write EXACTLY {targetLength} words. Count carefully. This is mandatory.

**STRUCTURE YOUR DESCRIPTION**:
- Main subjects: Who/what is in the image? Describe appearance, poses, expressions, clothing
- Setting & background: Where is this? Describe the environment, location, surroundings
- Colors & lighting: What colors dominate? How is the lighting? Time of day? Mood?
- Composition: How are elements arranged? Foreground/background relationship?
- Textures & details: Materials, surfaces, patterns, fine details visible
- Atmosphere: Overall feeling, mood, aesthetic quality

Write a SINGLE flowing paragraph. Be descriptive and specific. Use vivid language. The description will be used for DALL-E image generation.

REMINDER: Your response must be EXACTLY {targetLength} words long. Count before submitting.

Description:";

            // Configure the chat completion options
            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(@"You are a professional visual description writer. Your task is to create detailed image descriptions with EXACT word counts.

CRITICAL RULES:
1. You MUST match the exact word count specified in the user request
2. Count every word before responding - accuracy is mandatory
3. Write one flowing paragraph in descriptive narrative style
4. Include visual details: subjects, setting, colors, lighting, composition, textures, atmosphere
5. Be specific and vivid - this description will be used for AI image generation
6. Use sensory language that paints a complete picture

The word count requirement is NON-NEGOTIABLE. If asked for 350 words, you must write exactly 350 words."),
                new UserChatMessage(prompt)
            };

            var chatOptions = new ChatCompletionOptions
            {
                MaxOutputTokenCount = Math.Max(1500, (int)(targetLength * 2.5)), // Allow enough tokens for target word count
                Temperature = 0.7f,
                TopP = 0.95f
            };

            attemptedModels.Add(_chatDeployment);
            ChatCompletion response;

            try
            {
                // Try with primary model
                response = await chatClient.CompleteChatAsync(messages, chatOptions);
            }
            catch (Exception ex) when (ex.Message.Contains("unavailable") && _chatDeployment != _fallbackChatModel)
            {
                // If primary model unavailable, try fallback (using same deployment for now)
                _logger.LogWarning("Primary deployment {PrimaryDeployment} unavailable. Trying fallback model {FallbackModel}",
                    _chatDeployment, _fallbackChatModel);

                var fallbackChatClient = client.GetChatClient(_chatDeployment); // Use same deployment for fallback
                attemptedModels.Add(_fallbackChatModel);

                response = await fallbackChatClient.CompleteChatAsync(messages, chatOptions);

                _logger.LogInformation("Successfully used fallback model {FallbackModel}", _fallbackChatModel);
            }

            var detailedDescription = response.Content[0].Text.Trim();
            var tokensUsed = response.Usage.TotalTokenCount;
            var processingTime = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;

            Log.Information("=== STATE CHANGE: OpenAI Detailed Description Generation Completed ===");
            Log.Information("Processing time: {ProcessingTime}ms", processingTime);
            Log.Information("Tokens used: {TokensUsed}", tokensUsed);
            Log.Information("Output description length: {Length} characters", detailedDescription.Length);
            Log.Information("Models attempted: {Models}", string.Join(", ", attemptedModels));

            // Track metrics in Application Insights
            _telemetryClient.TrackMetric("OpenAIDetailedDescriptionTime", processingTime);
            _telemetryClient.TrackMetric("OpenAIDetailedDescriptionTokens", tokensUsed);

            // Track model name as a custom property
            var descriptionTelemetry = new Microsoft.ApplicationInsights.DataContracts.TraceTelemetry("OpenAI Detailed Description Generation Completed");
            descriptionTelemetry.Properties["Model"] = _chatModel;
            _telemetryClient.TrackTrace(descriptionTelemetry);

            return (detailedDescription, tokensUsed, processingTime);
        }
        catch (Exception ex)
        {
            var processingTime = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
            _logger.LogError(ex, "Error generating detailed description with OpenAI after {ProcessingTime}ms. Attempted models: {Models}",
                processingTime, string.Join(", ", attemptedModels));

            _telemetryClient.TrackException(ex, new Dictionary<string, string>
            {
                { "Service", "OpenAIDetailedDescription" },
                { "ProcessingTime", processingTime.ToString() },
                { "AttemptedModels", string.Join(", ", attemptedModels) }
            });

            throw; // Rethrow to be handled by the controller
        }
    }

    /// <summary>
    /// Generates a new image based on a description using DALL-E
    /// </summary>
    public async Task<(byte[] ImageData, string ContentType, int TokensUsed, long ProcessingTimeMs)> GenerateImageAsync(
        string description)
    {
        // Validate inputs
        if (description == null)
            throw new ArgumentNullException(nameof(description));
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Description cannot be empty or whitespace", nameof(description));

        _logger.LogInformation("Generating image with DALL-E based on description");
        var startTime = DateTime.UtcNow;
        try
        {            // Create the Azure OpenAI client using the image-specific endpoint
            var client = new AzureOpenAIClient(new Uri(_imageEndpoint), new Azure.AzureKeyCredential(_imageApiKey));
            var imageClient = client.GetImageClient(_imageDeployment);

            // Configure the image generation options
            var imageGenerationOptions = new ImageGenerationOptions
            {
                Size = GeneratedImageSize.W1024xH1024,
                Quality = GeneratedImageQuality.Standard,
                Style = GeneratedImageStyle.Natural,
                ResponseFormat = GeneratedImageFormat.Bytes
            };

            // Generate the image
            var response = await imageClient.GenerateImageAsync(description, imageGenerationOptions);
            var generatedImage = response.Value;

            // Get binary data from the image
            byte[] imageData = generatedImage.ImageBytes.ToArray();
            var contentType = "image/png"; // DALL-E generates PNG images

            // Estimate token usage based on prompt length
            var estimatedTokens = description.Length / 4; // Very rough estimate

            var processingTime = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;

            _logger.LogInformation("Image generation completed in {ProcessingTime}ms, estimated tokens: {Tokens}, model: {Model}",
                processingTime, estimatedTokens, _imageModel);

            // Track metrics in Application Insights
            _telemetryClient.TrackMetric("DALLEGenerationTime", processingTime);
            _telemetryClient.TrackMetric("DALLEEstimatedTokens", estimatedTokens);

            // Track model name as a custom property
            var imageTelemetry = new Microsoft.ApplicationInsights.DataContracts.TraceTelemetry("DALL-E Image Generation Completed");
            imageTelemetry.Properties["Model"] = _imageModel;
            _telemetryClient.TrackTrace(imageTelemetry);

            return (imageData, contentType, estimatedTokens, processingTime);
        }
        catch (Exception ex)
        {
            var processingTime = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
            _logger.LogError(ex, "Error generating image with DALL-E after {ProcessingTime}ms", processingTime);

            _telemetryClient.TrackException(ex, new Dictionary<string, string>
            {
                { "Service", "OpenAIImageGeneration" },
                { "ProcessingTime", processingTime.ToString() },
                { "Model", _imageModel }
            });

            throw new InvalidOperationException("The image generation service is temporarily unavailable. Please try again later.", ex);
        }
    }

    /// <summary>
    /// Generates a funny meme caption based on image tags and context
    /// </summary>
    public async Task<(string TopText, string BottomText, int TokensUsed, long ProcessingTimeMs)> GenerateMemeCaptionAsync(
        List<string> tags, double confidenceScore)
    {
        // Validate inputs
        if (tags == null || tags.Count == 0)
            throw new ArgumentException("Tags list cannot be null or empty", nameof(tags));

        _logger.LogInformation("Generating meme caption based on {TagCount} tags", tags.Count);

        Log.Information("=== STATE CHANGE: OpenAI Meme Caption Generation Started ===");
        Log.Information("Tags count: {TagCount}", tags.Count);
        Log.Information("Confidence: {Confidence}", confidenceScore);

        var startTime = DateTime.UtcNow;
        var attemptedModels = new List<string>();
        
        try
        {
            // Create the Azure OpenAI client
            var client = new AzureOpenAIClient(new Uri(_endpoint), new Azure.AzureKeyCredential(_apiKey));
            var chatClient = client.GetChatClient(_chatDeployment);

            // Build the prompt for meme caption generation
            var prompt = $@"You are a hilarious meme creator. Based on the following image analysis, create a funny meme caption.

Image Analysis:
- Detected elements: {string.Join(", ", tags)}
- Analysis confidence: {confidenceScore:F2}

Create a classic two-line meme caption that is clever, witty, and funny. The caption should:
1. Be related to the detected elements in a humorous way
2. Follow internet meme culture and humor style
3. Be concise and punchy (each line should be 2-8 words)
4. Use classic meme formats when appropriate (surprised, fail, success, etc.)
5. Be appropriate but edgy humor

Return ONLY two lines in this exact format:
TOP: [your top text here]
BOTTOM: [your bottom text here]

Example format:
TOP: WHEN YOU SEE YOUR CRUSH
BOTTOM: BUT FORGET HOW TO SPEAK

Your meme caption:";

            // Configure chat completion with lower temperature for consistent humor
            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(@"You are an expert meme creator who understands internet humor, pop culture, and classic meme formats. Create funny, clever captions that would go viral. Keep it witty but appropriate."),
                new UserChatMessage(prompt)
            };

            var chatOptions = new ChatCompletionOptions
            {
                MaxOutputTokenCount = 100,
                Temperature = 0.5f, // Lower temperature for more consistent, focused humor
                TopP = 0.9f
            };

            attemptedModels.Add(_chatDeployment);
            ChatCompletion response;

            try
            {
                // Try with primary model
                response = await chatClient.CompleteChatAsync(messages, chatOptions);
            }
            catch (Exception ex) when (ex.Message.Contains("unavailable") && _chatDeployment != _fallbackChatModel)
            {
                _logger.LogWarning("Primary deployment {PrimaryDeployment} unavailable. Trying fallback model {FallbackModel}",
                    _chatDeployment, _fallbackChatModel);

                var fallbackChatClient = client.GetChatClient(_chatDeployment);
                attemptedModels.Add(_fallbackChatModel);

                response = await fallbackChatClient.CompleteChatAsync(messages, chatOptions);

                _logger.LogInformation("Successfully used fallback model {FallbackModel}", _fallbackChatModel);
            }

            var captionText = response.Content[0].Text.Trim();
            var tokensUsed = response.Usage.TotalTokenCount;
            var processingTime = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;

            // Parse the response to extract top and bottom text
            string topText = "";
            string bottomText = "";

            var lines = captionText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (line.StartsWith("TOP:", StringComparison.OrdinalIgnoreCase))
                {
                    topText = line.Substring(4).Trim();
                }
                else if (line.StartsWith("BOTTOM:", StringComparison.OrdinalIgnoreCase))
                {
                    bottomText = line.Substring(7).Trim();
                }
            }

            // Fallback if parsing failed
            if (string.IsNullOrWhiteSpace(topText) && string.IsNullOrWhiteSpace(bottomText))
            {
                _logger.LogWarning("Failed to parse meme caption format. Using raw response.");
                var parts = captionText.Split('\n', 2, StringSplitOptions.RemoveEmptyEntries);
                topText = parts.Length > 0 ? parts[0] : "MEME";
                bottomText = parts.Length > 1 ? parts[1] : "GENERATION";
            }

            Log.Information("=== STATE CHANGE: OpenAI Meme Caption Generation Completed ===");
            Log.Information("Processing time: {ProcessingTime}ms", processingTime);
            Log.Information("Tokens used: {TokensUsed}", tokensUsed);
            Log.Information("Top text: {TopText}", topText);
            Log.Information("Bottom text: {BottomText}", bottomText);

            // Track metrics in Application Insights
            _telemetryClient.TrackMetric("OpenAIMemeCaptionTime", processingTime);
            _telemetryClient.TrackMetric("OpenAIMemeCaptionTokens", tokensUsed);

            var memeTelemetry = new Microsoft.ApplicationInsights.DataContracts.TraceTelemetry("OpenAI Meme Caption Generation Completed");
            memeTelemetry.Properties["Model"] = _chatModel;
            memeTelemetry.Properties["TopText"] = topText;
            memeTelemetry.Properties["BottomText"] = bottomText;
            _telemetryClient.TrackTrace(memeTelemetry);

            return (topText, bottomText, tokensUsed, processingTime);
        }
        catch (Exception ex)
        {
            var processingTime = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
            _logger.LogError(ex, "Error generating meme caption with OpenAI after {ProcessingTime}ms. Attempted models: {Models}",
                processingTime, string.Join(", ", attemptedModels));

            _telemetryClient.TrackException(ex, new Dictionary<string, string>
            {
                { "Service", "OpenAIMemeCaption" },
                { "ProcessingTime", processingTime.ToString() },
                { "AttemptedModels", string.Join(", ", attemptedModels) }
            });

            throw;
        }
    }
}
