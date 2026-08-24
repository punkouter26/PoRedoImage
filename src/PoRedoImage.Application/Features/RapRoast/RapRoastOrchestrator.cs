using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PoRedoImage.Domain.Interfaces;
using PoRedoImage.Shared.Configuration;
using PoRedoImage.Shared.DTOs;

namespace PoRedoImage.Application.Features.RapRoast;

/// <summary>
/// Orchestrates photo → description → roast lyrics → performed track.
/// </summary>
public interface IRapRoastOrchestrator
{
    Task<RapRoastResponse> ProcessAsync(RapRoastRequest request, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class RapRoastOrchestrator(
    IVisionServiceRouter visionRouter,
    SceneDescriber sceneDescriber,
    RoastLyricsWriter lyricsWriter,
    IMusicGenerationService musicService,
    ISceneDetailProvider sceneDetails,
    ILogger<RapRoastOrchestrator> logger) : IRapRoastOrchestrator
{
    /// <summary>
    /// Hard cap on calls to the music provider per request. The refusal path retries exactly once
    /// with softened lyrics; without a cap a stubborn safety filter would bill on a loop.
    /// </summary>
    internal const int MaxMusicAttempts = 2;

    public async Task<RapRoastResponse> ProcessAsync(RapRoastRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var total = Stopwatch.StartNew();
        var imageBytes = Convert.FromBase64String(request.ImageData);

        // Steps 1 + 2 — look at the photo, once.
        //
        // This used to be two Computer Vision calls with identical bytes: AnalyzeAsync for
        // Caption|Tags here, then GetDetailsAsync for Read|Objects|People|DenseCaptions inside the
        // scene describer. Azure CV takes every one of those features in a single request, so the
        // second call bought a round-trip and a charge and nothing else.
        //
        // When the backend cannot combine them — Ollama and the browser-local models have no OCR or
        // dense captions — the two calls still happen, but concurrently rather than one after the
        // other. Neither depends on the other's output.
        string baseDescription;
        IReadOnlyList<string> tags;
        SceneDetails? details = null;

        var vision = visionRouter.Resolve(request.ModelId);

        // A caller that explicitly chose Ollama gets Ollama, even though combining would be
        // cheaper — honouring the model selection matters more than saving a call.
        if (!AiProviderIds.IsOllama(request.ModelId)
            && sceneDetails is ICombinedVisionAnalyzer { SupportsCombinedAnalysis: true } combined)
        {
            try
            {
                var all = await combined.AnalyzeAllAsync(imageBytes, ct);
                baseDescription = all.Description;
                tags = all.Tags;
                details = all.Details;
                logger.LogInformation(
                    "Combined vision analysis in {Elapsed}ms — one call instead of two.", all.ElapsedMs);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The combined call is an OPTIMISATION over two independent services, so its failure
                // must cost what it saved and nothing more.
                //
                // This is not hypothetical — it is what the integration suite caught. Detail
                // extraction has always swallowed its own errors (losing OCR degrades the bars, it
                // does not fail the request), and folding the primary vision call into it quietly
                // moved that call behind a component whose credentials can be broken independently.
                // In the Test environment the vision service is a working mock while the Computer
                // Vision client holds a fake key, and the whole roast started 500ing.
                logger.LogWarning(ex,
                    "Combined vision analysis failed; falling back to separate vision + detail calls.");
                (baseDescription, tags, details) = await SeparateAnalysisAsync(vision, imageBytes, ct);
            }
        }
        else
        {
            (baseDescription, tags, details) = await SeparateAnalysisAsync(vision, imageBytes, ct);
        }

        // Step 3 — a genuinely detailed scene description. The vision backend's own description is
        // often just its top tags joined together (Azure's Caption feature is region-limited), and
        // keywords produce generic bars. A vision-capable chat model gives the specifics a roast
        // needs; without one this returns the tag-derived text unchanged. The detail is handed in
        // rather than re-fetched — that is the whole point of the combined call above.
        var scene = await sceneDescriber.DescribeAsync(imageBytes, baseDescription, tags, details, ct);
        var description = scene.Text;

        var response = new RapRoastResponse
        {
            ImageDescription = description,
            DescriptionIsDetailed = scene.Detailed,
            DescriptionFallbackReason = scene.FallbackReason,
            Scene = scene.Detailed ? Map(scene.Snapshot) : null,
        };

        // Steps 3 + 4 — write bars, then have them performed. A refusal from the music provider is
        // an expected outcome for a roast, so it drives one softened rewrite rather than an error.
        RoastLyrics? lyrics = null;
        MusicGenerationResult? music = null;

        for (var attempt = 1; attempt <= MaxMusicAttempts; attempt++)
        {
            var softened = attempt > 1;
            lyrics = await lyricsWriter.WriteAsync(
                description, tags, request.Style, request.Intensity, softened, ct);

            if (!musicService.IsConfigured)
            {
                // No music provider configured (a normal local-dev state): return the bars alone
                // rather than failing the whole request.
                logger.LogInformation("Music provider not configured — returning lyrics only.");
                return Finish(response, lyrics, music: null, total,
                    refusalReason: "Music generation is not configured on this environment.");
            }

            music = await musicService.GenerateAsync(lyrics.Text, StylePrompt(request.Style), ct);

            if (!music.Refused)
            {
                logger.LogInformation(
                    "Roast track generated on attempt {Attempt}. Softened={Softened}", attempt, softened);
                return Finish(response, lyrics, music, total, refusalReason: null);
            }

            logger.LogInformation(
                "Music provider refused on attempt {Attempt} of {Max}. Reason={Reason}",
                attempt, MaxMusicAttempts, music.RefusalReason);
        }

        // Exhausted the attempts — the user still gets the lyrics.
        logger.LogInformation("Music provider refused every attempt; returning lyrics only.");
        return Finish(response, lyrics!, music: null, total, music?.RefusalReason
            ?? "The music provider declined to perform these lyrics.");
    }

    /// <summary>
    /// The two-call path: vision and scene detail run CONCURRENTLY, because neither reads the
    /// other's output. Used when the backend cannot combine them, and as the fallback when the
    /// combined call fails.
    /// </summary>
    private async Task<(string Description, IReadOnlyList<string> Tags, SceneDetails Details)>
        SeparateAnalysisAsync(IVisionService vision, byte[] imageBytes, CancellationToken ct)
    {
        var analyzeTask = vision.AnalyzeAsync(imageBytes, ct);
        var detailTask = SafeDetailsAsync(imageBytes, ct);
        await Task.WhenAll(analyzeTask, detailTask);

        var (description, tags, _, _) = await analyzeTask;
        return (description, tags, await detailTask);
    }

    /// <summary>
    /// Scene detail is an enhancement: losing it must degrade the bars, never fail the request.
    /// </summary>
    private async Task<SceneDetails> SafeDetailsAsync(byte[] image, CancellationToken ct)
    {
        try
        {
            return await sceneDetails.GetDetailsAsync(image, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Scene detail extraction failed; continuing without it.");
            return SceneDetails.Empty;
        }
    }

    private static RapRoastResponse Finish(
        RapRoastResponse response,
        RoastLyrics lyrics,
        MusicGenerationResult? music,
        Stopwatch total,
        string? refusalReason)
    {
        response.Lyrics = lyrics.Text;
        response.LyricsSoftened = lyrics.Softened;

        if (music is not null)
        {
            response.AudioData = Convert.ToBase64String(music.Audio);
            response.AudioContentType = music.ContentType;
            response.AudioRefused = false;
        }
        else
        {
            response.AudioRefused = true;
            response.RefusalReason = refusalReason;
        }

        total.Stop();
        response.TotalMs = total.ElapsedMilliseconds;
        return response;
    }

    /// <summary>Maps the Application snapshot onto the wire DTO (Shared cannot see Application).</summary>
    private static SceneSnapshotDto Map(SceneSnapshot s) => new()
    {
        Outfit = s.Outfit,
        Pose = s.Pose,
        Expression = s.Expression,
        Setting = s.Setting,
        Props = s.Props,
        TextInImage = s.TextInImage,
        MostIncongruousDetail = s.MostIncongruousDetail,
    };

    /// <summary>Musical direction handed to the music model alongside the lyrics.</summary>
    private static string StylePrompt(RapStyle style) => style switch
    {
        RapStyle.Trap =>
            "A modern trap rap track. Booming 808 sub-bass, rapid hi-hat rolls, half-time feel around "
            + "140 BPM. Confident male rap vocal, clear diction, punchy delivery.",
        RapStyle.OldSchool =>
            "An old-school 1980s party rap track. Live funk drum break, horn stabs, electric bass, "
            + "around 105 BPM. Energetic playful rap vocal with crowd energy.",
        _ =>
            "A 90s boom-bap hip-hop track. Dusty sampled drums, warm upright bass, vinyl crackle, "
            + "around 90 BPM. Laid-back confident rap vocal with clear diction.",
    };
}
