using System.Diagnostics;
using UFOps.Core;
using UFOps.EngineSdk;

namespace UFOps.Reconciliation.Tests;

public sealed class ReconciliationEngineTests
{
    [Fact]
    public async Task PresenceCountsAndDuplicatesAreDerivedFromPreservedItems()
    {
        var request = Request(
            [
                new ReconciliationItem("L1", "A"),
                new ReconciliationItem("L2", "A"),
                new ReconciliationItem("L3", "B")
            ],
            [
                new ReconciliationItem("R1", "A"),
                new ReconciliationItem("R2", "C"),
                new ReconciliationItem("R3", "C")
            ],
            Policy());

        var result = await new ReconciliationEngine().ReconcileAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Groups.Length);
        Assert.Equal(3, result.Value.Summary.LeftItemCount);
        Assert.Equal(3, result.Value.Summary.RightItemCount);
        Assert.Equal(1, result.Value.Summary.BothKeyCount);
        Assert.Equal(1, result.Value.Summary.LeftOnlyKeyCount);
        Assert.Equal(1, result.Value.Summary.RightOnlyKeyCount);
        Assert.Equal(1, result.Value.Summary.LeftDuplicateKeyCount);
        Assert.Equal(1, result.Value.Summary.RightDuplicateKeyCount);
        Assert.Equal(1, result.Value.Summary.LeftDuplicateItemExcess);
        Assert.Equal(1, result.Value.Summary.RightDuplicateItemExcess);
        Assert.Equal(ReconciliationPresence.Both, Group(result.Value, "A").Presence);
        Assert.Equal(ReconciliationPresence.LeftOnly, Group(result.Value, "B").Presence);
        Assert.Equal(ReconciliationPresence.RightOnly, Group(result.Value, "C").Presence);
    }

    [Fact]
    public async Task NormalizationCollisionsPreserveEveryRawVariantAndIdentity()
    {
        var result = await new ReconciliationEngine().ReconcileAsync(
            Request(
                [
                    new ReconciliationItem("L1", " ABC "),
                    new ReconciliationItem("L2", "abc"),
                    new ReconciliationItem("L3", "AbC")
                ],
                [new ReconciliationItem("R1", "aBc")],
                Policy(trim: true, casePolicy: ReconciliationCasePolicy.OrdinalIgnoreCase)),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var group = Assert.Single(result.Value.Groups);
        Assert.Equal(3, group.LeftCount);
        Assert.Equal(1, group.RightCount);
        Assert.Equal(["L1", "L3", "L2"], group.LeftItems.Select(item => item.ItemId));
        Assert.Contains(group.LeftItems, item => item.RawValue == " ABC ");
        Assert.Contains(group.LeftItems, item => item.RawValue == "abc");
        Assert.Contains(group.LeftItems, item => item.RawValue == "AbC");
    }

    [Fact]
    public async Task FormCCombinesComposedAndDecomposedUnicodeWhileNoneKeepsThemDistinct()
    {
        const string composed = "é";
        const string decomposed = "e\u0301";
        var engine = new ReconciliationEngine();

        var none = await engine.ReconcileAsync(
            Request(
                [new ReconciliationItem("L1", composed)],
                [new ReconciliationItem("R1", decomposed)],
                Policy(unicodePolicy: ReconciliationUnicodePolicy.None)),
            TestContext.Current.CancellationToken);
        var formC = await engine.ReconcileAsync(
            Request(
                [new ReconciliationItem("L1", composed)],
                [new ReconciliationItem("R1", decomposed)],
                Policy(unicodePolicy: ReconciliationUnicodePolicy.FormC)),
            TestContext.Current.CancellationToken);

        Assert.True(none.IsSuccess);
        Assert.True(formC.IsSuccess);
        Assert.Equal(2, none.Value.Groups.Length);
        Assert.Single(formC.Value.Groups);
        Assert.Equal(ReconciliationPresence.Both, formC.Value.Groups[0].Presence);
    }

    [Fact]
    public async Task OrdinalAndOrdinalIgnoreCaseRemainExplicitlyDifferent()
    {
        var engine = new ReconciliationEngine();
        var ordinal = await engine.ReconcileAsync(
            Request(
                [new ReconciliationItem("L1", "ABC")],
                [new ReconciliationItem("R1", "abc")],
                Policy(casePolicy: ReconciliationCasePolicy.Ordinal)),
            TestContext.Current.CancellationToken);
        var ignoreCase = await engine.ReconcileAsync(
            Request(
                [new ReconciliationItem("L1", "ABC")],
                [new ReconciliationItem("R1", "abc")],
                Policy(casePolicy: ReconciliationCasePolicy.OrdinalIgnoreCase)),
            TestContext.Current.CancellationToken);

        Assert.True(ordinal.IsSuccess);
        Assert.True(ignoreCase.IsSuccess);
        Assert.Equal(2, ordinal.Value.Groups.Length);
        Assert.Single(ignoreCase.Value.Groups);
        Assert.Equal("ABC", ignoreCase.Value.Groups[0].CanonicalKey);
    }

    [Fact]
    public async Task TrimmedEmptyValueFailsClosedWithoutPartialSuccess()
    {
        var result = await new ReconciliationEngine().ReconcileAsync(
            Request(
                [new ReconciliationItem("L1", "valid"), new ReconciliationItem("L2", "   ")],
                [],
                Policy(trim: true)),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal("RECONCILIATION.NORMALIZED_EMPTY", result.Error!.Code.Value);
        Assert.Equal(ErrorCategory.Validation, result.Error.Category);
    }

    [Fact]
    public async Task PreCancelledRequestReturnsStructuredCancellation()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();

        var result = await new ReconciliationEngine().ReconcileAsync(
            Request([new ReconciliationItem("L1", "A")], [], Policy()),
            source.Token);

        Assert.True(result.IsFailure);
        Assert.Equal("RECONCILIATION.CANCELLED", result.Error!.Code.Value);
        Assert.Equal(ErrorCategory.Cancelled, result.Error.Category);
    }

    [Fact]
    public async Task ReorderedInputsProduceTheSameCanonicalResultAndItemOrder()
    {
        var left = new[]
        {
            new ReconciliationItem("L3", "beta"),
            new ReconciliationItem("L1", "ALPHA"),
            new ReconciliationItem("L2", "alpha")
        };
        var right = new[]
        {
            new ReconciliationItem("R2", "gamma"),
            new ReconciliationItem("R1", "Alpha")
        };
        var engine = new ReconciliationEngine();
        var policy = Policy(casePolicy: ReconciliationCasePolicy.OrdinalIgnoreCase);

        var first = await engine.ReconcileAsync(
            Request(left, right, policy),
            TestContext.Current.CancellationToken);
        var second = await engine.ReconcileAsync(
            Request(left.Reverse(), right.Reverse(), policy),
            TestContext.Current.CancellationToken);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(
            first.Value.Groups.Select(group => group.CanonicalKey),
            second.Value.Groups.Select(group => group.CanonicalKey));
        Assert.Equal(
            first.Value.Groups.SelectMany(group => group.LeftItems).Select(item => item.ItemId),
            second.Value.Groups.SelectMany(group => group.LeftItems).Select(item => item.ItemId));
        Assert.Equal(
            first.Value.Groups.SelectMany(group => group.RightItems).Select(item => item.ItemId),
            second.Value.Groups.SelectMany(group => group.RightItems).Select(item => item.ItemId));
        Assert.Equal(first.Value.Summary, second.Value.Summary);
    }

    [Fact]
    public async Task RealWindowsTemporaryListFilesFeedTypedContractWithArabicUnicode()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = CreateTempRoot();
        try
        {
            var leftPath = Path.Combine(root, "left.txt");
            var rightPath = Path.Combine(root, "right.txt");
            await File.WriteAllLinesAsync(
                leftPath,
                ["MRN-001", " عيادة ", "مريض"],
                TestContext.Current.CancellationToken);
            await File.WriteAllLinesAsync(
                rightPath,
                ["mrn-001", "عيادة", "آخر"],
                TestContext.Current.CancellationToken);

            var leftLines = await File.ReadAllLinesAsync(leftPath, TestContext.Current.CancellationToken);
            var rightLines = await File.ReadAllLinesAsync(rightPath, TestContext.Current.CancellationToken);
            var leftItems = leftLines.Select((value, index) => new ReconciliationItem($"L{index:D4}", value));
            var rightItems = rightLines.Select((value, index) => new ReconciliationItem($"R{index:D4}", value));

            var result = await new ReconciliationEngine().ReconcileAsync(
                new ReconciliationRequest(
                    "left-file",
                    leftItems,
                    "right-file",
                    rightItems,
                    Policy(trim: true, unicodePolicy: ReconciliationUnicodePolicy.FormC, casePolicy: ReconciliationCasePolicy.OrdinalIgnoreCase)),
                TestContext.Current.CancellationToken);

            Assert.True(result.IsSuccess);
            Assert.Equal(4, result.Value.Groups.Length);
            Assert.Equal(2, result.Value.Summary.BothKeyCount);
            Assert.Equal(3, result.Value.Summary.LeftItemCount);
            Assert.Equal(3, result.Value.Summary.RightItemCount);
            Assert.Contains(result.Value.Groups, group => group.CanonicalKey == "عيادة" && group.Presence == ReconciliationPresence.Both);
            Assert.Equal(["MRN-001", " عيادة ", "مريض"], await File.ReadAllLinesAsync(leftPath, TestContext.Current.CancellationToken));
            Assert.Equal(["mrn-001", "عيادة", "آخر"], await File.ReadAllLinesAsync(rightPath, TestContext.Current.CancellationToken));
        }
        finally
        {
            DeleteTree(root);
        }
    }

    [Fact]
    public async Task QualificationPassesEngineSdkContract()
    {
        var working = CreateTempRoot();
        var evidence = CreateTempRoot();
        try
        {
            var engine = new ReconciliationEngine();
            var result = await engine.QualifyAsync(
                new EngineExecutionContext(OperationId.New(), working, evidence),
                TestContext.Current.CancellationToken);

            Assert.True(result.IsSuccess);
            Assert.True(result.Value.Passed);
            Assert.Equal("list.reconciliation", engine.Descriptor.Id.Value);
            Assert.Contains(
                engine.Descriptor.Capabilities,
                capability => capability.Id.Value == "reconciliation.deterministic-grouping");
        }
        finally
        {
            DeleteTree(working);
            DeleteTree(evidence);
        }
    }

    [Fact]
    public async Task OneHundredThousandItemsMeetPerformanceGateWithoutDataLoss()
    {
        const int countPerSide = 50_000;
        const int uniqueKeys = 40_000;
        var left = Enumerable.Range(0, countPerSide)
            .Select(index => new ReconciliationItem($"L-{index:D5}", $"KEY-{index % uniqueKeys:D5}"))
            .ToArray();
        var right = Enumerable.Range(0, countPerSide)
            .Select(index => new ReconciliationItem($"R-{index:D5}", $"key-{index % uniqueKeys:D5}"))
            .ToArray();

        var stopwatch = Stopwatch.StartNew();
        var result = await new ReconciliationEngine().ReconcileAsync(
            Request(
                left,
                right,
                Policy(casePolicy: ReconciliationCasePolicy.OrdinalIgnoreCase)),
            TestContext.Current.CancellationToken);
        stopwatch.Stop();

        Assert.True(result.IsSuccess);
        Assert.Equal(countPerSide, result.Value.Summary.LeftItemCount);
        Assert.Equal(countPerSide, result.Value.Summary.RightItemCount);
        Assert.Equal(uniqueKeys, result.Value.Summary.UniqueKeyCount);
        Assert.Equal(uniqueKeys, result.Value.Summary.BothKeyCount);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(10),
            $"Reconciliation performance budget exceeded: {stopwatch.Elapsed.TotalMilliseconds:F0} ms for 100,000 items.");
    }

    private static ReconciliationRequest Request(
        IEnumerable<ReconciliationItem> left,
        IEnumerable<ReconciliationItem> right,
        ReconciliationNormalizationPolicy policy) => new("left", left, "right", right, policy);

    private static ReconciliationNormalizationPolicy Policy(
        bool trim = false,
        ReconciliationUnicodePolicy unicodePolicy = ReconciliationUnicodePolicy.None,
        ReconciliationCasePolicy casePolicy = ReconciliationCasePolicy.Ordinal) =>
        new(trim, unicodePolicy, casePolicy);

    private static ReconciliationGroup Group(ReconciliationResult result, string key) =>
        Assert.Single(result.Groups, group => group.CanonicalKey == key);

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "UFOps-Reconciliation-Tests", Guid.NewGuid().ToString("N"));
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
}
