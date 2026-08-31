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
        Assert.Equal(value, parsed);
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
