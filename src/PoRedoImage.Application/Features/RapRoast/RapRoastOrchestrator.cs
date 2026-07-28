using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PoRedoImage.Domain.Interfaces;
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

        // Step 1 — vision for tags. Routed exactly as the analyze pipeline is, so the roast honours
        // whichever backend the caller selected.
        var vision = visionRouter.Resolve(request.ModelId);
        var (baseDescription, tags, _, _) = await vision.AnalyzeAsync(imageBytes, ct);

        // Step 2 — a genuinely detailed scene description. The vision backend's own description is
        // often just its top tags joined together (Azure's Caption feature is region-limited), and
        // keywords produce generic bars. A vision-capable chat model gives the specifics a roast
        // needs; without one this returns the tag-derived text unchanged.
        var scene = await sceneDescriber.DescribeAsync(imageBytes, baseDescription, tags, ct);
        var description = scene.Text;

        var response = new RapRoastResponse
        {
            ImageDescription = description,
            DescriptionIsDetailed = scene.Detailed,
            Scene = scene.Detailed ? Map(scene.Snapshot) : null,
        };

        // Steps 3 + 4 — write bars, then have them performed. A refusal from the music provider is
        // an expected outcome for a roast, so it drives one softened rewrite rather than an error.
        RoastLyrics? lyrics = null;
        MusicGenerationResult? music = null;

        for (var attempt = 1; attempt <= MaxMusicAttempts; attempt++)
        {
            var softened = attempt > 1;
            lyrics = await lyricsWriter.WriteAsync(description, tags, request.Style, softened, ct);

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
