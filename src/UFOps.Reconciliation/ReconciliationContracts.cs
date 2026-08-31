using System.Collections.Immutable;

namespace UFOps.Reconciliation;

public enum ReconciliationCasePolicy
{
    Ordinal,
    OrdinalIgnoreCase
}

public enum ReconciliationUnicodePolicy
{
    None,
    FormC
}

public enum ReconciliationPresence
{
    LeftOnly,
    RightOnly,
    Both
}

public sealed record ReconciliationNormalizationPolicy
{
    public bool TrimWhitespace { get; }
    public ReconciliationUnicodePolicy UnicodePolicy { get; }
    public ReconciliationCasePolicy CasePolicy { get; }

    public ReconciliationNormalizationPolicy(
        bool trimWhitespace,
        ReconciliationUnicodePolicy unicodePolicy,
        ReconciliationCasePolicy casePolicy)
    {
        if (!Enum.IsDefined(unicodePolicy))
        {
            throw new ArgumentOutOfRangeException(nameof(unicodePolicy), "Unicode normalization policy is not defined.");
        }

        if (!Enum.IsDefined(casePolicy))
        {
            throw new ArgumentOutOfRangeException(nameof(casePolicy), "Reconciliation case policy is not defined.");
        }

        TrimWhitespace = trimWhitespace;
        UnicodePolicy = unicodePolicy;
        CasePolicy = casePolicy;
    }

    internal StringComparer CreateComparer() => CasePolicy switch
    {
        ReconciliationCasePolicy.Ordinal => StringComparer.Ordinal,
        ReconciliationCasePolicy.OrdinalIgnoreCase => StringComparer.OrdinalIgnoreCase,
        _ => throw new InvalidOperationException("Unsupported reconciliation case policy.")
    };
}

public sealed record ReconciliationItem
{
    public string ItemId { get; }
    public string Value { get; }

    public ReconciliationItem(string itemId, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        ArgumentNullException.ThrowIfNull(value);
        if (itemId.Length > 128 || itemId.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("Reconciliation item ID must contain at most 128 non-whitespace characters.", nameof(itemId));
        }

        ItemId = itemId;
        Value = value;
    }
}

public sealed class ReconciliationRequest
{
    public string LeftSourceId { get; }
    public string RightSourceId { get; }
    public ImmutableArray<ReconciliationItem> LeftItems { get; }
    public ImmutableArray<ReconciliationItem> RightItems { get; }
    public ReconciliationNormalizationPolicy Normalization { get; }

    public ReconciliationRequest(
        string leftSourceId,
        IEnumerable<ReconciliationItem> leftItems,
        string rightSourceId,
        IEnumerable<ReconciliationItem> rightItems,
        ReconciliationNormalizationPolicy normalization)
    {
        LeftSourceId = ValidateSourceId(leftSourceId, nameof(leftSourceId));
        RightSourceId = ValidateSourceId(rightSourceId, nameof(rightSourceId));
        if (string.Equals(LeftSourceId, RightSourceId, StringComparison.Ordinal))
        {
            throw new ArgumentException("Left and right source identities must be distinct.", nameof(rightSourceId));
        }

        ArgumentNullException.ThrowIfNull(leftItems);
        ArgumentNullException.ThrowIfNull(rightItems);
        ArgumentNullException.ThrowIfNull(normalization);

        LeftItems = MaterializeItems(leftItems, nameof(leftItems));
        RightItems = MaterializeItems(rightItems, nameof(rightItems));
        Normalization = normalization;
    }

    private static ImmutableArray<ReconciliationItem> MaterializeItems(
        IEnumerable<ReconciliationItem> items,
        string parameterName)
    {
        var materialized = items.ToImmutableArray();
        if (materialized.Any(item => item is null))
        {
            throw new ArgumentException("Reconciliation inputs cannot contain null items.", parameterName);
        }

        if (materialized.Select(item => item.ItemId).Distinct(StringComparer.Ordinal).Count() != materialized.Length)
        {
            throw new ArgumentException("Source item IDs must be unique within each source.", parameterName);
        }

        return materialized;
    }

    private static string ValidateSourceId(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > 128 || value.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("Source ID must contain at most 128 non-whitespace characters.", parameterName);
        }

        return value;
    }
}

public sealed record ReconciledItem
{
    public string ItemId { get; }
    public string RawValue { get; }
    public string NormalizedValue { get; }

    public ReconciledItem(string itemId, string rawValue, string normalizedValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        ArgumentNullException.ThrowIfNull(rawValue);
        ArgumentException.ThrowIfNullOrEmpty(normalizedValue);

        ItemId = itemId;
        RawValue = rawValue;
        NormalizedValue = normalizedValue;
    }
}

