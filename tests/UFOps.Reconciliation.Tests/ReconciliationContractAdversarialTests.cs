namespace UFOps.Reconciliation.Tests;

public sealed class ReconciliationContractAdversarialTests
{
    [Fact]
    public void SameSourceIdentityIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new ReconciliationRequest(
            "same",
            [],
            "same",
            [],
            Policy()));
    }

    [Fact]
    public void DuplicateItemIdsWithinLeftSourceAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new ReconciliationRequest(
            "left",
            [new ReconciliationItem("A", "one"), new ReconciliationItem("A", "two")],
            "right",
            [],
            Policy()));
    }

    [Fact]
    public void DuplicateItemIdsWithinRightSourceAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new ReconciliationRequest(
            "left",
            [],
            "right",
            [new ReconciliationItem("A", "one"), new ReconciliationItem("A", "two")],
            Policy()));
    }

    [Fact]
    public void UndefinedUnicodePolicyIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReconciliationNormalizationPolicy(
            false,
            (ReconciliationUnicodePolicy)999,
            ReconciliationCasePolicy.Ordinal));
    }

    [Fact]
    public void UndefinedCasePolicyIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReconciliationNormalizationPolicy(
            false,
            ReconciliationUnicodePolicy.None,
            (ReconciliationCasePolicy)999));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("has space")]
    public void InvalidItemIdsAreRejected(string itemId)
    {
        Assert.Throws<ArgumentException>(() => new ReconciliationItem(itemId, "value"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("has space")]
    public void InvalidSourceIdsAreRejected(string sourceId)
    {
        Assert.Throws<ArgumentException>(() => new ReconciliationRequest(
            sourceId,
            [],
            "right",
            [],
            Policy()));
    }

    [Fact]
    public void EmptyGroupIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new ReconciliationGroup("key", [], []));
    }

    [Fact]
    public void DuplicateCanonicalKeysInResultAreRejectedUnderCasePolicy()
    {
        var policy = new ReconciliationNormalizationPolicy(
            false,
            ReconciliationUnicodePolicy.None,
            ReconciliationCasePolicy.OrdinalIgnoreCase);
        var first = new ReconciliationGroup(
            "ABC",
            [new ReconciledItem("L1", "ABC", "ABC")],
            []);
        var second = new ReconciliationGroup(
            "abc",
            [],
            [new ReconciledItem("R1", "abc", "abc")]);

        Assert.Throws<ArgumentException>(() => new ReconciliationResult(
            "left",
            "right",
            policy,
            [first, second]));
    }

    private static ReconciliationNormalizationPolicy Policy() => new(
        false,
        ReconciliationUnicodePolicy.None,
        ReconciliationCasePolicy.Ordinal);
}
