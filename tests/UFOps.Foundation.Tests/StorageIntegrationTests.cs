using Microsoft.Data.Sqlite;
using UFOps.Foundation.Storage;

namespace UFOps.Foundation.Tests;

public sealed class StorageIntegrationTests
{
    [Fact]
    public async Task FoundationDatabaseUsesRealSqliteWalAndIsIdempotent()
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
            Assert.Equal(FoundationDatabase.CurrentSchemaVersion, await database.GetSchemaVersionAsync(cancellationToken));
            Assert.Equal("wal", (await database.GetJournalModeAsync(cancellationToken)).ToLowerInvariant());
            Assert.True(await database.GetForeignKeysEnabledAsync(cancellationToken));
            Assert.Equal("test-value", await database.GetMetadataAsync("test-key", cancellationToken));
            Assert.Equal("ok", await database.QuickCheckAsync(cancellationToken));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task FoundationMetadataPersistsAcrossDatabaseInstances()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "ufops-foundation-persistence-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "foundation.db");
        try
        {
            var first = new FoundationDatabase(path);
            await first.InitializeAsync(cancellationToken);
            await first.SetMetadataAsync("persistent-key", "persistent-value", cancellationToken);

            var second = new FoundationDatabase(path);
            await second.InitializeAsync(cancellationToken);
            Assert.Equal("persistent-value", await second.GetMetadataAsync("persistent-key", cancellationToken));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task FoundationDatabaseFailsClosedOnUnknownFutureSchema()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "ufops-foundation-schema-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "foundation.db");
        try
        {
            var database = new FoundationDatabase(path);
            await database.InitializeAsync(cancellationToken);

            await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString()))
            {
                await connection.OpenAsync(cancellationToken);
                await using var command = connection.CreateCommand();
                command.CommandText = "INSERT INTO schema_migrations(version, applied_utc, description) VALUES (2, '2026-08-31T00:00:00Z', 'future schema');";
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            var reopened = new FoundationDatabase(path);
            await Assert.ThrowsAsync<InvalidDataException>(() => reopened.InitializeAsync(cancellationToken).AsTask());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
