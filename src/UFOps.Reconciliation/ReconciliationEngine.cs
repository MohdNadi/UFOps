using System.Collections.Immutable;
using System.Text;
using UFOps.Core;
using UFOps.EngineSdk;

namespace UFOps.Reconciliation;

public sealed class ReconciliationEngine : IEngine
{
    public EngineDescriptor Descriptor { get; } = new(
        new EngineId("list.reconciliation"),
        "List Reconciliation",
        new Version(1, 0, 0),
        [
            new EngineCapability(
                new CapabilityId("reconciliation.deterministic-grouping"),
                1,
                "Deterministic two-source reconciliation that preserves every source item and duplicate."),
            new EngineCapability(
                new CapabilityId("reconciliation.explicit-normalization"),
                1,
                "Explicit whitespace, Unicode Form C, and ordinal case normalization policy."),
            new EngineCapability(
                new CapabilityId("reconciliation.audit-preservation"),
                1,
                "Per-key source-item preservation with exact presence and duplicate accounting.")
        ]);

    public ValueTask<Result<ReconciliationResult>> ReconcileAsync(
        ReconciliationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromResult(CancelledFailure());
        }

        var comparer = request.Normalization.CreateComparer();
        var buckets = new Dictionary<string, Bucket>(comparer);

        var leftResult = AddItems(
            request.LeftItems,
            request.Normalization,
            buckets,
            isLeft: true,
            cancellationToken);
        if (leftResult is not null)
        {
            return ValueTask.FromResult(leftResult);
        }

        var rightResult = AddItems(
            request.RightItems,
            request.Normalization,
            buckets,
            isLeft: false,
            cancellationToken);
        if (rightResult is not null)
        {
            return ValueTask.FromResult(rightResult);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromResult(CancelledFailure());
        }

        var groups = buckets.Values
            .Select(CreateGroup)
            .OrderBy(group => group.CanonicalKey, comparer)
            .ThenBy(group => group.CanonicalKey, StringComparer.Ordinal)
            .ToImmutableArray();

