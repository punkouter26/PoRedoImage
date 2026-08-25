using System.Globalization;
using System.Text;
using Microsoft.ML.OnnxRuntimeGenAI;

// Aliased because this class exposes a Model property of its own (the catalog entry). Without the
// alias, `new Model(...)` binds to that property and fails with CS0118.
using OrtModel = Microsoft.ML.OnnxRuntimeGenAI.Model;

namespace PoRedoImage.Mobile.Services;

/// <summary>
/// Meme captions from Qwen2.5 0.5B Instruct, executed natively through ONNX Runtime GenAI.
/// </summary>
/// <remarks>
/// <para>
/// The weights are side-loaded rather than bundled: at ~800 MB they are far past any store limit,
/// and <c>EmbedAssembliesIntoApk</c> would try to pack them into the APK. <see cref="IOnDeviceModelStore"/>
/// finds them; <c>SCRIPTS/push-mobile-model.ps1</c> puts them there.
/// </para>
/// <para>
/// Everything here is serialized behind one semaphore. ONNX Runtime GenAI's <c>Model</c>,
/// <c>Tokenizer</c> and <c>Generator</c> are not thread-safe, and a second load while the first is
/// still mapping would briefly double an already large native allocation on a device that has no
/// room for it.
/// </para>
/// </remarks>
public sealed class QwenCaptionService : IOnDeviceCaptionService, IDisposable
{
    /// <summary>
    /// Room for a caption and nothing more. Qwen2.5-0.5B will happily keep writing an essay; a tight
    /// budget is what keeps generation to a few seconds on a phone CPU rather than a minute.
    /// </summary>
    private const int MaxNewTokens = 60;

    /// <summary>
    /// The exported <c>genai_config.json</c> ships <c>top_k = 1</c>, which is greedy decoding — the
    /// same photo would produce the same caption every time. Memes need variety, so sampling is
    /// re-enabled here rather than by editing the model file, which the push script re-verifies by
    /// size and would overwrite.
    /// </summary>
    private const double Temperature = 0.9;
    private const double TopP = 0.95;
    private const int TopK = 50;

    /// <summary>
    /// How many times to re-roll a caption that came back looking like the model answering a
    /// question rather than captioning a photo. See <see cref="LooksLikeMetaResponse"/>.
    /// </summary>
    private const int MaxAttempts = 3;

    private readonly IOnDeviceModelStore _store;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private OrtModel? _model;
    private Tokenizer? _tokenizer;
    private string? _loadedFrom;
    private bool _disposed;

    public QwenCaptionService(IOnDeviceModelStore store)
    {
        _store = store;
    }

    public OnDeviceModel Model => OnDeviceModelCatalog.Qwen25MemeCaption;

    public OnDeviceModelStatus Probe() => _store.Probe(Model);

    public async Task<string> GenerateMemeCaptionAsync(
        string sceneDescription,
        IProgress<string>? stage = null,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(sceneDescription))
        {
            throw new OnDeviceCaptionException(
                "No scene description to work from, so there is nothing to caption.");
        }

