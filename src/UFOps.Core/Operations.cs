using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

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

public static class OperationStateMachine
{
    public static bool CanTransition(OperationState current, OperationState next) => current switch
    {
        OperationState.Planned => next is OperationState.Executing or OperationState.Failed or OperationState.NeedsReview,
        OperationState.Executing => next is OperationState.ActionDone or OperationState.Failed or OperationState.NeedsReview,
        OperationState.ActionDone => next is OperationState.Verifying or OperationState.Failed or OperationState.NeedsReview,
        OperationState.Verifying => next is OperationState.Verified or OperationState.Failed or OperationState.NeedsReview,
        OperationState.Verified => next is OperationState.Committed or OperationState.Failed or OperationState.NeedsReview,
        OperationState.Failed => next is OperationState.RolledBack or OperationState.NeedsReview,
        OperationState.Committed or OperationState.RolledBack or OperationState.NeedsReview => false,
        _ => false
    };

    public static void EnsureTransition(OperationState current, OperationState next)
    {
        if (!CanTransition(current, next))
        {
            throw new InvalidOperationException($"Invalid operation-state transition: {current} -> {next}.");
        }
    }
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
        if (destinationLocator is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(destinationLocator);
        }

        ItemKey = itemKey;
        SourceLocator = sourceLocator;
        DestinationLocator = destinationLocator;
        Action = action;
        Attributes = OperationAttributeMap.Materialize(attributes, nameof(attributes));
    }
}

public sealed record OperationBinding
{
    public OperationId OperationId { get; }
    public int PlanRevision { get; }
    public OperationPlanFingerprint PlanFingerprint { get; }

    public OperationBinding(OperationId operationId, int planRevision, OperationPlanFingerprint planFingerprint)
    {
        ArgumentNullException.ThrowIfNull(operationId);
        ArgumentOutOfRangeException.ThrowIfLessThan(planRevision, 1);
        ArgumentNullException.ThrowIfNull(planFingerprint);
        OperationId = operationId;
        PlanRevision = planRevision;
        PlanFingerprint = planFingerprint;
    }
}

public sealed class OperationPlan
{
    public OperationId OperationId { get; }
    public int Revision { get; }
    public DateTimeOffset CreatedUtc { get; }
    public ImmutableArray<PlannedItem> Items { get; }
    public OperationBinding Binding { get; }

    public OperationPlan(OperationId operationId, int revision, DateTimeOffset createdUtc, IEnumerable<PlannedItem> items)
    {
        ArgumentNullException.ThrowIfNull(operationId);
        ArgumentOutOfRangeException.ThrowIfLessThan(revision, 1);
        EnsureUtc(createdUtc, nameof(createdUtc));
        ArgumentNullException.ThrowIfNull(items);
        var materialized = items.ToImmutableArray();
        if (materialized.IsDefaultOrEmpty)
        {
            throw new ArgumentException("An operation plan must contain at least one item.", nameof(items));
        }

        if (materialized.Any(item => item is null))
        {
            throw new ArgumentException("An operation plan cannot contain null items.", nameof(items));
        }

        if (materialized.Select(item => item.ItemKey).Distinct(StringComparer.Ordinal).Count() != materialized.Length)
        {
            throw new ArgumentException("An operation plan cannot contain duplicate item keys.", nameof(items));
        }

        OperationId = operationId;
        Revision = revision;
        CreatedUtc = createdUtc;
        Items = materialized;
        Binding = new OperationBinding(operationId, revision, ComputeFingerprint(operationId, revision, createdUtc, materialized));
    }

    private static OperationPlanFingerprint ComputeFingerprint(
        OperationId operationId,
        int revision,
        DateTimeOffset createdUtc,
        ImmutableArray<PlannedItem> items)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "UFOPS-OPERATION-PLAN-V1");
        Append(hash, operationId.ToString());
        Append(hash, revision.ToString(CultureInfo.InvariantCulture));
        Append(hash, createdUtc.ToString("O", CultureInfo.InvariantCulture));
        Append(hash, items.Length.ToString(CultureInfo.InvariantCulture));

        foreach (var item in items)
        {
            Append(hash, item.ItemKey);
            Append(hash, item.SourceLocator);
            Append(hash, item.DestinationLocator);
            Append(hash, item.Action);
            Append(hash, item.Attributes.Count.ToString(CultureInfo.InvariantCulture));
            foreach (var pair in item.Attributes.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                Append(hash, pair.Key);
                Append(hash, pair.Value);
            }
        }

        return new OperationPlanFingerprint(Convert.ToHexString(hash.GetHashAndReset()));
    }

    private static void Append(IncrementalHash hash, string? value)
    {
        Span<byte> length = stackalloc byte[4];
        if (value is null)
        {
            BinaryPrimitives.WriteInt32BigEndian(length, -1);
            hash.AppendData(length);
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(value);
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    internal static void EnsureUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("UTC timestamps must have a zero offset.", parameterName);
        }
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
        Attributes = OperationAttributeMap.Materialize(attributes, nameof(attributes));
    }
}

public sealed class OperationExecutionResult
{
    public OperationBinding Binding { get; }
    public OperationId OperationId => Binding.OperationId;
    public DateTimeOffset StartedUtc { get; }
    public DateTimeOffset CompletedUtc { get; }
    public ImmutableArray<OperationItemResult> Items { get; }

    public OperationExecutionResult(
        OperationBinding binding,
        DateTimeOffset startedUtc,
        DateTimeOffset completedUtc,
        IEnumerable<OperationItemResult> items)
    {
        ArgumentNullException.ThrowIfNull(binding);
        OperationPlan.EnsureUtc(startedUtc, nameof(startedUtc));
        OperationPlan.EnsureUtc(completedUtc, nameof(completedUtc));
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

        if (materialized.Any(item => item is null))
        {
            throw new ArgumentException("An execution result cannot contain null items.", nameof(items));
        }

        if (materialized.Select(item => item.ItemKey).Distinct(StringComparer.Ordinal).Count() != materialized.Length)
        {
            throw new ArgumentException("An execution result cannot contain duplicate item keys.", nameof(items));
        }

        Binding = binding;
        StartedUtc = startedUtc;
        CompletedUtc = completedUtc;
        Items = materialized;
    }
}

internal static class OperationAttributeMap
{
    internal static ImmutableDictionary<string, string> Materialize(
        IEnumerable<KeyValuePair<string, string>>? attributes,
        string parameterName)
    {
        if (attributes is null)
        {
            return ImmutableDictionary<string, string>.Empty;
        }

        var builder = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        foreach (var pair in attributes)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pair.Key, parameterName);
            ArgumentNullException.ThrowIfNull(pair.Value, parameterName);
            if (!builder.TryAdd(pair.Key, pair.Value))
            {
                throw new ArgumentException($"Duplicate attribute key: {pair.Key}.", parameterName);
            }
        }

        return builder.ToImmutable();
    }
}