public sealed class ReconciliationGroup
{
    public string CanonicalKey { get; }
    public ImmutableArray<ReconciledItem> LeftItems { get; }
    public ImmutableArray<ReconciledItem> RightItems { get; }
    public int LeftCount => LeftItems.Length;
    public int RightCount => RightItems.Length;
    public bool HasLeftDuplicates => LeftCount > 1;
    public bool HasRightDuplicates => RightCount > 1;
    public ReconciliationPresence Presence => (LeftCount > 0, RightCount > 0) switch
    {
        (true, true) => ReconciliationPresence.Both,
        (true, false) => ReconciliationPresence.LeftOnly,
        (false, true) => ReconciliationPresence.RightOnly,
        _ => throw new InvalidOperationException("A reconciliation group cannot be empty on both sides.")
    };

    public ReconciliationGroup(
        string canonicalKey,
        IEnumerable<ReconciledItem> leftItems,
        IEnumerable<ReconciledItem> rightItems)
    {
        ArgumentException.ThrowIfNullOrEmpty(canonicalKey);
        ArgumentNullException.ThrowIfNull(leftItems);
        ArgumentNullException.ThrowIfNull(rightItems);

        var materializedLeft = leftItems.ToImmutableArray();
        var materializedRight = rightItems.ToImmutableArray();
        if (materializedLeft.Any(item => item is null) || materializedRight.Any(item => item is null))
        {
            throw new ArgumentException("Reconciliation groups cannot contain null items.");
        }

        if (materializedLeft.IsDefaultOrEmpty && materializedRight.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A reconciliation group must contain at least one item.");
        }

        CanonicalKey = canonicalKey;
        LeftItems = materializedLeft;
        RightItems = materializedRight;
    }
}

public sealed record ReconciliationSummary
{
    public int LeftItemCount { get; }
    public int RightItemCount { get; }
    public int UniqueKeyCount { get; }
    public int BothKeyCount { get; }
    public int LeftOnlyKeyCount { get; }
    public int RightOnlyKeyCount { get; }
    public int LeftDuplicateKeyCount { get; }
    public int RightDuplicateKeyCount { get; }
    public int LeftDuplicateItemExcess { get; }
    public int RightDuplicateItemExcess { get; }

    internal ReconciliationSummary(ImmutableArray<ReconciliationGroup> groups)
    {
        LeftItemCount = groups.Sum(group => group.LeftCount);
        RightItemCount = groups.Sum(group => group.RightCount);
        UniqueKeyCount = groups.Length;
        BothKeyCount = groups.Count(group => group.Presence == ReconciliationPresence.Both);
        LeftOnlyKeyCount = groups.Count(group => group.Presence == ReconciliationPresence.LeftOnly);
        RightOnlyKeyCount = groups.Count(group => group.Presence == ReconciliationPresence.RightOnly);
        LeftDuplicateKeyCount = groups.Count(group => group.HasLeftDuplicates);
        RightDuplicateKeyCount = groups.Count(group => group.HasRightDuplicates);
        LeftDuplicateItemExcess = groups.Sum(group => Math.Max(0, group.LeftCount - 1));
        RightDuplicateItemExcess = groups.Sum(group => Math.Max(0, group.RightCount - 1));
    }
}

public sealed class ReconciliationResult
{
    public string LeftSourceId { get; }
    public string RightSourceId { get; }
    public ReconciliationNormalizationPolicy Normalization { get; }
    public ImmutableArray<ReconciliationGroup> Groups { get; }
    public ReconciliationSummary Summary { get; }

    public ReconciliationResult(
        string leftSourceId,
        string rightSourceId,
        ReconciliationNormalizationPolicy normalization,
        IEnumerable<ReconciliationGroup> groups)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leftSourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(rightSourceId);
        ArgumentNullException.ThrowIfNull(normalization);
        ArgumentNullException.ThrowIfNull(groups);
        if (string.Equals(leftSourceId, rightSourceId, StringComparison.Ordinal))
        {
            throw new ArgumentException("Result source identities must be distinct.", nameof(rightSourceId));
        }

        var materialized = groups.ToImmutableArray();
        if (materialized.Any(group => group is null))
        {
            throw new ArgumentException("Reconciliation results cannot contain null groups.", nameof(groups));
        }

        var comparer = normalization.CreateComparer();
        if (materialized.Select(group => group.CanonicalKey).Distinct(comparer).Count() != materialized.Length)
        {
            throw new ArgumentException("Reconciliation result contains duplicate canonical keys.", nameof(groups));
        }

        LeftSourceId = leftSourceId;
        RightSourceId = rightSourceId;
        Normalization = normalization;
        Groups = materialized;
        Summary = new ReconciliationSummary(materialized);
    }
}
