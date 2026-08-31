using System.Collections.Immutable;
using UFOps.Discovery;

namespace UFOps.Query;

public enum SelectionRuleStage
{
    Include,
    Exclude,
    Except
}

public enum SelectionRuleKind
{
    Exact,
    Glob,
    Extension,
    EntryKind,
    FileSizeRange
}

public enum SelectionField
{
    FullPath,
    RelativePath,
    FileName
}

public enum SelectionCasePolicy
{
    Ordinal,
    OrdinalIgnoreCase
}

public enum SelectionDisposition
{
    Selected,
    RejectedByInclude,
    Excluded,
    ReIncludedByExcept
}

public sealed record SelectionRule
{
    public string Id { get; }
    public SelectionRuleStage Stage { get; }
    public SelectionRuleKind Kind { get; }
    public SelectionField? Field { get; }
    public string? Value { get; }
    public DiscoveryEntryKind? EntryKind { get; }
    public long? MinimumBytes { get; }
    public long? MaximumBytes { get; }
    public SelectionCasePolicy CasePolicy { get; }

    public SelectionRule(
        string id,
        SelectionRuleStage stage,
        SelectionRuleKind kind,
        SelectionField? field = null,
        string? value = null,
        DiscoveryEntryKind? entryKind = null,
        long? minimumBytes = null,
        long? maximumBytes = null,
        SelectionCasePolicy casePolicy = SelectionCasePolicy.OrdinalIgnoreCase)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (id.Length > 96 || id.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("Selection rule ID must contain at most 96 non-whitespace characters.", nameof(id));
        }

        ValidateEnums(stage, kind, field, entryKind, casePolicy);
        ValidateShape(kind, field, value, entryKind, minimumBytes, maximumBytes);

        Id = id;
        Stage = stage;
        Kind = kind;
        Field = field;
        Value = value;
        EntryKind = entryKind;
        MinimumBytes = minimumBytes;
        MaximumBytes = maximumBytes;
        CasePolicy = casePolicy;
    }

    private static void ValidateEnums(
        SelectionRuleStage stage,
        SelectionRuleKind kind,
        SelectionField? field,
        DiscoveryEntryKind? entryKind,
        SelectionCasePolicy casePolicy)
    {
        if (!Enum.IsDefined(stage))
        {
            throw new ArgumentOutOfRangeException(nameof(stage), "Selection rule stage is not defined.");
        }

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), "Selection rule kind is not defined.");
        }

        if (field is not null && !Enum.IsDefined(field.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(field), "Selection field is not defined.");
        }

        if (entryKind is not null && !Enum.IsDefined(entryKind.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(entryKind), "Discovery entry kind is not defined.");
        }

        if (!Enum.IsDefined(casePolicy))
        {
            throw new ArgumentOutOfRangeException(nameof(casePolicy), "Selection case policy is not defined.");
        }
    }

    private static void ValidateShape(
        SelectionRuleKind kind,
        SelectionField? field,
        string? value,
        DiscoveryEntryKind? entryKind,
        long? minimumBytes,
        long? maximumBytes)
    {
        switch (kind)
        {
            case SelectionRuleKind.Exact:
            case SelectionRuleKind.Glob:
                if (field is null)
                {
                    throw new ArgumentException("Exact and glob rules require a selection field.", nameof(field));
                }

                ArgumentException.ThrowIfNullOrWhiteSpace(value);
                RejectUnexpected(entryKind, minimumBytes, maximumBytes);
                break;

            case SelectionRuleKind.Extension:
                if (field is not null)
                {
                    throw new ArgumentException("Extension rules do not accept a selection field.", nameof(field));
                }

                ArgumentException.ThrowIfNullOrWhiteSpace(value);
                RejectUnexpected(entryKind, minimumBytes, maximumBytes);
                break;

            case SelectionRuleKind.EntryKind:
                if (entryKind is null)
                {
                    throw new ArgumentException("Entry-kind rules require an entry kind.", nameof(entryKind));
                }

                if (field is not null || value is not null || minimumBytes is not null || maximumBytes is not null)
                {
                    throw new ArgumentException("Entry-kind rules cannot carry unrelated match values.");
                }

                break;

            case SelectionRuleKind.FileSizeRange:
                if (minimumBytes is null && maximumBytes is null)
                {
                    throw new ArgumentException("File-size rules require at least one bound.");
                }

                if (minimumBytes is < 0 || maximumBytes is < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(minimumBytes), "File-size bounds cannot be negative.");
                }

                if (minimumBytes is not null && maximumBytes is not null && minimumBytes > maximumBytes)
                {
                    throw new ArgumentException("Minimum file size cannot exceed maximum file size.");
                }

                if (field is not null || value is not null || entryKind is not null)
                {
                    throw new ArgumentException("File-size rules cannot carry unrelated match values.");
                }

                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    private static void RejectUnexpected(
        DiscoveryEntryKind? entryKind,
        long? minimumBytes,
        long? maximumBytes)
    {
        if (entryKind is not null || minimumBytes is not null || maximumBytes is not null)
        {
            throw new ArgumentException("Text rules cannot carry entry-kind or file-size values.");
        }
    }
}

