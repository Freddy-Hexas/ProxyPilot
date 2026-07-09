using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ProcessProxyManager.Core;

namespace ProcessProxyManager.Mihomo;

public sealed class MihomoApiClient
{
    private readonly HttpClient _httpClient;

    public MihomoApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<MihomoApiResult> CheckAsync(string apiUrl, string secret, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, apiUrl, "/version", secret);
        return await SendAsync(request, cancellationToken);
    }

    public async Task<MihomoApiResult> ReloadConfigAsync(
        string apiUrl,
        string secret,
        string configPath,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Put, apiUrl, "/configs", secret);
        request.Content = CreateReloadContent(configPath);

        return await SendAsync(request, cancellationToken);
    }

    public async Task<MihomoApiResult> ReloadConfigAsync(
        string apiUrl,
        string secret,
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Put, apiUrl, "/configs", secret);
        request.Content = CreateReloadContent(settings);

        return await SendAsync(request, cancellationToken);
    }

    public async Task<IReadOnlyList<MihomoConnection>> GetConnectionsAsync(
        string apiUrl,
        string secret,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, apiUrl, "/connections", secret);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("connections", out var connectionsElement) ||
            connectionsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var connections = new List<MihomoConnection>();
        foreach (var item in connectionsElement.EnumerateArray())
        {
            var network = GetString(item, "metadata", "network");
            var process = GetString(item, "metadata", "process");
            var processPath = GetString(item, "metadata", "processPath");
            var sourceIp = GetString(item, "metadata", "sourceIP");
            var sourcePort = GetInt(item, "metadata", "sourcePort");
            var destinationIp = GetString(item, "metadata", "destinationIP");
            var destinationPort = GetInt(item, "metadata", "destinationPort");
            var host = GetString(item, "metadata", "host");
            var remoteDestination = GetString(item, "metadata", "remoteDestination");
            var rule = GetString(item, "rule");
            var rulePayload = GetString(item, "rulePayload");
            var chains = GetStringArray(item, "chains");

            connections.Add(new MihomoConnection(
                network,
                process,
                processPath,
                sourceIp,
                sourcePort,
                destinationIp,
                destinationPort,
                host,
                remoteDestination,
                rule,
                rulePayload,
                chains));
        }

        return connections;
    }

    private async Task<MihomoApiResult> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return new MihomoApiResult(response.IsSuccessStatusCode, (int)response.StatusCode, body);
        }
        catch (Exception exception)
        {
            return new MihomoApiResult(false, 0, exception.Message);
        }
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string apiUrl, string path, string secret)
    {
        var baseUri = apiUrl.EndsWith("/", StringComparison.Ordinal) ? apiUrl.TrimEnd('/') : apiUrl;
        var request = new HttpRequestMessage(method, $"{baseUri}{path}");

        if (!string.IsNullOrWhiteSpace(secret))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        }

        return request;
    }

    public static StringContent CreateReloadContent(AppSettings settings)
    {
        return CreateReloadContent(settings.GeneratedConfigPath);
    }

    public static StringContent CreateReloadContent(string configPath)
    {
        return new StringContent(
            JsonSerializer.Serialize(new { path = Path.GetFullPath(configPath) }),
            Encoding.UTF8,
            "application/json");
    }

    private static string GetString(JsonElement element, string parent, string property)
    {
        if (!element.TryGetProperty(parent, out var parentElement) ||
            !parentElement.TryGetProperty(property, out var propertyElement) ||
            propertyElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return string.Empty;
        }

        return propertyElement.ToString();
    }

    private static int GetInt(JsonElement element, string parent, string property)
    {
        if (!element.TryGetProperty(parent, out var parentElement) ||
            !parentElement.TryGetProperty(property, out var propertyElement))
        {
            return 0;
        }

        return propertyElement.TryGetInt32(out var value) ? value : 0;
    }

    private static string GetString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var propertyElement) ||
            propertyElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return string.Empty;
        }

        return propertyElement.ToString();
    }

    private static IReadOnlyList<string> GetStringArray(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var propertyElement) ||
            propertyElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return propertyElement
            .EnumerateArray()
            .Select(static item => item.ToString())
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .ToList();
    }
}
