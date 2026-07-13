using System.Text.Json;
using ProcessProxyManager.Core;
using Xunit;

namespace ProcessProxyManager.Core.Tests;

public sealed class JsonFileStoreTests
{
    [Fact]
    public async Task SaveAsync_ReplacesExistingDocumentWithoutLeavingTemporaryFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ProxyPilot.Tests", Guid.NewGuid().ToString("N"));
        var filePath = Path.Combine(directory, "settings.json");
        var store = new JsonFileStore<TestDocument>(filePath);

        try
        {
            await store.SaveAsync(new TestDocument { Value = "first" });
            await store.SaveAsync(new TestDocument { Value = "second" });

            var json = await File.ReadAllTextAsync(filePath);
            Assert.Equal("second", JsonDocument.Parse(json).RootElement.GetProperty("value").GetString());
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private sealed class TestDocument
    {
        public string Value { get; set; } = string.Empty;
    }
}
