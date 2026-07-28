using Microsoft.Extensions.Logging.Abstractions;
using PoRedoImage.Client.LocalAi;

namespace PoRedoImage.Tests.Unit.Features;

/// <summary>
/// Covers the dtype fallback chain required by NET_RULES §5.
/// </summary>
/// <remarks>
/// The chain lives in C# precisely so it can be tested like this: no browser, no GPU, no network.
/// Device capabilities are a plain record, and the runtime is faked, so every pruning and
/// advance-on-failure path is exercised deterministically.
/// </remarks>
public class LocalAiChainTests
{
    private static readonly DeviceCapabilities FullGpu = new(HasWebGpu: true, HasShaderF16: true);
    private static readonly DeviceCapabilities GpuNoF16 = new(HasWebGpu: true, HasShaderF16: false);
    private static readonly DeviceCapabilities NoGpu = DeviceCapabilities.None;

    private static LocalModelDescriptor Model(params DtypeVariant[] chain) => new(
        Id: new LocalModelId("test-model"),
        DisplayName: "Test Model",
        Capability: LocalCapability.Vision,
        Runtime: LocalRuntime.TransformersJs,
        RepoId: "test/model",
        VariantChain: chain,
        ApproxDownloadMb: 1);

    // ── Probe-time pruning ───────────────────────────────────────────

    public static TheoryData<DeviceCapabilities, DtypeVariant[], LocalDevice> PruningCases() => new()
    {
        // Full WebGPU: nothing is pruned, declared order preserved.
        { FullGpu, [DtypeVariant.Q4F16, DtypeVariant.F16, DtypeVariant.Q4, DtypeVariant.F32], LocalDevice.WebGpu },
        // GPU without shader-f16: both half-precision variants go. Attempting one only ever
        // produces a shader-compilation failure, so it must never be attempted at all.
        { GpuNoF16, [DtypeVariant.Q4, DtypeVariant.F32], LocalDevice.WebGpu },
        // No adapter at all: execution drops to wasm, where fp16 does not exist.
        { NoGpu, [DtypeVariant.Q4, DtypeVariant.F32], LocalDevice.Wasm },
    };

    [Theory]
    [MemberData(nameof(PruningCases))]
    public void Chain_is_pruned_to_what_the_device_can_execute(
        DeviceCapabilities capabilities, DtypeVariant[] expected, LocalDevice expectedDevice)
    {
        var chain = DtypeChain.Create(
            Model(DtypeVariant.Q4F16, DtypeVariant.F16, DtypeVariant.Q4, DtypeVariant.F32), capabilities);

        Assert.Equal(expected, chain.Variants);
        Assert.Equal(expectedDevice, chain.Device);
    }

    [Fact]
    public void Every_chain_must_survive_the_weakest_device()
    {
        // An entry declaring only fp16 variants is unusable without shader-f16. That is a registry
        // mistake and must surface loudly rather than as a silent no-op...
        var ex = Assert.Throws<InvalidOperationException>(
            () => DtypeChain.Create(Model(DtypeVariant.F16, DtypeVariant.Q4F16), NoGpu));
        Assert.Contains("Q4 or F32", ex.Message, StringComparison.Ordinal);

        // ...and no real registry entry may violate it.
        foreach (var descriptor in LocalModelRegistry.All)
        {
            Assert.NotEmpty(DtypeChain.Create(descriptor, NoGpu).Variants);
        }
    }

    // ── Load-time advance ────────────────────────────────────────────

    [Fact]
    public void Advance_walks_the_chain_then_reports_exhaustion()
    {
        var chain = DtypeChain.Create(Model(DtypeVariant.Q4, DtypeVariant.F32), FullGpu);

        Assert.Equal(DtypeVariant.Q4, chain.Current);
        Assert.Equal(1, chain.AttemptNumber);

        Assert.True(chain.Advance());
        Assert.Equal(DtypeVariant.F32, chain.Current);
        Assert.Equal(2, chain.AttemptNumber);

        Assert.False(chain.Advance());
        Assert.True(chain.IsExhausted);
        Assert.Throws<InvalidOperationException>(() => chain.Current);
    }

    [Theory]
    [InlineData(LocalAiFailure.OutOfMemory, true)]
    [InlineData(LocalAiFailure.DeviceLost, true)]
    [InlineData(LocalAiFailure.ShaderCompilation, true)]
    [InlineData(LocalAiFailure.ShaderF16Unsupported, true)]
    [InlineData(LocalAiFailure.Network, false)]
    [InlineData(LocalAiFailure.ModelUnsupported, false)]
    [InlineData(LocalAiFailure.NoWebGpuAdapter, false)]
    public void Only_resource_failures_are_worth_a_smaller_variant(LocalAiFailure failure, bool recoverable)
    {
        // Retrying a network failure at a smaller dtype just spends the user's bandwidth to reach
        // exactly the same error.
        Assert.Equal(recoverable, DtypeChain.IsRecoverable(failure));
    }

