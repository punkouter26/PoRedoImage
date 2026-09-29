using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PoRedoImage.Infrastructure.Services;
using Xunit;

namespace PoRedoImage.Tests.Unit.Services;

/// <summary>
/// Veo exposes no audio parameter, so the prompt is the only lever — and with no audio direction it
/// commonly returns a silent clip while still billing the "with audio" rate. These cases pin the two
/// halves of the rule: add the directive when the caller said nothing about sound, and never touch a
/// prompt that already took a position — including one that asked for silence.
/// </summary>
public sealed class VeoAudioDirectionTests
{
    [Theory]
    // Nothing about sound → the directive is appended.
    [InlineData("a man reads a menu", true)]
    [InlineData("slow dolly across the table", true)]
    [InlineData("", true)]
    // Already asks for sound → left exactly as written.
    [InlineData("he laughs, cutlery clinking", false)]
    [InlineData("add upbeat music", false)]
    [InlineData("a narrator describes the scene", false)]
    [InlineData("she speaks to camera", false)]
    // Explicitly asks for NO sound → must not be contradicted two sentences later.
    [InlineData("a silent film pastiche", false)]
    [InlineData("keep it quiet, no sound", false)]
    public void Audio_directive_is_added_only_when_the_prompt_is_silent_on_sound(
        string prompt, bool expectDirective)
    {
        var result = VeoVideoGenerationService.WithAudioDirection(prompt);

        Assert.Equal(expectDirective, result.EndsWith(VeoVideoGenerationService.AudioDirective, StringComparison.Ordinal));

        // The caller's own words survive verbatim either way — the directive only ever appends.
        Assert.StartsWith(prompt.TrimEnd(), result, StringComparison.Ordinal);
    }

    /// <summary>
    /// Regression: 066dac7 removed the per-request <c>x-goog-api-key</c> header and every render
    /// came back "Method doesn't allow unregistered callers". Start AND poll must both carry it.
    /// </summary>
    [Fact]
    public async Task Every_Veo_request_carries_the_api_key()
    {
        var seen = new List<string?>();
        var handler = new StubHandler(req =>
        {
            seen.Add(req.Headers.TryGetValues("x-goog-api-key", out var v) ? v.Single() : null);
            var body = req.Method == HttpMethod.Post ? """{"name":"operations/op1"}""" : """{"done":false}""";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        });
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("Veo")).Returns(() => new HttpClient(handler));
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Google:ApiKey"] = "test-key" })
            .Build();

        var veo = new VeoVideoGenerationService(config, factory.Object, NullLogger<VeoVideoGenerationService>.Instance);
        var op = await veo.StartAsync([1, 2, 3], "image/png", "a slow zoom");
        await veo.PollAsync(op);

        Assert.Equal(["test-key", "test-key"], seen);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(respond(request));
    }
}