        var status = Probe();
        if (!status.IsAvailable || status.Directory is null)
        {
            throw new OnDeviceCaptionException(
                $"{Model.DisplayName} is not on this device. {status.Detail} " +
                "Turn off On-Device Meme Captions in Settings to use the server instead.");
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            EnsureLoaded(status.Directory, stage);
            stage?.Report("Writing caption on device…");
            return await Task.Run(() => Generate(sceneDescription, ct), ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Unload()
    {
        _gate.Wait();
        try
        {
            ReleaseSession();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Loads the weights if they are not already resident, or if they now resolve to a different
    /// directory than the loaded copy — which happens the first time a push lands in external
    /// storage after the app fell back to internal.
    /// </summary>
    private void EnsureLoaded(string directory, IProgress<string>? stage)
    {
        if (_model is not null && _tokenizer is not null &&
            string.Equals(_loadedFrom, directory, StringComparison.Ordinal))
        {
            return;
        }

        ReleaseSession();
        stage?.Report($"Loading {Model.DisplayName}…");

        try
        {
            _model = new OrtModel(directory);
            _tokenizer = new Tokenizer(_model);
            _loadedFrom = directory;
        }
        catch (Exception ex)
        {
            ReleaseSession();

            // OnnxRuntimeGenAIException is the usual case, but a missing external-weights file
            // surfaces as a plain IO or native failure, and an out-of-memory kill on a small device
            // arrives as something else again. All of them mean the same thing to the user.
            throw new OnDeviceCaptionException(
                $"Could not load {Model.DisplayName} from {directory}. " +
                "The transfer may be incomplete — re-run SCRIPTS/push-mobile-model.ps1. " +
                $"({ex.Message})",
                ex);
        }
    }

    /// <summary>
    /// Generates until the output looks like a caption, or the attempt budget runs out.
    /// </summary>
    /// <remarks>
    /// Re-rolling is affordable here in a way it would not be against a metered API: a caption costs
    /// well under a second and no money, so spending extra attempts to avoid showing the user "I'm
    /// not sure what you're asking" is a clear trade. If every attempt still looks like the model
    /// talking about the request, that is reported as a failure rather than displayed — a refusal
    /// presented as the meme caption is worse than an honest error, and the user can retry or turn
    /// the model off.
    /// </remarks>
    private string Generate(string sceneDescription, CancellationToken ct)
    {
        var model = _model ?? throw new OnDeviceCaptionException("Model was unloaded mid-request.");
        var tokenizer = _tokenizer ?? throw new OnDeviceCaptionException("Tokenizer was unloaded mid-request.");

        var last = string.Empty;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            last = GenerateOnce(model, tokenizer, sceneDescription, ct);

            if (!LooksLikeMetaResponse(last))
                return last;
        }

        throw new OnDeviceCaptionException(
            $"{Model.DisplayName} would not caption this photo — it answered "
            + (last.Length == 0 ? "with nothing" : $"\"{last}\"")
            + $" on all {MaxAttempts} attempts. Small models decline photos of people fairly often. "
            + "Try again, or turn off On-Device Meme Captions in Settings to use the server.");
    }

    private static string GenerateOnce(
        OrtModel model,
        Tokenizer tokenizer,
        string sceneDescription,
        CancellationToken ct)
    {
        try
        {
            using var prompt = tokenizer.Encode(BuildPrompt(sceneDescription));
            var promptLength = prompt[0].Length;

            using var options = new GeneratorParams(model);
            options.SetSearchOption("max_length", promptLength + MaxNewTokens);
            options.SetSearchOption("do_sample", true);
            options.SetSearchOption("temperature", Temperature);
            options.SetSearchOption("top_p", TopP);
            options.SetSearchOption("top_k", TopK);

            using var generator = new Generator(model, options);
            generator.AppendTokenSequences(prompt);

            using var stream = tokenizer.CreateStream();
            var caption = new StringBuilder();

            while (!generator.IsDone())
            {
                ct.ThrowIfCancellationRequested();
                generator.GenerateNextToken();

                var next = generator.GetNextTokens();
                if (next.Length == 0)
                    break;

                caption.Append(stream.Decode(next[0]));
            }

            return Tidy(caption.ToString());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new OnDeviceCaptionException(
                $"{OnDeviceModelCatalog.Qwen25MemeCaption.DisplayName} failed while writing the caption: {ex.Message}",
                ex);
        }
    }

    /// <summary>
    /// Builds the ChatML prompt by hand rather than through <c>Tokenizer.ApplyChatTemplate</c>.
    /// The template path wants a JSON message array and a template string, which means escaping a
    /// user-derived description into JSON just to have the runtime parse it back out; the format
    /// Qwen2.5 expects is four literal tags, so writing them is both shorter and one less thing that
    /// can silently change under a model swap.
    /// </summary>
    /// <remarks>
    /// The worked examples are load-bearing, not decoration. A zero-shot version of this prompt was
    /// measured against the same model and ignored its own instructions roughly a third of the time:
    /// emoji despite "no emoji", wrapping quotes, and truncated fragments. The examples fix the
    /// format because a 0.5B model imitates demonstrated shape far more reliably than it follows
    /// described shape. They are deliberately plain rather than joke-heavy — punchier exemplars were
    /// also measured, and they bought a little more wit at the cost of noticeably more incoherence.
    /// </remarks>
    private static string BuildPrompt(string sceneDescription) =>
        "<|im_start|>system\n" +
        "You are a meme caption writer. Reply with exactly one caption, under 12 words, in the voice " +
        "of a meme top-text. No quotes, no emoji, no explanation.<|im_end|>\n" +
        "<|im_start|>user\nPhoto: A dog lying on the floor next to a chewed-up shoe.<|im_end|>\n" +
        "<|im_start|>assistant\nI have no idea who did this<|im_end|>\n" +
        "<|im_start|>user\nPhoto: A man staring at a spreadsheet late at night in an empty office.<|im_end|>\n" +
        "<|im_start|>assistant\nThe numbers went in. Nothing came out.<|im_end|>\n" +
        "<|im_start|>user\nPhoto: A toddler covered head to toe in spaghetti sauce.<|im_end|>\n" +
        "<|im_start|>assistant\nDinner was a team effort<|im_end|>\n" +
        "<|im_start|>user\nPhoto: " + sceneDescription.Trim() + "<|im_end|>\n" +
        "<|im_start|>assistant\n";

    /// <summary>
    /// Trims the model's habitual flourishes. A 0.5B model ignores "no quotation marks" often
    /// enough that stripping them is cheaper than prompt-tuning around it, and it sometimes keeps
    /// going after the caption, so only the first non-empty line is kept.
    /// </summary>
    private static string Tidy(string raw)
    {
        var text = raw.Replace("<|im_end|>", string.Empty, StringComparison.Ordinal).Trim();

        var firstLine = text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(firstLine))
            return string.Empty;

        return StripSymbols(firstLine).Trim('"', '“', '”', '\'', ' ');
    }

    /// <summary>
    /// Drops emoji and other pictographs. The prompt asks for none and the examples show none, and
    /// that got the rate down but not to zero — the model still reaches for a 🎉 now and then, and a
    /// stray emoji in burned-in meme text looks like a bug rather than a joke.
    /// </summary>
    private static string StripSymbols(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.OtherSymbol or UnicodeCategory.Format or UnicodeCategory.PrivateUse)
                continue;

            sb.Append(rune);
        }

        return sb.ToString().Replace("  ", " ", StringComparison.Ordinal).Trim();
    }

