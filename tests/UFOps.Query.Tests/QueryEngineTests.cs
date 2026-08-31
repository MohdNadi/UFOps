using System.Diagnostics;
using UFOps.Core;
using UFOps.Discovery;
using UFOps.EngineSdk;

namespace UFOps.Query.Tests;

public sealed class QueryEngineTests
{
    [Fact]
    public async Task IncludeExcludeExceptPrecedenceIsEnforced()
    {
        var pdf = Entry("C:/source/keep.pdf", "keep.pdf", DiscoveryEntryKind.File, 10);
        var restore = Entry("C:/source/restore.pdf", "restore.pdf", DiscoveryEntryKind.File, 20);
        var txt = Entry("C:/source/other.txt", "other.txt", DiscoveryEntryKind.File, 30);
        var rules = new[]
        {
            new SelectionRule("include-pdf", SelectionRuleStage.Include, SelectionRuleKind.Extension, value: ".pdf"),
            new SelectionRule("exclude-restore", SelectionRuleStage.Exclude, SelectionRuleKind.Exact, SelectionField.FileName, "restore.pdf"),
            new SelectionRule("except-restore", SelectionRuleStage.Except, SelectionRuleKind.Exact, SelectionField.FileName, "restore.pdf")
        };

        var result = await new QueryEngine().SelectAsync(
            new SelectionRequest([txt, restore, pdf], rules),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.SelectedEntries.Length);
        Assert.Contains(result.Value.SelectedEntries, item => item.FullPath == pdf.FullPath);
        Assert.Contains(result.Value.SelectedEntries, item => item.FullPath == restore.FullPath);
        Assert.Equal(SelectionDisposition.ReIncludedByExcept, DecisionFor(result.Value, restore).Disposition);
        Assert.Equal(SelectionDisposition.RejectedByInclude, DecisionFor(result.Value, txt).Disposition);
    }

    [Fact]
    public async Task ExceptCannotBypassIncludeBoundary()
    {
        var pdf = Entry("C:/source/keep.pdf", "keep.pdf", DiscoveryEntryKind.File, 10);
        var txt = Entry("C:/source/restore.txt", "restore.txt", DiscoveryEntryKind.File, 10);
        var rules = new[]
        {
            new SelectionRule("include-pdf", SelectionRuleStage.Include, SelectionRuleKind.Extension, value: "pdf"),
            new SelectionRule("exclude-all", SelectionRuleStage.Exclude, SelectionRuleKind.Glob, SelectionField.FileName, "*"),
            new SelectionRule("except-txt", SelectionRuleStage.Except, SelectionRuleKind.Exact, SelectionField.FileName, "restore.txt")
        };

        var result = await new QueryEngine().SelectAsync(
            new SelectionRequest([pdf, txt], rules),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.SelectedEntries);
        Assert.Equal(SelectionDisposition.RejectedByInclude, DecisionFor(result.Value, txt).Disposition);
        Assert.DoesNotContain("except-txt", DecisionFor(result.Value, txt).MatchedRuleIds);
    }

    [Fact]
    public async Task EmptyIncludeMeansAllSourceEntriesAreEligible()
    {
        var first = Entry("C:/source/a.txt", "a.txt", DiscoveryEntryKind.File, 1);
        var second = Entry("C:/source/b.tmp", "b.tmp", DiscoveryEntryKind.File, 1);
        var rules = new[]
        {
            new SelectionRule("exclude-tmp", SelectionRuleStage.Exclude, SelectionRuleKind.Extension, value: ".tmp"),
            new SelectionRule("except-b", SelectionRuleStage.Except, SelectionRuleKind.Exact, SelectionField.FileName, "b.tmp")
        };

        var result = await new QueryEngine().SelectAsync(
            new SelectionRequest([second, first], rules),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.SelectedEntries.Length);
        Assert.Equal(SelectionDisposition.Selected, DecisionFor(result.Value, first).Disposition);
        Assert.Equal(SelectionDisposition.ReIncludedByExcept, DecisionFor(result.Value, second).Disposition);
    }

    [Fact]
    public async Task ExactGlobExtensionKindAndSizeRulesMatchExpectedRecords()
    {
        var file = Entry("C:/source/عيادة/Report.PDF", "عيادة/Report.PDF", DiscoveryEntryKind.File, 2048);
        var directory = Entry("C:/source/عيادة", "عيادة", DiscoveryEntryKind.Directory, null);
        var rules = new[]
        {
            new SelectionRule("include-arabic", SelectionRuleStage.Include, SelectionRuleKind.Glob, SelectionField.RelativePath, "عيادة/*.PDF", casePolicy: SelectionCasePolicy.Ordinal),
            new SelectionRule("exclude-large", SelectionRuleStage.Exclude, SelectionRuleKind.FileSizeRange, minimumBytes: 1024),
            new SelectionRule("except-pdf", SelectionRuleStage.Except, SelectionRuleKind.Extension, value: "PDF", casePolicy: SelectionCasePolicy.Ordinal),
            new SelectionRule("exclude-dir", SelectionRuleStage.Exclude, SelectionRuleKind.EntryKind, entryKind: DiscoveryEntryKind.Directory)
        };

        var result = await new QueryEngine().SelectAsync(
            new SelectionRequest([directory, file], rules),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.SelectedEntries);
        Assert.Equal(file.FullPath, result.Value.SelectedEntries[0].FullPath);
        Assert.Equal(
            ["include-arabic", "exclude-large", "except-pdf"],
            DecisionFor(result.Value, file).MatchedRuleIds);
        Assert.Equal(SelectionDisposition.RejectedByInclude, DecisionFor(result.Value, directory).Disposition);
    }

