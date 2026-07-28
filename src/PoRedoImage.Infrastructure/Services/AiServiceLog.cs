using Microsoft.Extensions.Logging;

namespace PoRedoImage.Infrastructure.Services;

/// <summary>
/// Source-generated logging (§5 "Zero-Allocation Logging") for the outbound AI service hot paths —
/// every image generation, vision analysis, and chat completion passes through here, so these are
/// the highest-frequency log sites in the app.
/// </summary>
/// <remarks>
/// <c>[LoggerMessage]</c> compiles each call to an allocation-free, zero-boxing log method: no
/// <c>params object[]</c>, no boxing of the <c>long</c>/<c>int</c> arguments, and no message
/// formatting at all when the level is disabled. Startup and initialization messages are
/// deliberately left as ordinary <c>ILogger</c> calls — they run once and gain nothing from
/// source generation.
/// </remarks>
internal static partial class AiServiceLog
{
    // ── Gemini ──────────────────────────────────────────────────────────
    [LoggerMessage(Level = LogLevel.Information, Message = "Calling Gemini API (img2img). Model={Model}")]
    public static partial void GeminiImg2ImgStarting(this ILogger logger, string model);

    [LoggerMessage(Level = LogLevel.Information, Message = "Gemini API img2img complete in {Elapsed}ms. Size={Size} bytes")]
    public static partial void GeminiImg2ImgComplete(this ILogger logger, long elapsed, int size);

    [LoggerMessage(Level = LogLevel.Information, Message = "Calling Gemini API (img2img re-roll). Model={Model}, Seed={Seed}")]
    public static partial void GeminiRerollStarting(this ILogger logger, string model, int seed);

    [LoggerMessage(Level = LogLevel.Information, Message = "Gemini API re-roll complete in {Elapsed}ms. Seed={Seed}, Size={Size} bytes")]
    public static partial void GeminiRerollComplete(this ILogger logger, long elapsed, int seed, int size);

    [LoggerMessage(Level = LogLevel.Information, Message = "Calling Gemini API. Model={Model}")]
    public static partial void GeminiStarting(this ILogger logger, string model);

    [LoggerMessage(Level = LogLevel.Information, Message = "Gemini API complete in {Elapsed}ms. Size={Size} bytes")]
    public static partial void GeminiComplete(this ILogger logger, long elapsed, int size);

    [LoggerMessage(Level = LogLevel.Error, Message = "Gemini API error {Status}: {Body}")]
    public static partial void GeminiError(this ILogger logger, int status, string body);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Gemini raw response: {Body}")]
    public static partial void GeminiRawResponse(this ILogger logger, string body);

    // ── HuggingFace ─────────────────────────────────────────────────────
    [LoggerMessage(Level = LogLevel.Information, Message = "Calling HuggingFace (img2img). Provider={Provider}/{Model}, Seed={Seed}")]
    public static partial void HuggingFaceImg2ImgStarting(this ILogger logger, string provider, string model, int seed);

    [LoggerMessage(Level = LogLevel.Information, Message = "Calling HuggingFace (text2img). Provider={Provider}/{Model}")]
    public static partial void HuggingFaceText2ImgStarting(this ILogger logger, string provider, string model);

    [LoggerMessage(Level = LogLevel.Error, Message = "HuggingFace API error {Status}: {Body}")]
    public static partial void HuggingFaceError(this ILogger logger, int status, string body);

    [LoggerMessage(Level = LogLevel.Information, Message = "HuggingFace API complete in {Elapsed}ms. Size={Size} bytes")]
    public static partial void HuggingFaceComplete(this ILogger logger, long elapsed, int size);

    // ── Azure Computer Vision ───────────────────────────────────────────
    [LoggerMessage(Level = LogLevel.Information, Message = "Analyzing image via Azure Computer Vision. Size={Size} bytes")]
    public static partial void VisionAnalysisStarting(this ILogger logger, int size);

    [LoggerMessage(Level = LogLevel.Information, Message = "Vision analysis complete in {Elapsed}ms (with caption)")]
    public static partial void VisionAnalysisComplete(this ILogger logger, long elapsed);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Caption not supported in this region — retrying with Tags only")]
    public static partial void VisionCaptionUnsupported(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Vision analysis complete in {Elapsed}ms (tags-only fallback)")]
    public static partial void VisionAnalysisCompleteTagsOnly(this ILogger logger, long elapsed);

    // ── Azure OpenAI ────────────────────────────────────────────────────
    [LoggerMessage(Level = LogLevel.Information, Message = "Enhancing description. TargetLength={Length}")]
    public static partial void EnhanceStarting(this ILogger logger, int length);

    [LoggerMessage(Level = LogLevel.Information, Message = "Description enhanced in {Elapsed}ms. Tokens={Tokens}")]
    public static partial void EnhanceComplete(this ILogger logger, long elapsed, int tokens);

    [LoggerMessage(Level = LogLevel.Information, Message = "Generating meme caption from {Count} tags")]
    public static partial void MemeCaptionStarting(this ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Meme caption generated in {Elapsed}ms")]
    public static partial void MemeCaptionComplete(this ILogger logger, long elapsed);

    [LoggerMessage(Level = LogLevel.Information, Message = "Describing person via GPT-4o vision. Size={Size} bytes")]
    public static partial void DescribePersonStarting(this ILogger logger, int size);

    [LoggerMessage(Level = LogLevel.Information, Message = "Person described in {Elapsed}ms: {Description}")]
    public static partial void DescribePersonComplete(this ILogger logger, long elapsed, string description);
}
