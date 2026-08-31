using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using UFOps.Core;
using UFOps.Discovery;
using UFOps.EngineSdk;

namespace UFOps.Query;

public sealed class QueryEngine : IEngine
{
    private readonly StringComparer _pathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private readonly IComparer<string> _pathSortComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    public EngineDescriptor Descriptor { get; } = new(
        new EngineId("query.selection"),
        "Query Include Exclude Except Selection",
        new Version(1, 0, 0),
        [
            new EngineCapability(
                new CapabilityId("query.include-exclude-except"),
                1,
                "Deterministic SOURCE -> INCLUDE -> EXCLUDE -> EXCEPT selection semantics."),
            new EngineCapability(
                new CapabilityId("query.glob-exact"),
                1,
                "Exact and bounded glob matching over discovered filesystem records."),
            new EngineCapability(
                new CapabilityId("query.audit-decisions"),
                1,
                "Per-entry selection disposition and matched-rule evidence.")
        ]);

    public ValueTask<Result<SelectionResult>> SelectAsync(
        SelectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromResult(CancelledFailure());
        }

        var duplicate = request.Entries
            .GroupBy(entry => entry.FullPath, _pathComparer)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            return ValueTask.FromResult(Result.Failure<SelectionResult>(new UFOpsError(
                new ErrorCode("QUERY.DUPLICATE_SOURCE"),
                ErrorCategory.Validation,
                $"Selection source contains duplicate path identity: {duplicate.Key}.")));
        }

        var compiledResult = CompileRules(request.Rules);
        if (compiledResult.IsFailure)
        {
            return ValueTask.FromResult(Result.Failure<SelectionResult>(compiledResult.Error!));
        }

        var compiled = compiledResult.Value;
        var includes = compiled.Where(item => item.Rule.Stage == SelectionRuleStage.Include).ToImmutableArray();
        var excludes = compiled.Where(item => item.Rule.Stage == SelectionRuleStage.Exclude).ToImmutableArray();
        var excepts = compiled.Where(item => item.Rule.Stage == SelectionRuleStage.Except).ToImmutableArray();

        var orderedEntries = request.Entries
            .OrderBy(entry => entry.FullPath, _pathSortComparer)
            .ThenBy(entry => entry.FullPath, StringComparer.Ordinal)
            .ToImmutableArray();
        var decisions = ImmutableArray.CreateBuilder<SelectionDecision>(orderedEntries.Length);

        foreach (var entry in orderedEntries)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return ValueTask.FromResult(CancelledFailure());
            }

            var matched = new List<string>();
            var includeMatched = includes.IsDefaultOrEmpty || CollectMatches(entry, includes, matched);
            if (!includeMatched)
            {
                decisions.Add(new SelectionDecision(
                    entry,
                    false,
                    SelectionDisposition.RejectedByInclude,
                    matched));
                continue;
            }

            var excluded = CollectMatches(entry, excludes, matched);
            if (!excluded)
            {
                decisions.Add(new SelectionDecision(
                    entry,
                    true,
                    SelectionDisposition.Selected,
                    matched));
                continue;
            }

            var excepted = CollectMatches(entry, excepts, matched);
            decisions.Add(new SelectionDecision(
                entry,
                excepted,
                excepted ? SelectionDisposition.ReIncludedByExcept : SelectionDisposition.Excluded,
                matched));
        }

        return ValueTask.FromResult(Result.Success(new SelectionResult(decisions)));
    }

    public async ValueTask<Result<EngineQualification>> QualifyAsync(
        EngineExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (cancellationToken.IsCancellationRequested)
        {
            return Result.Failure<EngineQualification>(new UFOpsError(
                new ErrorCode("QUERY.CANCELLED"),
                ErrorCategory.Cancelled,
                "Query qualification was cancelled before it started."));
        }

        Directory.CreateDirectory(context.WorkingDirectory);
        Directory.CreateDirectory(context.EvidenceDirectory);
        var root = Path.Combine(context.WorkingDirectory, $"query-qualification-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var keepDirectory = Path.Combine(root, "عيادة");
            Directory.CreateDirectory(keepDirectory);
            var keep = Path.Combine(keepDirectory, "keep.pdf");
            var restore = Path.Combine(keepDirectory, "restore.pdf");
            var other = Path.Combine(root, "other.txt");
            await File.WriteAllTextAsync(keep, "keep", cancellationToken);
            await File.WriteAllTextAsync(restore, "restore", cancellationToken);
            await File.WriteAllTextAsync(other, "other", cancellationToken);

            var discovery = await new DiscoveryEngine().DiscoverAsync(
                new DiscoveryRequest([root]),
                cancellationToken);
            if (discovery.IsFailure)
            {
                return Result.Failure<EngineQualification>(discovery.Error!);
            }

            var rules = new[]
            {
                new SelectionRule("include-pdf", SelectionRuleStage.Include, SelectionRuleKind.Extension, value: ".pdf"),
                new SelectionRule("exclude-restore", SelectionRuleStage.Exclude, SelectionRuleKind.Exact, SelectionField.FileName, "restore.pdf"),
                new SelectionRule("except-restore", SelectionRuleStage.Except, SelectionRuleKind.Glob, SelectionField.RelativePath, "عيادة/restore.*")
            };
            var first = await SelectAsync(new SelectionRequest(discovery.Value.Entries, rules), cancellationToken);
            var second = await SelectAsync(new SelectionRequest(discovery.Value.Entries, rules), cancellationToken);

            var checks = new List<QualificationCheck>
            {
                new(
                    "real-discovery-integration",
                    first.IsSuccess && first.Value.SelectedEntries.Any(entry => _pathComparer.Equals(entry.FullPath, keep)),
                    "Query qualification must consume real Discovery records from a real filesystem tree."),
                new(
                    "include-exclude-except-precedence",
                    first.IsSuccess && first.Value.SelectedEntries.Any(entry => _pathComparer.Equals(entry.FullPath, restore)),
                    "EXCEPT must re-include an INCLUDE-eligible entry that was removed by EXCLUDE."),
                new(
                    "include-boundary",
                    first.IsSuccess && !first.Value.SelectedEntries.Any(entry => _pathComparer.Equals(entry.FullPath, other)),
                    "EXCEPT must not introduce an entry that failed INCLUDE."),
                new(
                    "deterministic-output",
                    first.IsSuccess && second.IsSuccess && DecisionsEqual(first.Value.Decisions, second.Value.Decisions),
                    "Unchanged source records and rules must produce the same ordered decisions."),
                new(
                    "read-only-input",
                    File.Exists(keep) && File.Exists(restore) && File.Exists(other),
                    "Selection must not mutate discovered filesystem inputs.")
            };

            return Result.Success(new EngineQualification(checks));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static Result<ImmutableArray<CompiledRule>> CompileRules(ImmutableArray<SelectionRule> rules)
    {
        var builder = ImmutableArray.CreateBuilder<CompiledRule>(rules.Length);
        foreach (var rule in rules)
        {
            Regex? regex = null;
            if (rule.Kind == SelectionRuleKind.Glob)
            {
                var regexResult = CompileGlob(rule);
                if (regexResult.IsFailure)
                {
                    return Result.Failure<ImmutableArray<CompiledRule>>(regexResult.Error!);
                }

                regex = regexResult.Value;
            }

            builder.Add(new CompiledRule(rule, regex));
        }

        return Result.Success(builder.MoveToImmutable());
    }

    private static Result<Regex> CompileGlob(SelectionRule rule)
    {
        var value = rule.Value!;
        if (value.IndexOf('\0') >= 0 || value.IndexOfAny(['[', ']', '{', '}']) >= 0)
        {
            return Result.Failure<Regex>(new UFOpsError(
                new ErrorCode("QUERY.INVALID_GLOB"),
                ErrorCategory.Validation,
                $"Rule '{rule.Id}' contains unsupported glob syntax. Supported wildcards are *, **, and ?."));
        }

        var pattern = NormalizeSeparators(value);
        var builder = new StringBuilder("\\A");
        for (var index = 0; index < pattern.Length; index++)
        {
            var character = pattern[index];
            if (character == '*')
            {
                var isDouble = index + 1 < pattern.Length && pattern[index + 1] == '*';
                if (isDouble)
                {
                    builder.Append(".*");
                    index++;
                }
                else
                {
                    builder.Append("[^/]*");
                }
            }
            else if (character == '?')
            {
                builder.Append("[^/]");
            }
            else
            {
                builder.Append(Regex.Escape(character.ToString()));
            }
        }

        builder.Append("\\z");
        var options = RegexOptions.CultureInvariant | RegexOptions.NonBacktracking;
        if (rule.CasePolicy == SelectionCasePolicy.OrdinalIgnoreCase)
        {
            options |= RegexOptions.IgnoreCase;
        }

        return Result.Success(new Regex(builder.ToString(), options, TimeSpan.FromSeconds(1)));
    }

    private static bool CollectMatches(
        DiscoveryEntry entry,
        ImmutableArray<CompiledRule> rules,
        List<string> matched)
    {
        var any = false;
        foreach (var compiled in rules)
        {
            if (Matches(entry, compiled))
            {
                matched.Add(compiled.Rule.Id);
                any = true;
            }
        }

        return any;
    }

    private static bool Matches(DiscoveryEntry entry, CompiledRule compiled)
    {
        var rule = compiled.Rule;
        return rule.Kind switch
        {
            SelectionRuleKind.Exact => TextEquals(GetField(entry, rule.Field!.Value), rule.Value!, rule.CasePolicy),
            SelectionRuleKind.Glob => compiled.Glob!.IsMatch(NormalizeSeparators(GetField(entry, rule.Field!.Value))),
            SelectionRuleKind.Extension => TextEquals(NormalizeExtension(Path.GetExtension(entry.FullPath)), NormalizeExtension(rule.Value!), rule.CasePolicy),
            SelectionRuleKind.EntryKind => entry.Kind == rule.EntryKind,
            SelectionRuleKind.FileSizeRange => MatchesSize(entry, rule.MinimumBytes, rule.MaximumBytes),
            _ => false
        };
    }

    private static string GetField(DiscoveryEntry entry, SelectionField field) => field switch
    {
        SelectionField.FullPath => entry.FullPath,
        SelectionField.RelativePath => entry.RelativePath,
        SelectionField.FileName => Path.GetFileName(entry.FullPath),
        _ => throw new ArgumentOutOfRangeException(nameof(field))
    };

    private static bool TextEquals(string left, string right, SelectionCasePolicy policy)
    {
        if (left.IndexOfAny(['\\', '/']) >= 0 || right.IndexOfAny(['\\', '/']) >= 0)
        {
            left = NormalizeSeparators(left);
            right = NormalizeSeparators(right);
        }

        return string.Equals(
            left,
            right,
            policy == SelectionCasePolicy.Ordinal ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeSeparators(string value) => value.Replace('\\', '/');

    private static string NormalizeExtension(string value) => value.StartsWith('.', StringComparison.Ordinal)
        ? value[1..]
        : value;

    private static bool MatchesSize(DiscoveryEntry entry, long? minimum, long? maximum)
    {
        if (entry.Kind != DiscoveryEntryKind.File || entry.LengthBytes is null)
        {
            return false;
        }

        var length = entry.LengthBytes.Value;
        return (minimum is null || length >= minimum.Value)
            && (maximum is null || length <= maximum.Value);
    }

    private static bool DecisionsEqual(
        ImmutableArray<SelectionDecision> first,
        ImmutableArray<SelectionDecision> second)
    {
        if (first.Length != second.Length)
        {
            return false;
        }

        for (var index = 0; index < first.Length; index++)
        {
            if (!string.Equals(first[index].Entry.FullPath, second[index].Entry.FullPath, StringComparison.Ordinal)
                || first[index].IsSelected != second[index].IsSelected
                || first[index].Disposition != second[index].Disposition
                || !first[index].MatchedRuleIds.SequenceEqual(second[index].MatchedRuleIds, StringComparer.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static Result<SelectionResult> CancelledFailure() => Result.Failure<SelectionResult>(new UFOpsError(
        new ErrorCode("QUERY.CANCELLED"),
        ErrorCategory.Cancelled,
        "Selection was cancelled before completion."));

    private sealed record CompiledRule(SelectionRule Rule, Regex? Glob);
}
