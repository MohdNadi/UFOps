using UFOps.Foundation.Storage;

namespace UFOps.Foundation.Tests;

public sealed class StorageIntegrationTests
{
    [Fact]
    public async Task FoundationDatabaseUsesRealSqliteAndIsIdempotent()
    {
        var root = Path.Combine(Path.GetTempPath(), "ufops-foundation-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "foundation.db");
        try
        {
            var database = new FoundationDatabase(path);
            await database.InitializeAsync();
            await database.InitializeAsync();
            await database.SetMetadataAsync("test-key", "test-value");

            Assert.True(File.Exists(path));
            Assert.Equal(1, await database.GetSchemaVersionAsync());
            Assert.Equal("test-value", await database.GetMetadataAsync("test-key"));
            Assert.Equal("ok", await database.QuickCheckAsync());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