    [Fact]
    public async Task FileSizeRuleNeverMatchesDirectory()
    {
        var directory = Entry("C:/source/folder", "folder", DiscoveryEntryKind.Directory, null);
        var file = Entry("C:/source/file.bin", "file.bin", DiscoveryEntryKind.File, 500);
        var rules = new[]
        {
            new SelectionRule("exclude-small", SelectionRuleStage.Exclude, SelectionRuleKind.FileSizeRange, maximumBytes: 1000)
        };

        var result = await new QueryEngine().SelectAsync(
            new SelectionRequest([directory, file], rules),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.True(DecisionFor(result.Value, directory).IsSelected);
        Assert.False(DecisionFor(result.Value, file).IsSelected);
    }

    [Fact]
    public async Task CasePolicyIsExplicitAndOrdinal()
    {
        var file = Entry("C:/source/REPORT.PDF", "REPORT.PDF", DiscoveryEntryKind.File, 1);
        var sensitive = new SelectionRule(
            "sensitive",
            SelectionRuleStage.Include,
            SelectionRuleKind.Extension,
            value: "pdf",
            casePolicy: SelectionCasePolicy.Ordinal);
        var insensitive = new SelectionRule(
            "insensitive",
            SelectionRuleStage.Include,
            SelectionRuleKind.Extension,
            value: "pdf",
            casePolicy: SelectionCasePolicy.OrdinalIgnoreCase);

        var first = await new QueryEngine().SelectAsync(
            new SelectionRequest([file], [sensitive]),
            TestContext.Current.CancellationToken);
        var second = await new QueryEngine().SelectAsync(
            new SelectionRequest([file], [insensitive]),
            TestContext.Current.CancellationToken);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Empty(first.Value.SelectedEntries);
        Assert.Single(second.Value.SelectedEntries);
    }

    [Fact]
    public async Task UnsupportedGlobSyntaxFailsClosed()
    {
        var file = Entry("C:/source/a.txt", "a.txt", DiscoveryEntryKind.File, 1);
        var invalid = new SelectionRule(
            "invalid-glob",
            SelectionRuleStage.Include,
            SelectionRuleKind.Glob,
            SelectionField.FileName,
            "*.txt[");

        var result = await new QueryEngine().SelectAsync(
            new SelectionRequest([file], [invalid]),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal("QUERY.INVALID_GLOB", result.Error!.Code.Value);
        Assert.Equal(ErrorCategory.Validation, result.Error.Category);
    }

    [Fact]
    public async Task DuplicateSourceIdentityFailsClosed()
    {
        var first = Entry("C:/source/a.txt", "a.txt", DiscoveryEntryKind.File, 1);
        var duplicate = Entry("C:/source/a.txt", "copy/a.txt", DiscoveryEntryKind.File, 1);

        var result = await new QueryEngine().SelectAsync(
            new SelectionRequest([first, duplicate], []),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal("QUERY.DUPLICATE_SOURCE", result.Error!.Code.Value);
    }

    [Fact]
    public async Task PreCancelledSelectionFailsWithStructuredCancellation()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        var result = await new QueryEngine().SelectAsync(
            new SelectionRequest([Entry("C:/source/a.txt", "a.txt", DiscoveryEntryKind.File, 1)], []),
            source.Token);

        Assert.True(result.IsFailure);
        Assert.Equal("QUERY.CANCELLED", result.Error!.Code.Value);
        Assert.Equal(ErrorCategory.Cancelled, result.Error.Category);
    }

    [Fact]
    public async Task OutputAndMatchedRuleOrderingAreDeterministic()
    {
        var a = Entry("C:/source/a.txt", "a.txt", DiscoveryEntryKind.File, 1);
        var b = Entry("C:/source/b.txt", "b.txt", DiscoveryEntryKind.File, 1);
        var rules = new[]
        {
            new SelectionRule("include-1", SelectionRuleStage.Include, SelectionRuleKind.Glob, SelectionField.FileName, "*.txt"),
            new SelectionRule("include-2", SelectionRuleStage.Include, SelectionRuleKind.Extension, value: ".txt")
        };
        var engine = new QueryEngine();

        var first = await engine.SelectAsync(new SelectionRequest([b, a], rules), TestContext.Current.CancellationToken);
        var second = await engine.SelectAsync(new SelectionRequest([a, b], rules), TestContext.Current.CancellationToken);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(
            first.Value.Decisions.Select(decision => decision.Entry.FullPath),
            second.Value.Decisions.Select(decision => decision.Entry.FullPath));
        Assert.All(first.Value.Decisions, decision => Assert.Equal(["include-1", "include-2"], decision.MatchedRuleIds));
    }

    [Fact]
    public async Task RealWindowsDiscoveryFeedsSelectionWithoutMutation()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = CreateTempRoot();
        try
        {
            var arabicDirectory = Path.Combine(root, "طلبات");
            Directory.CreateDirectory(arabicDirectory);
            var selected = Path.Combine(arabicDirectory, "نموذج.pdf");
            var rejected = Path.Combine(root, "notes.txt");
            await File.WriteAllTextAsync(selected, "pdf-data", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(rejected, "text-data", TestContext.Current.CancellationToken);

            var discovery = await new DiscoveryEngine().DiscoverAsync(
                new DiscoveryRequest([root]),
                TestContext.Current.CancellationToken);
            Assert.True(discovery.IsSuccess);

            var query = await new QueryEngine().SelectAsync(
                new SelectionRequest(
                    discovery.Value.Entries,
                    [new SelectionRule("include-pdf", SelectionRuleStage.Include, SelectionRuleKind.Extension, value: ".pdf")]),
                TestContext.Current.CancellationToken);

            Assert.True(query.IsSuccess);
            Assert.Contains(query.Value.SelectedEntries, entry => PathEquals(entry.FullPath, selected));
            Assert.DoesNotContain(query.Value.SelectedEntries, entry => PathEquals(entry.FullPath, rejected));
            Assert.Equal("pdf-data", await File.ReadAllTextAsync(selected, TestContext.Current.CancellationToken));
            Assert.Equal("text-data", await File.ReadAllTextAsync(rejected, TestContext.Current.CancellationToken));
        }
        finally
        {
            DeleteTree(root);
        }
    }

    [Fact]
    public async Task QualificationUsesRealDiscoveryAndPassesEngineSdkContract()
    {
        var working = CreateTempRoot();
        var evidence = CreateTempRoot();
        try
        {
            var engine = new QueryEngine();
            var result = await engine.QualifyAsync(
                new EngineExecutionContext(OperationId.New(), working, evidence),
                TestContext.Current.CancellationToken);

            Assert.True(result.IsSuccess);
            Assert.True(result.Value.Passed);
            Assert.Equal("query.selection", engine.Descriptor.Id.Value);
            Assert.Contains(engine.Descriptor.Capabilities, capability => capability.Id.Value == "query.include-exclude-except");
        }
        finally
        {
            DeleteTree(working);
            DeleteTree(evidence);
        }
    }

    [Fact]
    public async Task TwentyThousandRecordSelectionMeetsGateBudget()
    {
        var entries = Enumerable.Range(0, 20_000)
            .Select(index => Entry(
                $"C:/source/item-{index:D5}.{(index % 2 == 0 ? "pdf" : "txt")}",
                $"item-{index:D5}.{(index % 2 == 0 ? "pdf" : "txt")}",
                DiscoveryEntryKind.File,
                index))
            .ToArray();
        var rules = new[]
        {
            new SelectionRule("include-docs", SelectionRuleStage.Include, SelectionRuleKind.Glob, SelectionField.FileName, "*.pdf"),
            new SelectionRule("exclude-large", SelectionRuleStage.Exclude, SelectionRuleKind.FileSizeRange, minimumBytes: 15_000)
        };

        var stopwatch = Stopwatch.StartNew();
        var result = await new QueryEngine().SelectAsync(
            new SelectionRequest(entries, rules),
            TestContext.Current.CancellationToken);
        stopwatch.Stop();

        Assert.True(result.IsSuccess);
        Assert.Equal(7_500, result.Value.SelectedEntries.Length);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"Query performance budget exceeded: {stopwatch.Elapsed.TotalMilliseconds:F0} ms for 20,000 records.");
    }

    private static SelectionDecision DecisionFor(SelectionResult result, DiscoveryEntry entry) =>
        Assert.Single(result.Decisions, decision => decision.Entry.FullPath == entry.FullPath);

    private static DiscoveryEntry Entry(
        string fullPath,
        string relativePath,
        DiscoveryEntryKind kind,
        long? length) => new(
            "C:/source",
            fullPath,
            relativePath,
            kind,
            length,
            DateTimeOffset.UnixEpoch,
            kind == DiscoveryEntryKind.Directory ? FileAttributes.Directory : FileAttributes.Normal);

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "UFOps-Query-Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return Path.GetFullPath(path);
    }

    private static void DeleteTree(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static bool PathEquals(string left, string right) =>
        (OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .Equals(Path.GetFullPath(left), Path.GetFullPath(right));
}
