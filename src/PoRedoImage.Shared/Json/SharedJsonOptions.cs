using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace PoRedoImage.Shared.Json;

/// <summary>
/// The one <see cref="JsonSerializerOptions"/> both sides of the wire serialize with.
/// </summary>
/// <remarks>
/// <para>
/// §1 asks for source-generated serialization "across API and WASM". The server half is wired
/// once in <c>PoRedoImage.Web.Program.cs</c> via <c>ConfigureHttpJsonOptions</c>. The client half
/// has no such hook: <c>HttpClient.GetFromJsonAsync&lt;T&gt;(url)</c> with no options argument uses
/// an internal <c>JsonSerializerDefaults.Web</c> instance that nothing can intercept, so every
/// WASM call site has to pass these options explicitly to reach the source-generated path.
/// </para>
/// <para>
/// <see cref="SharedJsonContext"/> is deliberately FIRST in the resolver chain — <see cref="JsonTypeInfoResolver.Combine"/>
/// takes the first resolver that answers, and the reflective one answers for everything, so putting
/// it first would leave the context unused. <see cref="DefaultJsonTypeInfoResolver"/> remains as the
/// tail so page-local view models (Diag's payload, the gallery items) still serialize without
/// needing an entry in the context. <c>SharedJsonContractTests</c> pins the ordering.
/// </para>
/// </remarks>
public static class SharedJsonOptions
{
    /// <summary>
    /// Source-generation-first resolver chain, with reflection as the fallback tail.
    /// </summary>
    // IL2026: DefaultJsonTypeInfoResolver IS the reflective resolver, kept deliberately as the
    // fallback for types outside the shared contract. Shared DTOs never reach it.
#pragma warning disable IL2026
    public static IJsonTypeInfoResolver CreateResolver() => JsonTypeInfoResolver.Combine(
        new SharedJsonContext(),
        new DefaultJsonTypeInfoResolver());
#pragma warning restore IL2026

    /// <summary>
    /// Options matching the server's minimal-API serializer exactly: web defaults (camelCase,
    /// case-insensitive reads), source-gen-first resolution, and null properties omitted.
    /// </summary>
    public static JsonSerializerOptions Default { get; } = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = CreateResolver(),
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
