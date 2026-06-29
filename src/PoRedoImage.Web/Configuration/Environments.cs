namespace PoRedoImage.Web.Configuration;

/// <summary>
/// Environment name constants + a single <see cref="IsDevOrTest"/> helper used in the few places
/// where a code branch should fire for BOTH the local F5 dev loop and the integration/E2E test
/// tiers (FakeAuth registration, /auth/login/fake, the dev-only diagnostic policy gate).
///
/// Running locally = Development. Running in Azure App Service = Production. Running
/// integration + E2E tests = Test. The Test env is opted into via
/// <c>builder.UseEnvironment("Test")</c> in the WebApplicationFactory fixtures; the test
/// projects set <c>ASPNETCORE_ENVIRONMENT=Test</c> in <c>launchSettings.json</c> / CI.
/// </summary>
public static class PoEnvironments
{
    public const string Test = "Test";

    /// <summary>True when the host is in the local dev loop (F5 / dotnet run) OR running tests.</summary>
    public static bool IsDevOrTest(this IWebHostEnvironment env) =>
        env.IsDevelopment() || env.IsEnvironment(Test);
}
