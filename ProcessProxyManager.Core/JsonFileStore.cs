using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProcessProxyManager.Core;

public class JsonFileStore<T> where T : new()
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public JsonFileStore(string filePath)
    {
        FilePath = filePath;
    }

    public string FilePath { get; }

    public async Task<T> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(FilePath))
        {
            return new T();
        }

        await using var stream = File.OpenRead(FilePath);
        var document = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
        return document ?? new T();
    }

    public async Task SaveAsync(T document, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(FilePath);
        await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken);
    }
}
