# FREEZE-03 — List Reconciliation Engine

Status: CANDIDATE — NOT FROZEN

Frozen parent: `f86f83409a30cf3a71d8ea14d0f42cffda37897b` (FREEZE-02 Query Selection).

## Bounded responsibility

`UFOps.Reconciliation` reconciles exactly two explicitly identified logical item lists. It is read-only and does not discover files, parse documents, classify content, route files, copy/move data, or render reports.

Each input item carries a stable source-local item ID and an unmodified raw value. Reconciliation preserves every item and every duplicate. It must never use set conversion or implicit deduplication.

## Explicit normalization

Normalization is part of the request contract and is never inferred from machine culture or UI locale:

- whitespace trimming: on/off;
- Unicode normalization: `None` or `FormC`;
- comparison: `Ordinal` or `OrdinalIgnoreCase`.

If a value becomes empty after the requested normalization, the operation fails closed with `RECONCILIATION.NORMALIZED_EMPTY` rather than silently dropping the item.

## Deterministic grouping and evidence

Items are grouped by the normalized comparison key while retaining item ID, raw value, and normalized value. Each group reports left/right item collections, counts, duplicate flags, and presence (`LeftOnly`, `RightOnly`, or `Both`). Summary totals are mechanically derived from the detailed groups.

Canonical group ordering and item ordering must be independent of input enumeration order. Case-insensitive collisions retain every original variant; the canonical key is chosen deterministically from the preserved normalized variants.

## Failure and cancellation semantics

Duplicate item IDs within either source and equal left/right source identities are rejected at contract construction. Undefined normalization enum values are rejected. Cancellation returns a structured `RECONCILIATION.CANCELLED` failure and never a misleading partial-success result.

## Qualification requirements

FREEZE-03 requires all of the following before freeze:

- locked dependency restore;
- warnings-as-errors Release build;
- deterministic `UFOps.Reconciliation.dll` SHA-256 across clean consecutive builds;
- all frozen Foundation regression tests PASS;
- all frozen Discovery regression tests PASS;
- all frozen Query regression tests PASS;
- Reconciliation functional and adversarial tests PASS on Windows;
- temporary real list files containing Arabic/Unicode values are read by the test harness into typed reconciliation items;
- 100,000 items across both sources meet the mechanical performance budget without data loss;
- repository drift check PASS;
- exact commit/tree-bound evidence package with an internal SHA-256 manifest;
- independent evidence verification with zero missing or mismatched entries.

Only after all gates pass may the exact qualifying commit/tree be declared `FREEZE-03 PASS / FROZEN`.