    /// <summary>
    /// Rejects output that is the model talking about the request instead of captioning it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two shapes show up in practice. The model either comments on the request ("I'm not sure what
    /// you're asking — it looks like a random image") or declines it outright: the first on-device
    /// run against a real photo returned "I couldn't generate fake people descriptions. Please
    /// provide details if you're looking for something else." Both are coherent sentences, so no
    /// format check catches them, and both read to the user as the feature being broken.
    /// </para>
    /// <para>
    /// Refusals cluster on photos containing people, which is most of the memeable ones, so this is
    /// a common path rather than an edge case. The two structural tells are worth as much as the
    /// phrase list: a caption does not address the reader in the second person, and a caption asked
    /// to be under 12 words does not run to sixteen.
    /// </para>
    /// </remarks>
    private static bool LooksLikeMetaResponse(string caption)
    {
        if (caption.Length == 0)
            return true;

        // Asked for under 12 words; past 16 it has stopped captioning and started explaining.
        if (caption.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 16)
            return true;

        string[] tells =
        [
            // Talking about the request rather than answering it.
            "i'm not sure", "i am not sure", "not sure what", "as an ai", "as a language model",
            "it looks like some", "random image", "the photo shows", "the image shows",
            "here is a caption", "here's a caption", "caption:",

            // Declining it.
            "i couldn't", "i could not", "i cannot", "i can't", "i am unable", "i'm unable",
            "unable to", "sorry", "i don't have", "i do not have",

            // Addressing the user instead of captioning.
            "please provide", "let me know", "if you're looking", "if you are looking",
            "would you like", "feel free to",
        ];

        return tells.Any(t => caption.Contains(t, StringComparison.OrdinalIgnoreCase));
    }

    private void ReleaseSession()
    {
        _tokenizer?.Dispose();
        _tokenizer = null;
        _model?.Dispose();
        _model = null;
        _loadedFrom = null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        ReleaseSession();
        _gate.Dispose();
    }
}
