using UFOps.Core;

namespace UFOps.Foundation.Tests;

public sealed class CoreContractTests
{
    [Fact]
    public void OperationIdRejectsEmptyGuid()
    {
        Assert.Throws<ArgumentException>(() => OperationId.FromGuid(Guid.Empty));
    }

    [Fact]
    public void OperationIdRoundTrips()
    {
        var value = OperationId.New();
        Assert.True(OperationId.TryParse(value.ToString(), out var parsed));
        Assert.NotNull(parsed);
        Assert.Equal(value, parsed);
    }

    [Theory]
    [InlineData("FOUNDATION..FAILURE")]
    [InlineData("FOUNDATION.1FAILURE")]
    [InlineData("foundation.FAILURE")]
    [InlineData("FOUNDATION.FAILURE.")]
    public void ErrorCodeRejectsMalformedDottedIdentifiers(string value)
    {
        Assert.Throws<ArgumentException>(() => new ErrorCode(value));
    }

    [Fact]
    public void EngineDescriptorRejectsDuplicateCapabilities()
    {
        var capability = new EngineCapability(new CapabilityId("foundation.health"), 1, "Foundation health qualification.");
        Assert.Throws<ArgumentException>(() => new EngineDescriptor(
            new EngineId("foundation.engine"),
            "Foundation",
            new Version(1, 0, 0),
            [capability, capability]));
    }

    [Fact]
    public void OperationPlanRejectsDuplicateItemKeys()
    {
        var item = new PlannedItem("item-1", "file:///one", null, "inspect");
        Assert.Throws<ArgumentException>(() => new OperationPlan(OperationId.New(), 1, DateTimeOffset.UtcNow, [item, item]));
    }

    [Fact]
    public void OperationPlanRejectsNonUtcTimestamp()
    {
        var item = new PlannedItem("item-1", "file:///one", null, "inspect");
        var nonUtc = new DateTimeOffset(2026, 8, 31, 4, 0, 0, TimeSpan.FromHours(3));
        Assert.Throws<ArgumentException>(() => new OperationPlan(OperationId.New(), 1, nonUtc, [item]));
    }

    [Fact]
    public void OperationPlanFingerprintIsStableAcrossAttributeInsertionOrder()
    {
        var operationId = OperationId.New();
        var created = DateTimeOffset.UtcNow;
        var first = new PlannedItem(
            "item-1",
            "file:///one",
            "file:///two",
            "copy",
            new Dictionary<string, string> { ["b"] = "2", ["a"] = "1" });
        var second = new PlannedItem(
            "item-1",
            "file:///one",
            "file:///two",
            "copy",
            new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" });

        var planA = new OperationPlan(operationId, 1, created, [first]);
        var planB = new OperationPlan(operationId, 1, created, [second]);

        Assert.Equal(planA.Binding.PlanFingerprint, planB.Binding.PlanFingerprint);
    }

    [Fact]
    public void OperationPlanFingerprintChangesWhenPlanContentChanges()
    {
        var operationId = OperationId.New();
        var created = DateTimeOffset.UtcNow;
        var planA = new OperationPlan(operationId, 1, created, [new PlannedItem("item-1", "file:///one", "file:///two", "copy")]);
        var planB = new OperationPlan(operationId, 1, created, [new PlannedItem("item-1", "file:///one", "file:///three", "copy")]);

        Assert.NotEqual(planA.Binding.PlanFingerprint, planB.Binding.PlanFingerprint);
    }

    [Fact]
    public void ExecutionResultCarriesExactPlanBinding()
    {
        var now = DateTimeOffset.UtcNow;
        var plan = new OperationPlan(OperationId.New(), 3, now, [new PlannedItem("item-1", "file:///one", null, "inspect")]);
        var result = new OperationExecutionResult(
            plan.Binding,
            now,
            now,
            [new OperationItemResult("item-1", ItemOutcome.Succeeded)]);

        Assert.Equal(plan.Binding, result.Binding);
        Assert.Equal(3, result.Binding.PlanRevision);
        Assert.Equal(plan.Binding.PlanFingerprint, result.Binding.PlanFingerprint);
    }

    [Fact]
    public void OperationStateMachineRejectsUnsafeSkipFromExecutingToCommitted()
    {
        Assert.False(OperationStateMachine.CanTransition(OperationState.Executing, OperationState.Committed));
        Assert.Throws<InvalidOperationException>(() => OperationStateMachine.EnsureTransition(OperationState.Executing, OperationState.Committed));
    }

    [Fact]
    public void OperationStateMachineAllowsNormalVerifiedCommitPath()
    {
        Assert.True(OperationStateMachine.CanTransition(OperationState.Planned, OperationState.Executing));
        Assert.True(OperationStateMachine.CanTransition(OperationState.Executing, OperationState.ActionDone));
        Assert.True(OperationStateMachine.CanTransition(OperationState.ActionDone, OperationState.Verifying));
        Assert.True(OperationStateMachine.CanTransition(OperationState.Verifying, OperationState.Verified));
        Assert.True(OperationStateMachine.CanTransition(OperationState.Verified, OperationState.Committed));
    }

    [Fact]
    public void FailedItemRequiresStructuredError()
    {
        Assert.Throws<ArgumentException>(() => new OperationItemResult("item-1", ItemOutcome.Failed));
    }

    [Fact]
    public void FailureResultDoesNotExposeValue()
    {
        var error = new UFOpsError(new ErrorCode("FOUNDATION.TEST_FAILURE"), ErrorCategory.Validation, "Expected test failure.");
        var result = Result.Failure<string>(error);
        Assert.True(result.IsFailure);
        Assert.Same(error, result.Error);
        Assert.Throws<InvalidOperationException>(() => result.Value);
    }
}
