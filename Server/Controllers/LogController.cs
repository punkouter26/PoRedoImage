using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace Server.Controllers;

/// <summary>
/// Controller for receiving client-side logs from the Blazor WebAssembly client.
/// This enables centralized logging of client-side events in Application Insights.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class LogController : ControllerBase
{
    private readonly ILogger<LogController> _logger;
    private readonly Microsoft.ApplicationInsights.TelemetryClient _telemetryClient;

    public LogController(
        ILogger<LogController> logger,
        Microsoft.ApplicationInsights.TelemetryClient telemetryClient)
    {
        _logger = logger;
        _telemetryClient = telemetryClient;
    }

    /// <summary>
    /// Receives client-side logs and forwards them to Serilog and Application Insights
    /// POST /api/log/client
    /// </summary>
    [HttpPost("client")]
    public IActionResult LogClientMessage([FromBody] ClientLogEntry logEntry)
    {
        try
        {
            if (logEntry == null || string.IsNullOrWhiteSpace(logEntry.Message))
            {
                return BadRequest(new { error = "Log message is required" });
            }

            // Sanitize and validate log level
            var logLevel = ParseLogLevel(logEntry.Level);
            
            // Create structured log properties
            var properties = new Dictionary<string, object>
            {
                { "Source", "Client" },
                { "ClientTimestamp", logEntry.Timestamp },
                { "UserAgent", Request.Headers.UserAgent.ToString() },
                { "ClientUrl", logEntry.Url ?? "unknown" },
                { "SessionId", logEntry.SessionId ?? "unknown" }
            };

            // Add any additional properties from the client
            if (logEntry.Properties != null)
            {
                foreach (var prop in logEntry.Properties)
                {
                    properties[$"Client_{prop.Key}"] = prop.Value;
                }
            }

            // Log to Serilog with structured properties
            using (Serilog.Context.LogContext.PushProperty("ClientLog", true))
            using (Serilog.Context.LogContext.PushProperty("ClientProperties", properties, true))
            {
                switch (logLevel)
                {
                    case LogLevel.Trace:
                    case LogLevel.Debug:
                        _logger.LogDebug("[CLIENT] {Message}", logEntry.Message);
                        break;
                    case LogLevel.Information:
                        _logger.LogInformation("[CLIENT] {Message}", logEntry.Message);
                        break;
                    case LogLevel.Warning:
                        _logger.LogWarning("[CLIENT] {Message}", logEntry.Message);
                        break;
                    case LogLevel.Error:
                        _logger.LogError("[CLIENT] {Message} - Error: {Error}", logEntry.Message, logEntry.ErrorDetails);
                        break;
                    case LogLevel.Critical:
                        _logger.LogCritical("[CLIENT] {Message} - Error: {Error}", logEntry.Message, logEntry.ErrorDetails);
                        break;
                    default:
                        _logger.LogInformation("[CLIENT] {Message}", logEntry.Message);
                        break;
                }
            }

            // Also send to Application Insights as a custom event for better tracking
            _telemetryClient.TrackEvent("ClientLog", new Dictionary<string, string>
            {
                { "Message", logEntry.Message },
                { "Level", logEntry.Level },
                { "Url", logEntry.Url ?? "unknown" },
                { "SessionId", logEntry.SessionId ?? "unknown" },
                { "ErrorDetails", logEntry.ErrorDetails ?? "" }
            });

            return Ok(new { message = "Log received successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing client log");
            return StatusCode(500, new { error = "Failed to process log" });
        }
    }

    private LogLevel ParseLogLevel(string level)
    {
        return level?.ToLowerInvariant() switch
        {
            "trace" => LogLevel.Trace,
            "debug" => LogLevel.Debug,
            "information" or "info" => LogLevel.Information,
            "warning" or "warn" => LogLevel.Warning,
            "error" => LogLevel.Error,
            "critical" or "fatal" => LogLevel.Critical,
            _ => LogLevel.Information
        };
    }
}

/// <summary>
/// Model for client-side log entries
/// </summary>
public class ClientLogEntry
{
    public string Message { get; set; } = string.Empty;
    public string Level { get; set; } = "Information";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? Url { get; set; }
    public string? SessionId { get; set; }
    public string? ErrorDetails { get; set; }
    public Dictionary<string, object>? Properties { get; set; }
}
