using System.Text.Json;
using UFOps.Core;

namespace UFOps.Foundation.Tests;

public sealed class EvidenceAndCorpusTests
{
    [Fact]
    public async Task EvidenceWriterPersistsValidJsonLinesToRealFile()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "ufops-evidence-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "evidence.jsonl");
        try
        {
            using var writer = new JsonLinesEvidenceWriter(path);
            await writer.AppendAsync(EvidenceRecord.Create("foundation", "first", EvidenceOutcome.Pass, DateTimeOffset.UtcNow), cancellationToken);
            await writer.AppendAsync(EvidenceRecord.Create("foundation", "second", EvidenceOutcome.Info, DateTimeOffset.UtcNow), cancellationToken);

            var lines = await File.ReadAllLinesAsync(path, cancellationToken);
            Assert.Equal(2, lines.Length);
            foreach (var line in lines)
            {
                using var document = JsonDocument.Parse(line);
                Assert.True(document.RootElement.TryGetProperty("eventId", out var eventId));
                Assert.NotEqual(Guid.Empty, eventId.GetGuid());
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task EvidenceWriterSerializesExactOperationBinding()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "ufops-evidence-binding-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "evidence.jsonl");
        try
        {
            var now = DateTimeOffset.UtcNow;
            var plan = new OperationPlan(OperationId.New(), 7, now, [new PlannedItem("item-1", "file:///one", null, "inspect")]);
            using var writer = new JsonLinesEvidenceWriter(path);
            await writer.AppendAsync(
                EvidenceRecord.Create("foundation", "bound-event", EvidenceOutcome.Pass, now, plan.Binding),
                cancellationToken);

            var line = Assert.Single(await File.ReadAllLinesAsync(path, cancellationToken));
            using var document = JsonDocument.Parse(line);
            var binding = document.RootElement.GetProperty("operationBinding");
            Assert.Equal(7, binding.GetProperty("planRevision").GetInt32());
            Assert.Equal(
                plan.Binding.PlanFingerprint.Value,
                binding.GetProperty("planFingerprint").GetProperty("value").GetString());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task EvidenceWriterSerializesConcurrentAppendsAsWholeJsonLines()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "ufops-evidence-concurrency-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "evidence.jsonl");
        try
        {
            using var writer = new JsonLinesEvidenceWriter(path);
            var tasks = Enumerable.Range(0, 32)
                .Select(index => writer.AppendAsync(
                    EvidenceRecord.Create("foundation", $"event-{index}", EvidenceOutcome.Info, DateTimeOffset.UtcNow),
                    cancellationToken).AsTask())
                .ToArray();
            await Task.WhenAll(tasks);

            var lines = await File.ReadAllLinesAsync(path, cancellationToken);
            Assert.Equal(32, lines.Length);
            foreach (var line in lines)
            {
                using var document = JsonDocument.Parse(line);
                Assert.Equal("foundation", document.RootElement.GetProperty("component").GetString());
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void EvidenceRejectsNonUtcTimestamp()
    {
        var nonUtc = new DateTimeOffset(2026, 8, 31, 4, 0, 0, TimeSpan.FromHours(3));
        Assert.Throws<ArgumentException>(() => EvidenceRecord.Create("foundation", "bad-time", EvidenceOutcome.Fail, nonUtc));
    }

    [Theory]
    [InlineData("../outside.pdf")]
    [InlineData("folder/../outside.pdf")]
    [InlineData("folder//file.pdf")]
    [InlineData("file.pdf:stream")]
    public void CorpusCaseRejectsUnsafePaths(string relativePath)
    {
        Assert.Throws<ArgumentException>(() => new GoldenCorpusCase(
            "bad-case",
            relativePath,
            new string('a', 64),
            new Dictionary<string, string> { ["status"] = "PASS" }));
    }

    [Fact]
    public void CorpusCaseRequiresAtLeastOneExpectedResult()
    {
        Assert.Throws<ArgumentException>(() => new GoldenCorpusCase(
            "empty-case",
            "inputs/sample.pdf",
            new string('a', 64),
            Array.Empty<KeyValuePair<string, string>>()));
    }

    [Fact]
    public void CorpusManifestRejectsDuplicateCaseIds()
    {
        var corpusCase = new GoldenCorpusCase(
            "case-1",
            "inputs/sample.pdf",
            new string('a', 64),
            new Dictionary<string, string> { ["status"] = "PASS" });

        Assert.Throws<ArgumentException>(() => new GoldenCorpusManifest(
            GoldenCorpusManifest.CurrentSchemaVersion,
            "foundation-corpus",
            "Foundation corpus",
            [corpusCase, corpusCase]));
    }
}
