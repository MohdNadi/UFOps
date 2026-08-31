using UFOps.Discovery;

namespace UFOps.Query.Tests;

public sealed class QueryContractAdversarialTests
{
    [Fact]
    public void UndefinedRuleStageIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SelectionRule(
            "bad-stage",
            (SelectionRuleStage)999,
            SelectionRuleKind.Extension,
            value: ".txt"));
    }

    [Fact]
    public void UndefinedRuleKindIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SelectionRule(
            "bad-kind",
            SelectionRuleStage.Include,
            (SelectionRuleKind)999,
            value: ".txt"));
    }

    [Fact]
    public void UndefinedSelectionFieldIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SelectionRule(
            "bad-field",
            SelectionRuleStage.Include,
            SelectionRuleKind.Exact,
            (SelectionField)999,
            "value"));
    }

    [Fact]
    public void UndefinedDiscoveryEntryKindIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SelectionRule(
            "bad-entry-kind",
            SelectionRuleStage.Include,
            SelectionRuleKind.EntryKind,
            entryKind: (DiscoveryEntryKind)999));
    }

    [Fact]
    public void UndefinedCasePolicyIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SelectionRule(
            "bad-case",
            SelectionRuleStage.Include,
            SelectionRuleKind.Extension,
            value: ".txt",
            casePolicy: (SelectionCasePolicy)999));
    }

    [Fact]
    public void UndefinedDecisionDispositionIsRejected()
    {
        var entry = CreateEntry();
        Assert.Throws<ArgumentOutOfRangeException>(() => new SelectionDecision(
            entry,
            true,
            (SelectionDisposition)999,
            []));
    }

    [Theory]
    [InlineData(true, SelectionDisposition.Excluded)]
    [InlineData(true, SelectionDisposition.RejectedByInclude)]
    [InlineData(false, SelectionDisposition.Selected)]
    [InlineData(false, SelectionDisposition.ReIncludedByExcept)]
    public void DecisionDispositionMustAgreeWithSelectedFlag(
        bool isSelected,
        SelectionDisposition disposition)
    {
        var entry = CreateEntry();
        Assert.Throws<ArgumentException>(() => new SelectionDecision(
            entry,
            isSelected,
            disposition,
            []));
    }

    [Fact]
    public void DuplicateMatchedRuleIdsAreRejected()
    {
        var entry = CreateEntry();
        Assert.Throws<ArgumentException>(() => new SelectionDecision(
            entry,
            true,
            SelectionDisposition.Selected,
            ["rule-a", "rule-a"]));
    }

    private static DiscoveryEntry CreateEntry() => new(
        "C:/source",
        "C:/source/item.txt",
        "item.txt",
        DiscoveryEntryKind.File,
        1,
        DateTimeOffset.UnixEpoch,
        FileAttributes.Normal);
}