        return ValueTask.FromResult(Result.Success(new ReconciliationResult(
            request.LeftSourceId,
            request.RightSourceId,
            request.Normalization,
            groups)));
    }

    public ValueTask<Result<EngineQualification>> QualifyAsync(
        EngineExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromResult(Result.Failure<EngineQualification>(new UFOpsError(
                new ErrorCode("RECONCILIATION.CANCELLED"),
                ErrorCategory.Cancelled,
                "Reconciliation qualification was cancelled before it started.")));
        }

        Directory.CreateDirectory(context.WorkingDirectory);
        Directory.CreateDirectory(context.EvidenceDirectory);

        var policy = new ReconciliationNormalizationPolicy(
            trimWhitespace: true,
            ReconciliationUnicodePolicy.FormC,
            ReconciliationCasePolicy.OrdinalIgnoreCase);
        var left = new[]
        {
            new ReconciliationItem("L1", " عيادة "),
            new ReconciliationItem("L2", "MRN-001"),
            new ReconciliationItem("L3", "mrn-001")
        };
        var right = new[]
        {
            new ReconciliationItem("R1", "عيادة"),
            new ReconciliationItem("R2", "MRN-002")
        };
        var request = new ReconciliationRequest("left", left, "right", right, policy);
        var first = ReconcileAsync(request, cancellationToken).Result;
        var reordered = new ReconciliationRequest(
            "left",
            left.Reverse(),
            "right",
            right.Reverse(),
            policy);
        var second = ReconcileAsync(reordered, cancellationToken).Result;

        var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();
        var cancelled = ReconcileAsync(request, cancellationSource.Token).Result;

        var checks = new List<QualificationCheck>
        {
            new(
                "preserve-all-items",
                first.IsSuccess
                    && first.Value.Summary.LeftItemCount == left.Length
                    && first.Value.Summary.RightItemCount == right.Length,
                "Every source item must remain represented in the reconciliation result."),
            new(
                "duplicate-accounting",
                first.IsSuccess
                    && first.Value.Summary.LeftDuplicateKeyCount == 1
                    && first.Value.Summary.LeftDuplicateItemExcess == 1,
                "Duplicate normalized keys must remain visible and counted rather than deduplicated."),
            new(
                "unicode-arabic-normalization",
                first.IsSuccess
                    && first.Value.Groups.Any(group => group.CanonicalKey == "عيادة" && group.Presence == ReconciliationPresence.Both),
                "Arabic values must reconcile deterministically under explicit trimming and Form C normalization."),
            new(
                "deterministic-reordered-input",
                first.IsSuccess && second.IsSuccess && ResultsEquivalent(first.Value, second.Value),
                "Reordering input items must not change canonical groups, item ordering, or summary values."),
            new(
                "structured-cancellation",
                cancelled.IsFailure
                    && cancelled.Error?.Code.Value == "RECONCILIATION.CANCELLED"
                    && cancelled.Error.Category == ErrorCategory.Cancelled,
                "Cancellation must fail closed with a structured cancellation result and no partial success."),
            new(
                "read-only-input",
                left[0].Value == " عيادة " && right[0].Value == "عيادة",
                "Reconciliation must not mutate source item values.")
        };

        return ValueTask.FromResult(Result.Success(new EngineQualification(checks)));
    }

    private static Result<ReconciliationResult>? AddItems(
        ImmutableArray<ReconciliationItem> items,
        ReconciliationNormalizationPolicy policy,
        Dictionary<string, Bucket> buckets,
        bool isLeft,
        CancellationToken cancellationToken)
    {
        foreach (var item in items)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return CancelledFailure();
            }

            var normalized = Normalize(item.Value, policy);
            if (normalized.Length == 0)
            {
                return Result.Failure<ReconciliationResult>(new UFOpsError(
                    new ErrorCode("RECONCILIATION.NORMALIZED_EMPTY"),
                    ErrorCategory.Validation,
                    $"Item '{item.ItemId}' becomes empty under the requested normalization policy."));
            }

            if (!buckets.TryGetValue(normalized, out var bucket))
            {
                bucket = new Bucket();
                buckets.Add(normalized, bucket);
            }

            var reconciled = new ReconciledItem(item.ItemId, item.Value, normalized);
            if (isLeft)
            {
                bucket.Left.Add(reconciled);
            }
            else
            {
                bucket.Right.Add(reconciled);
            }
        }

        return null;
    }

    private static string Normalize(string value, ReconciliationNormalizationPolicy policy)
    {
        var normalized = policy.TrimWhitespace ? value.Trim() : value;
        if (policy.UnicodePolicy == ReconciliationUnicodePolicy.FormC)
        {
            normalized = normalized.Normalize(NormalizationForm.FormC);
        }

        return normalized;
    }

    private static ReconciliationGroup CreateGroup(Bucket bucket)
    {
        var allNormalizedValues = bucket.Left
            .Concat(bucket.Right)
            .Select(item => item.NormalizedValue);
        var canonicalKey = allNormalizedValues.Min(StringComparer.Ordinal)
            ?? throw new InvalidOperationException("A reconciliation bucket cannot be empty.");

        return new ReconciliationGroup(
            canonicalKey,
            SortItems(bucket.Left),
            SortItems(bucket.Right));
    }

    private static ImmutableArray<ReconciledItem> SortItems(IEnumerable<ReconciledItem> items) => items
        .OrderBy(item => item.NormalizedValue, StringComparer.Ordinal)
        .ThenBy(item => item.ItemId, StringComparer.Ordinal)
        .ThenBy(item => item.RawValue, StringComparer.Ordinal)
        .ToImmutableArray();

    private static bool ResultsEquivalent(ReconciliationResult first, ReconciliationResult second)
    {
        if (first.Groups.Length != second.Groups.Length
            || first.Summary != second.Summary)
        {
            return false;
        }

        for (var index = 0; index < first.Groups.Length; index++)
        {
            var left = first.Groups[index];
            var right = second.Groups[index];
            if (!string.Equals(left.CanonicalKey, right.CanonicalKey, StringComparison.Ordinal)
                || left.Presence != right.Presence
                || !ItemsEquivalent(left.LeftItems, right.LeftItems)
                || !ItemsEquivalent(left.RightItems, right.RightItems))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ItemsEquivalent(
        ImmutableArray<ReconciledItem> first,
        ImmutableArray<ReconciledItem> second)
    {
        if (first.Length != second.Length)
        {
            return false;
        }

        for (var index = 0; index < first.Length; index++)
        {
            if (first[index] != second[index])
            {
                return false;
            }
        }

        return true;
    }

    private static Result<ReconciliationResult> CancelledFailure() => Result.Failure<ReconciliationResult>(new UFOpsError(
        new ErrorCode("RECONCILIATION.CANCELLED"),
        ErrorCategory.Cancelled,
        "Reconciliation was cancelled before completion."));

    private sealed class Bucket
    {
        public List<ReconciledItem> Left { get; } = [];
        public List<ReconciledItem> Right { get; } = [];
    }
}
