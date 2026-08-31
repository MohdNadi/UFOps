using System.Text.Json;
using UFOps.Core;

namespace UFOps.Foundation.Tests;

public sealed class EvidenceAndCorpusTests
{
    [Fact]
    public async Task EvidenceWriterPersistsValidJsonLinesToRealFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "ufops-evidence-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "evidence.jsonl");
        try
        {
            using var writer = new JsonLinesEvidenceWriter(path);
            await writer.AppendAsync(EvidenceRecord.Create("foundation", "first", EvidenceOutcome.Pass, DateTimeOffset.UtcNow));
            await writer.AppendAsync(EvidenceRecord.Create("foundation", "second", EvidenceOutcome.Info, DateTimeOffset.UtcNow));

            var lines = await File.ReadAllLinesAsync(path);
            Assert.Equal(2, lines.Length);
            foreach (var line in lines)
            {
                using var document = JsonDocument.Parse(line);
                Assert.True(document.RootElement.TryGetProperty("eventId", out _));
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
    public void CorpusCaseRejectsPathTraversal()
    {
        Assert.Throws<ArgumentException>(() => new GoldenCorpusCase(
            "bad-case",
            "../outside.pdf",
            new string('a', 64),
            new Dictionary<string, string> { ["status"] = "PASS" }));
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
