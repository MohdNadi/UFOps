# FREEZE-02 — Query / Include / Exclude / Except Engine

Status: CANDIDATE — not frozen until the Windows qualification evidence for the exact candidate commit passes review.

Frozen predecessor: `FREEZE-01` at commit `316f49c63f28bb52c309ae64a8bb70f81bf6dac2`.

## Scope

This gate adds only deterministic, read-only selection over frozen `DiscoveryEntry` records. It does not mutate files and does not perform extraction, classification, routing, copy/move, rename, or later-engine behavior.

The mandatory precedence is:

`SOURCE → INCLUDE → EXCLUDE → EXCEPT → SELECTED`

Semantics are exact:

- no INCLUDE rules: every source record is initially eligible;
- one or more INCLUDE rules: a record is eligible only when at least one INCLUDE rule matches;
- EXCLUDE removes an eligible record when any EXCLUDE rule matches;
- EXCEPT can re-include only a record that was eligible after INCLUDE and was removed by EXCLUDE;
- EXCEPT is never evaluated for records that failed INCLUDE and therefore cannot bypass the INCLUDE boundary.

## Rule contract

Each rule has a unique ID, one stage, one match kind, and an explicit ordinal case policy for textual matching.

Supported match kinds:

- exact text match on full path, relative path, or file name;
- glob text match on full path, relative path, or file name;
- extension match;
- entry-kind match (`File` or `Directory`);
- inclusive file-size range match.

Glob grammar is deliberately bounded and deterministic: `*` matches zero or more non-separator characters, `**` matches across separators, and `?` matches one non-separator character. Path separators are normalized for matching. Character-class/brace syntax (`[]`, `{}`) is unsupported and fails closed as `QUERY.INVALID_GLOB`; it is never treated as a broadened pattern.

File-size predicates never match directories. A size rule may have a lower bound, an upper bound, or both; bounds are inclusive and must be non-negative.

## Determinism and auditability

Selection validates duplicate source path identities, compiles rules before evaluating any record, sorts decision output by deterministic path order, preserves rule declaration order inside `MatchedRuleIds`, and emits one `SelectionDecision` for every source record.

Decision dispositions are:

- `Selected`
- `RejectedByInclude`
- `Excluded`
- `ReIncludedByExcept`

Cancellation is fail-closed with `QUERY.CANCELLED`. Duplicate source identities fail closed with `QUERY.DUPLICATE_SOURCE`.

## Qualification

The gate must mechanically prove all of the following on Windows before freeze:

- exact frozen `FREEZE-01` ancestry and no changes outside the bounded FREEZE-02 whitelist;
- NuGet locked restore with the complete committed lock set;
- Release build with warnings treated as errors;
- deterministic Query assembly across consecutive clean builds;
- all frozen Foundation tests pass;
- all frozen Discovery tests pass;
- Query unit/negative/adversarial tests pass;
- real Windows filesystem integration uses the actual Discovery Engine before Query selection;
- Arabic/Unicode paths are exercised;
- EXCEPT cannot bypass INCLUDE;
- invalid glob syntax fails closed;
- directories cannot satisfy file-size predicates;
- case policy behavior is explicit;
- cancellation and duplicate source identities are tested;
- selection is read-only;
- deterministic output/rule ordering is tested;
- 20,000-record selection remains within the gate performance budget;
- evidence binds the exact commit and exact tree.

A successful automated run creates candidate evidence only. FREEZE-02 is not frozen until that evidence is independently checked for exact commit/tree binding and manifest integrity.
