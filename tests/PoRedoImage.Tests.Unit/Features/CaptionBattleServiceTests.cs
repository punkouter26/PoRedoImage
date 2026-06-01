using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PoRedoImage.Domain.Entities;
using PoRedoImage.Domain.Interfaces;
using PoRedoImage.Infrastructure.Services;

namespace PoRedoImage.Tests.Unit.Features;

/// <summary>
/// Unit tests for the Meme Caption Battle (Idea #5).
/// Uses a mock IGenerativeAiService so we can simulate success/failure
/// across the 8 personas without hitting the real Azure OpenAI endpoint.
/// </summary>
public class CaptionBattleServiceTests
{
    [Fact]
    public void AllPersonas_Contains8DistinctVoices()
    {
        var svc = CreateService(out _);

        Assert.Equal(8, svc.AllPersonas.Count);
        Assert.Equal(8, svc.AllPersonas.Distinct().Count());
    }

    [Fact]
    public async Task RunBattleAsync_AllSucceed_ReturnsAllCandidates()
    {
        var svc = CreateService(out var mockAi);

        var result = await svc.RunBattleAsync(["dog", "park"]);

        Assert.True(result.Succeeded == 8);
        Assert.Equal(8, result.Candidates.Count);
        Assert.All(result.Candidates, c => Assert.True(c.Succeeded));
    }

    [Fact]
    public async Task RunBattleAsync_PartialFailure_ReturnsFailedCandidates()
    {
        // Half the personas "fail" by throwing — service must capture them gracefully.
        var svc = CreateService(out var mockAi, failOnPersona: p => p == CaptionPersona.Sarcastic || p == CaptionPersona.TechBro);

        var result = await svc.RunBattleAsync(["dog", "park"]);

        Assert.Equal(8, result.Requested);
        Assert.Equal(6, result.Succeeded);
        Assert.Equal(2, result.Candidates.Count(c => !c.Succeeded));
        Assert.All(result.Candidates.Where(c => !c.Succeeded),
            c => Assert.False(string.IsNullOrEmpty(c.ErrorMessage)));
    }

    [Fact]
    public async Task RunBattleAsync_PreservesOrderRegardlessOfCompletionTime()
    {
        var svc = CreateService(out var mockAi, delayByPersona: p => p switch
        {
            CaptionPersona.GenZ => 50,
            CaptionPersona.Surreal => 5,
            _ => 0
        });

        var result = await svc.RunBattleAsync(["subject"]);

        // The order of the candidates should match the order of personas in AllPersonas,
        // not the order in which they finished. Verify by checking the persona names.
        var personas = result.Candidates.Select(c => c.Persona).ToList();
        var expected = svc.AllPersonas.ToList();
        Assert.Equal(expected, personas);
    }

    [Fact]
    public async Task RunBattleAsync_EmptyTags_StillRuns()
    {
        var svc = CreateService(out _);

        var result = await svc.RunBattleAsync([]);

        Assert.Equal(8, result.Requested);
        Assert.Equal(8, result.Succeeded);
    }

    [Fact]
    public async Task RunBattleAsync_AllFail_AllCandidatesReturnedWithErrors()
    {
        var svc = CreateService(out var mockAi, failOnPersona: _ => true);

        var result = await svc.RunBattleAsync(["subject"]);

        Assert.Equal(0, result.Succeeded);
        Assert.Equal(8, result.Requested);
        Assert.All(result.Candidates, c => Assert.False(c.Succeeded));
    }

    [Fact]
    public async Task RunBattleAsync_RestrictsToSubset()
    {
        var svc = CreateService(out _);

        var result = await svc.RunBattleAsync(["x"], [CaptionPersona.GenZ, CaptionPersona.Corporate]);

        Assert.Equal(2, result.Requested);
        Assert.Equal(2, result.Succeeded);
        Assert.All(result.Candidates, c => Assert.True(
            c.Persona == CaptionPersona.GenZ || c.Persona == CaptionPersona.Corporate));
    }

    private static CaptionBattleService CreateService(
        out Mock<IGenerativeAiService> mock,
        Func<CaptionPersona, bool>? failOnPersona = null,
        Func<CaptionPersona, int>? delayByPersona = null)
    {
        mock = new Mock<IGenerativeAiService>();
        mock.Setup(m => m.GenerateCaptionAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async (IReadOnlyList<string> tags, string system, CancellationToken ct) =>
            {
                // Find which persona is calling by inspecting the system prompt — brittle but
                // sufficient for unit tests; in production the persona is the caller's responsibility.
                var persona = CaptionPersonaExtensions_Probe(system);

                if (failOnPersona?.Invoke(persona) == true)
                    throw new InvalidOperationException($"Simulated failure for {persona}");

                if (delayByPersona is not null)
                    await Task.Delay(delayByPersona(persona), ct);

                return ($"{persona} caption for {string.Join(",", tags)}", 42, 10L);
            });
        return new CaptionBattleService(mock.Object, NullLogger<CaptionBattleService>.Instance);
    }

    /// <summary>Reverse-lookup the persona from its system prompt — test-only helper.</summary>
    private static CaptionPersona CaptionPersonaExtensions_Probe(string systemPrompt)
    {
        foreach (CaptionPersona p in Enum.GetValues<CaptionPersona>())
        {
            if (systemPrompt.StartsWith(p.SystemPrompt().Substring(0, 20), StringComparison.Ordinal))
                return p;
        }
        return CaptionPersona.GenZ;
    }
}