    // ── Session behaviour ────────────────────────────────────────────

    [Fact]
    public async Task Session_falls_back_to_the_next_variant_after_an_out_of_memory()
    {
        var runtime = new FakeRuntime(v => v == DtypeVariant.Q4
            ? throw new LocalInferenceException(LocalAiFailure.OutOfMemory, "out of memory")
            : "described");

        var session = new LocalInferenceSession(runtime, NullLogger.Instance);
        var outcome = await session.RunAsync(
            Model(DtypeVariant.Q4, DtypeVariant.F32), FullGpu, "prompt");

        Assert.Equal("described", outcome.Text);
        Assert.Equal(DtypeVariant.F32, outcome.Variant);
        Assert.Equal(2, outcome.Attempts);
        Assert.Equal([DtypeVariant.Q4, DtypeVariant.F32], runtime.Attempted);
    }

    [Theory]
    // Unrecoverable: repeating at a smaller dtype reaches the identical error, so stop at one.
    [InlineData(LocalAiFailure.Network, 1)]
    // Recoverable: every variant is tried before giving up.
    [InlineData(LocalAiFailure.DeviceLost, 2)]
    public async Task Session_attempt_count_matches_whether_the_failure_is_recoverable(
        LocalAiFailure failure, int expectedAttempts)
    {
        var runtime = new FakeRuntime(_ => throw new LocalInferenceException(failure, "boom"));
        var session = new LocalInferenceSession(runtime, NullLogger.Instance);

        var ex = await Assert.ThrowsAsync<LocalInferenceException>(() =>
            session.RunAsync(Model(DtypeVariant.Q4, DtypeVariant.F32), FullGpu, "prompt"));

        Assert.Equal(expectedAttempts, runtime.Attempted.Count);
        if (expectedAttempts > 1)
            Assert.Contains("Every dtype variant", ex.Message, StringComparison.Ordinal);
        else
            Assert.Equal(failure, ex.Failure);
    }

    // ── Runtime-specific variant interpretation ──────────────────────

    [Fact]
    public void Each_runtime_interprets_a_variant_in_its_own_terms()
    {
        var transformers = Model(DtypeVariant.Q4);
        var webllm = new LocalModelDescriptor(
            new LocalModelId("t"), "T", LocalCapability.Text, LocalRuntime.WebLlm,
            "Qwen2.5-0.5B-Instruct", [DtypeVariant.Q4F16], 1);

        // transformers.js takes the repo path unchanged and the dtype separately; WebLLM has no
        // dtype parameter, so the variant is encoded into the model id instead.
        Assert.Equal("test/model", LocalModelRegistry.ResolveModelReference(transformers, DtypeVariant.Q4));
        Assert.Equal("q4", LocalModelRegistry.TransformersDtype(DtypeVariant.Q4));
        Assert.Equal(
            "Qwen2.5-0.5B-Instruct-q4f16_1-MLC",
            LocalModelRegistry.ResolveModelReference(webllm, DtypeVariant.Q4F16));
    }

    [Theory]
    [InlineData("Device was lost while loading", LocalAiFailure.DeviceLost)]
    [InlineData("Out of memory", LocalAiFailure.OutOfMemory)]
    [InlineData("shader-f16 is required", LocalAiFailure.ShaderF16Unsupported)]
    [InlineData("requestAdapter returned null", LocalAiFailure.NoWebGpuAdapter)]
    [InlineData("Failed to fetch", LocalAiFailure.Network)]
    public void Raw_runtime_errors_are_classified(string raw, LocalAiFailure expected)
        => Assert.Equal(expected, LocalAiErrorClassifier.Classify(raw));

    /// <summary>Runtime stub — records attempts and fails or succeeds per variant.</summary>
    private sealed class FakeRuntime(Func<DtypeVariant, string> behaviour) : ILocalInferenceRuntime
    {
        public List<DtypeVariant> Attempted { get; } = [];

        public LocalRuntime Runtime => LocalRuntime.TransformersJs;

        public Task<string> RunAsync(
            LocalModelDescriptor descriptor, DtypeVariant variant, LocalDevice device,
            string prompt, byte[]? image, IProgress<LocalInferenceStatus>? progress, CancellationToken ct)
        {
            Attempted.Add(variant);
            return Task.FromResult(behaviour(variant));
        }
    }
}