public sealed class SelectionRequest
{
    public ImmutableArray<DiscoveryEntry> Entries { get; }
    public ImmutableArray<SelectionRule> Rules { get; }

    public SelectionRequest(IEnumerable<DiscoveryEntry> entries, IEnumerable<SelectionRule> rules)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(rules);

        var materializedEntries = entries.ToImmutableArray();
        var materializedRules = rules.ToImmutableArray();
        if (materializedEntries.Any(entry => entry is null))
        {
            throw new ArgumentException("Selection entries cannot contain null values.", nameof(entries));
        }

        if (materializedRules.Any(rule => rule is null))
        {
            throw new ArgumentException("Selection rules cannot contain null values.", nameof(rules));
        }

        if (materializedRules.Select(rule => rule.Id).Distinct(StringComparer.Ordinal).Count() != materializedRules.Length)
        {
            throw new ArgumentException("Selection rule IDs must be unique.", nameof(rules));
        }

        Entries = materializedEntries;
        Rules = materializedRules;
    }
}

public sealed record SelectionDecision
{
    public DiscoveryEntry Entry { get; }
    public bool IsSelected { get; }
    public SelectionDisposition Disposition { get; }
    public ImmutableArray<string> MatchedRuleIds { get; }

    public SelectionDecision(
        DiscoveryEntry entry,
        bool isSelected,
        SelectionDisposition disposition,
        IEnumerable<string> matchedRuleIds)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(matchedRuleIds);
        if (!Enum.IsDefined(disposition))
        {
            throw new ArgumentOutOfRangeException(nameof(disposition), "Selection disposition is not defined.");
        }

        var dispositionIsSelected = disposition is SelectionDisposition.Selected or SelectionDisposition.ReIncludedByExcept;
        if (isSelected != dispositionIsSelected)
        {
            throw new ArgumentException("Selection disposition is inconsistent with the selected flag.", nameof(disposition));
        }

        var materialized = matchedRuleIds.ToImmutableArray();
        if (materialized.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Matched rule IDs cannot contain empty values.", nameof(matchedRuleIds));
        }

        if (materialized.Distinct(StringComparer.Ordinal).Count() != materialized.Length)
        {
            throw new ArgumentException("Matched rule IDs must be unique within a selection decision.", nameof(matchedRuleIds));
        }

        Entry = entry;
        IsSelected = isSelected;
        Disposition = disposition;
        MatchedRuleIds = materialized;
    }
}

public sealed class SelectionResult
{
    public ImmutableArray<SelectionDecision> Decisions { get; }
    public ImmutableArray<DiscoveryEntry> SelectedEntries { get; }

    public SelectionResult(IEnumerable<SelectionDecision> decisions)
    {
        ArgumentNullException.ThrowIfNull(decisions);
        var materialized = decisions.ToImmutableArray();
        if (materialized.Any(decision => decision is null))
        {
            throw new ArgumentException("Selection decisions cannot contain null values.", nameof(decisions));
        }

        Decisions = materialized;
        SelectedEntries = materialized
            .Where(decision => decision.IsSelected)
            .Select(decision => decision.Entry)
            .ToImmutableArray();
    }
}
