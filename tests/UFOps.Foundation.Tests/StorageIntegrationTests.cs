using UFOps.Foundation.Storage;

namespace UFOps.Foundation.Tests;

public sealed class StorageIntegrationTests
{
    [Fact]
    public async Task FoundationDatabaseUsesRealSqliteAndIsIdempotent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "ufops-foundation-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "foundation.db");
        try
        {
            var database = new FoundationDatabase(path);
            await database.InitializeAsync(cancellationToken);
            await database.InitializeAsync(cancellationToken);
            await database.SetMetadataAsync("test-key", "test-value", cancellationToken);

            Assert.True(File.Exists(path));
            Assert.Equal(1, await database.GetSchemaVersionAsync(cancellationToken));
            Assert.Equal("test-value", await database.GetMetadataAsync("test-key", cancellationToken));
            Assert.Equal("ok", await database.QuickCheckAsync(cancellationToken));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
