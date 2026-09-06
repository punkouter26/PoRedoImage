using PoRedoImage.Domain.Interfaces;
using PoRedoImage.Shared.DTOs;
using PoRedoImage.Shared.Imaging;
using PoRedoImage.Web.Features.Shared;

namespace PoRedoImage.Web.Features.VideoGenerate;

/// <summary>
/// Image-to-video slice. <c>POST /api/video/generate</c> starts a Veo job and returns its handle;
/// <c>GET /api/video/status</c> polls that handle and returns the clip once it is ready.
/// </summary>
/// <remarks>
/// Two endpoints rather than one blocking call because Veo takes 1–5 minutes and Azure App Service
/// drops an idle HTTP request at ~230 seconds — a single long request would pass locally and fail
/// in production. Google holds the job state, so nothing is persisted here.
/// </remarks>
public static class VideoGenerateEndpoints
{
    public static IEndpointRouteBuilder MapVideoGenerateEndpoints(this IEndpointRouteBuilder app)
    {
        // Antiforgery at the GROUP level so a later POST added to this slice is protected by
        // default rather than by memory. The status GET is a read, but it lives in the same group
        // and the filter only validates state-changing verbs.
        var group = app.MapGroup("/api/video")
            .WithTags("VideoGenerate")
            .RequireAuthorization()
            .RequireAntiforgeryValidation();

        group.MapPost("/generate", async (
            VideoGenerateRequest request,
            IVideoGenerationService video,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("VideoGenerateEndpoints");

            if (!video.IsConfigured)
            {
                return Results.Problem(
                    "Video generation is not configured on this server (Google:ApiKey is missing).",
                    statusCode: 503, title: "Video Unavailable");
            }

            if (string.IsNullOrWhiteSpace(request.Prompt))
                return Results.BadRequest("Prompt is required.");

            ImageBytes imageBytes;
            try { imageBytes = ImageBytes.FromBase64(request.ImageData, request.ContentType); }
            catch (ImageValidationException ex) { return Results.BadRequest(ex.Message); }

            try
            {
                var operationName = await video.StartAsync(
                    imageBytes.Bytes.ToArray(),
                    request.ContentType,
                    request.Prompt,
                    ct);

                logger.LogInformation("Video job started. Operation={Operation}", operationName);

                return Results.Ok(new VideoGenerateStartResponse(operationName));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to start video generation");
                return Results.Problem(
                    "Could not start video generation.", statusCode: 502, title: "Video Service Error");
            }
        })
        .WithName("StartVideoGeneration")
        .WithSummary("Start an image-to-video render and return its operation handle");

        group.MapGet("/status", async (
            string op,
            IVideoGenerationService video,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("VideoGenerateEndpoints");

            if (string.IsNullOrWhiteSpace(op))
                return Results.BadRequest("Operation handle 'op' is required.");

            // The handle comes from the client, so it reaches the provider URL as a path segment.
            // Constrain it to the shape Google issues (operations/... or models/.../operations/...)
            // so a crafted value cannot walk the path onto a different endpoint.
            if (op.Contains("..", StringComparison.Ordinal)
                || op.StartsWith('/')
                || !op.Contains("operations/", StringComparison.Ordinal))
            {
                return Results.BadRequest("Malformed operation handle.");
            }

            try
            {
                var status = await video.PollAsync(op, ct);

                if (!status.Done)
                    return Results.Ok(new VideoGenerateStatusResponse(Done: false));

                if (status.Video is null || status.Video.Length == 0)
                {
                    return Results.Ok(new VideoGenerateStatusResponse(
                        Done: true,
                        ErrorMessage: status.ErrorMessage ?? "The video service returned no clip."));
                }

                logger.LogInformation("Video job {Operation} delivered {Bytes} bytes", op, status.Video.Length);
                return Results.Ok(new VideoGenerateStatusResponse(
                    Done: true,
                    VideoData: Convert.ToBase64String(status.Video),
                    VideoContentType: status.ContentType ?? "video/mp4"));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to poll video generation {Operation}", op);
                return Results.Problem(
                    "Could not check video status.", statusCode: 502, title: "Video Service Error");
            }
        })
        .WithName("GetVideoGenerationStatus")
        .WithSummary("Poll a video render started by POST /api/video/generate");

        return app;
    }
}
