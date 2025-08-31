using Microsoft.AspNetCore.Components;
using System.Net.Http.Json;

namespace Client.Services;

/// <summary>
/// Service for making HTTP requests to the server API
/// </summary>
public class ApiService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ApiService> _logger;

    public ApiService(HttpClient httpClient, ILogger<ApiService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Performs an HTTP POST request with simple retry logic
    /// </summary>
    public async Task<TResponse?> PostAsync<TRequest, TResponse>(string uri, TRequest request)
        where TRequest : class
        where TResponse : class
    {
        try
        {
            _logger.LogInformation("Making POST request to {Uri}", uri);

            // Simple retry logic - retry once on timeout or server error
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    var response = await _httpClient.PostAsJsonAsync(uri, request);

                    if (response.IsSuccessStatusCode)
                    {
                        _logger.LogInformation("Request to {Uri} successful", uri);
                        return await response.Content.ReadFromJsonAsync<TResponse>();
                    }

                    // Retry only on server errors or timeouts
                    if (attempt == 0 && ((int)response.StatusCode >= 500 || response.StatusCode == System.Net.HttpStatusCode.RequestTimeout))
                    {
                        _logger.LogWarning("Request to {Uri} failed with {Status}, retrying...", uri, response.StatusCode);
                        await Task.Delay(1000); // Simple 1 second delay
                        continue;
                    }

                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Request to {Uri} failed: {Status} - {Error}", uri, response.StatusCode, errorContent);
                    throw new HttpRequestException($"API request failed: {response.StatusCode}");
                }
                catch (TaskCanceledException) when (attempt == 0)
                {
                    _logger.LogWarning("Request to {Uri} timed out, retrying...", uri);
                    await Task.Delay(1000);
                    continue;
                }
            }

            throw new InvalidOperationException("Retry logic failed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error making request to {Uri}", uri);
            throw;
        }
    }

    /// <summary>
    /// Performs an HTTP GET request
    /// </summary>
    public async Task<TResponse?> GetAsync<TResponse>(string uri)
        where TResponse : class
    {
        try
        {
            _logger.LogInformation("Making GET request to {Uri}", uri);
            var response = await _httpClient.GetAsync(uri);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<TResponse>();
            }

            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogError("GET request to {Uri} failed: {Status} - {Error}", uri, response.StatusCode, errorContent);
            throw new HttpRequestException($"API request failed: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error making GET request to {Uri}", uri);
            throw;
        }
    }
}