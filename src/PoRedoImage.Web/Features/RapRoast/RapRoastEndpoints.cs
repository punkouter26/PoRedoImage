using System.Text.Json;
using PoRedoImage.Web.Features.RapRoast;
using PoRedoImage.Shared.DTOs;
using PoRedoImage.Shared.Imaging;
using PoRedoImage.Shared.Json;
using PoRedoImage.Web.Features.Shared;

namespace PoRedoImage.Web.Features.RapRoast;

/// <summary>
/// Minimal API endpoint for the Rap Roast slice.
/// POST /api/rap-roast → photo in, roast bars + a performed track out.
/// </summary>
/// <remarks>
/// Rate-limited with the same <c>ai-endpoints</c> policy as image analysis: a single request runs a
/// vision call, up to two chat calls, and up to two music generations, so it is among the most
/// expensive endpoints in the app.
/// </remarks>
public static class RapRoastEndpoints
{
    public static IEndpointRouteBuilder MapRapRoastEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/rap-roast")
            .WithTags("RapRoast")
            .AllowAnonymous()
            .RequireRateLimiting("ai-endpoints");

        group.MapPost("/", async (
            RapRoastRequest request,
            HttpContext http,
            RapRoastOrchestrator orchestrator,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("RapRoastEndpoints");

            if (string.IsNullOrWhiteSpace(request.ImageData))
                return Results.Problem(
                    detail: "ImageData is required.", statusCode: 400, title: "Validation Error");

            try
            {
                // Validates magic bytes and size before anything expensive runs.
                _ = ImageBytes.FromBase64(request.ImageData, request.ContentType);
            }
            catch (ImageValidationException ex)
            {
                logger.LogWarning(ex, "Rap roast image validation failed: {Message}", ex.Message);
                return Results.Problem(detail: ex.Message, statusCode: 400, title: "Invalid Image");
            }

            // A client that asks for NDJSON gets the bars the moment they are written, then the
            // finished response — the music step takes 30–90s and there is no reason to hide the
            // lyrics for all of it. Partial lines have TotalMs == 0; the last line is the result.
            // Anyone else (the mobile app) gets the single JSON body it always did.
            var streaming = http.Request.Headers.Accept.ToString()
                .Contains("application/x-ndjson", StringComparison.OrdinalIgnoreCase);

            async Task WriteLineAsync(RapRoastResponse r)
            {
                if (!http.Response.HasStarted)
                {
                    http.Response.ContentType = "application/x-ndjson";
                    http.Response.Headers["Cache-Control"] = "no-cache, no-store";
                    http.Response.Headers["X-Accel-Buffering"] = "no";
                }
                await JsonSerializer.SerializeAsync(http.Response.Body, r, SharedJsonContext.Default.RapRoastResponse, ct);
                await http.Response.Body.WriteAsync("\n"u8.ToArray(), ct);
                await http.Response.Body.FlushAsync(ct);
            }

            try
            {
                var result = await orchestrator.ProcessAsync(request, streaming ? WriteLineAsync : null, ct);

                logger.LogInformation(
                    "Rap roast complete in {Elapsed}ms. AudioRefused={Refused}, Softened={Softened}",
                    result.TotalMs, result.AudioRefused, result.LyricsSoftened);

                // A refusal is a 200: the lyrics are a legitimate result and the DTO carries the
                // AudioRefused flag for the client to render its explanatory state.
                if (!streaming) return Results.Ok(result);
                await WriteLineAsync(result);
                return Results.Empty;
            }
            catch (Exception ex) when (http.Response.HasStarted)
            {
                // Status is already 200 and the bars are on screen; ending the stream without a
                // final line is how the client learns the track never arrived.
                logger.LogError(ex, "Rap roast pipeline failed after the lyrics were streamed");
                return Results.Empty;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Rap roast pipeline failed");
                return Results.Problem(
                    detail: "The rap roast pipeline failed. Please try again.",
                    statusCode: 500, title: "Roast Failed");
            }
        })
        .WithName("CreateRapRoast")
        .WithSummary("Turn a photo into a roast rap track")
        .Produces<RapRoastResponse>(StatusCodes.Status200OK)
        .Produces<ProblemDetailsDto>(StatusCodes.Status400BadRequest)
        .Produces<ProblemDetailsDto>(StatusCodes.Status500InternalServerError)
        .AddEndpointFilter<ValidationFilter<RapRoastRequest>>();

        return app;
    }
}
