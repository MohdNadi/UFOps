using UFOps.Core;
using UFOps.Discovery;

namespace UFOps.Discovery.Tests;

public sealed class DiscoveryContractEdgeTests
{
    [Fact]
    public void EmptyRootSetIsRejectedAtContractBoundary()
    {
        var exception = Assert.Throws<ArgumentException>(() => new DiscoveryRequest(Array.Empty<string>()));
        Assert.Contains("at least one", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WhitespaceRootIsRejectedAtContractBoundary()
    {
        var exception = Assert.Throws<ArgumentException>(() => new DiscoveryRequest(["   "]));
        Assert.Contains("whitespace", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MalformedRootFailsClosedWithStructuredValidationError()
    {
        var malformed = $"invalid{Path.DirectorySeparatorChar}\0root".Replace("\\0", "\0", StringComparison.Ordinal);
        var result = await new DiscoveryEngine().DiscoverAsync(
            new DiscoveryRequest([malformed]),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCategory.Validation, result.Error!.Category);
        Assert.Equal("DISCOVERY.NO_VALID_ROOTS", result.Error.Code.Value);
    }

    [Fact]
    public async Task ExplicitFileRootProducesOneNormalizedFileRecord()
    {
        var directory = Path.Combine(Path.GetTempPath(), "UFOps-Discovery-Edge", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "ملف.txt");
        var payload = new byte[] { 1, 2, 3, 4, 5, 6, 7 };
        await File.WriteAllBytesAsync(file, payload, TestContext.Current.CancellationToken);

        try
        {
            var result = await new DiscoveryEngine().DiscoverAsync(
                new DiscoveryRequest([file]),
                TestContext.Current.CancellationToken);

            Assert.True(result.IsSuccess);
            Assert.False(result.Value.HasErrors);
            var entry = Assert.Single(result.Value.Entries);
            Assert.Equal(DiscoveryEntryKind.File, entry.Kind);
            Assert.Equal(payload.Length, entry.LengthBytes);
            Assert.Equal(".", entry.RelativePath);
            Assert.Equal(TimeSpan.Zero, entry.LastWriteTimeUtc.Offset);
            Assert.Equal(Path.GetFullPath(file), entry.FullPath, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task EmptyDirectoryRootProducesDirectoryRecordWithoutFabricatedChildren()
    {
        var directory = Path.Combine(Path.GetTempPath(), "UFOps-Discovery-Edge", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var result = await new DiscoveryEngine().DiscoverAsync(
                new DiscoveryRequest([directory]),
                TestContext.Current.CancellationToken);

            Assert.True(result.IsSuccess);
            Assert.False(result.Value.HasErrors);
            var entry = Assert.Single(result.Value.Entries);
            Assert.Equal(DiscoveryEntryKind.Directory, entry.Kind);
            Assert.Null(entry.LengthBytes);
            Assert.Equal(".", entry.RelativePath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
