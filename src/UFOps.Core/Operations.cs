using System.Collections.Immutable;

namespace UFOps.Core;

public enum OperationState
{
    Planned,
    Executing,
    ActionDone,
    Verifying,
    Verified,
    Committed,
    Failed,
    RolledBack,
    NeedsReview
}

public enum ItemOutcome
{
    Succeeded,
    Failed,
    Skipped,
    NeedsReview
}

public sealed record PlannedItem
{
    public string ItemKey { get; }
    public string SourceLocator { get; }
    public string? DestinationLocator { get; }
    public string Action { get; }
    public ImmutableDictionary<string, string> Attributes { get; }

    public PlannedItem(
        string itemKey,
        string sourceLocator,
        string? destinationLocator,
        string action,
        IEnumerable<KeyValuePair<string, string>>? attributes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceLocator);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ItemKey = itemKey;
        SourceLocator = sourceLocator;
        DestinationLocator = destinationLocator;
        Action = action;
        Attributes = attributes?.ToImmutableDictionary(StringComparer.Ordinal) ?? ImmutableDictionary<string, string>.Empty;
    }
}

public sealed class OperationPlan
{
    public OperationId OperationId { get; }
    public int Revision { get; }
    public DateTimeOffset CreatedUtc { get; }
    public ImmutableArray<PlannedItem> Items { get; }

    public OperationPlan(OperationId operationId, int revision, DateTimeOffset createdUtc, IEnumerable<PlannedItem> items)
    {
        if (revision < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(revision));
        }

        ArgumentNullException.ThrowIfNull(items);
        var materialized = items.ToImmutableArray();
        if (materialized.IsDefaultOrEmpty)
        {
            throw new ArgumentException("An operation plan must contain at least one item.", nameof(items));
        }

        if (materialized.Select(item => item.ItemKey).Distinct(StringComparer.Ordinal).Count() != materialized.Length)
        {
            throw new ArgumentException("An operation plan cannot contain duplicate item keys.", nameof(items));
        }

        OperationId = operationId;
        Revision = revision;
        CreatedUtc = createdUtc;
        Items = materialized;
    }
}

public sealed record OperationItemResult
{
    public string ItemKey { get; }
    public ItemOutcome Outcome { get; }
    public UFOpsError? Error { get; }
    public ImmutableDictionary<string, string> Attributes { get; }

    public OperationItemResult(
        string itemKey,
        ItemOutcome outcome,
        UFOpsError? error = null,
        IEnumerable<KeyValuePair<string, string>>? attributes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemKey);
        if (outcome == ItemOutcome.Failed && error is null)
        {
            throw new ArgumentException("A failed item must carry a structured error.", nameof(error));
        }

        if (outcome != ItemOutcome.Failed && error is not null)
        {
            throw new ArgumentException("Only failed items may carry a failure error.", nameof(error));
        }

        ItemKey = itemKey;
        Outcome = outcome;
        Error = error;
        Attributes = attributes?.ToImmutableDictionary(StringComparer.Ordinal) ?? ImmutableDictionary<string, string>.Empty;
    }
}

public sealed class OperationExecutionResult
{
    public OperationId OperationId { get; }
    public DateTimeOffset StartedUtc { get; }
    public DateTimeOffset CompletedUtc { get; }
    public ImmutableArray<OperationItemResult> Items { get; }

    public OperationExecutionResult(
        OperationId operationId,
        DateTimeOffset startedUtc,
        DateTimeOffset completedUtc,
        IEnumerable<OperationItemResult> items)
    {
        if (completedUtc < startedUtc)
        {
            throw new ArgumentException("Completion time cannot precede start time.", nameof(completedUtc));
        }

        ArgumentNullException.ThrowIfNull(items);
        var materialized = items.ToImmutableArray();
        if (materialized.IsDefaultOrEmpty)
        {
            throw new ArgumentException("An execution result must contain at least one item.", nameof(items));
        }

        if (materialized.Select(item => item.ItemKey).Distinct(StringComparer.Ordinal).Count() != materialized.Length)
        {
            throw new ArgumentException("An execution result cannot contain duplicate item keys.", nameof(items));
        }

        OperationId = operationId;
        StartedUtc = startedUtc;
        CompletedUtc = completedUtc;
        Items = materialized;
    }
}
