# FREEZE-01 — Discovery Engine

Status: IMPLEMENTED CANDIDATE — NOT FROZEN

Frozen prerequisite: `FREEZE-00` commit `df47e5f1b6d8b64c5b838a845f0d8f42e7bde664`.

## Bounded behavior

The Discovery Engine performs read-only filesystem discovery over explicit input roots. It does not perform matching, classification, extraction, routing, mutation, rename, copy, move, delete, indexing, or later-engine behavior.

Contract behavior:

- accepts one or more explicit file or directory roots;
- normalizes roots to full paths and deduplicates duplicate roots using Windows path semantics on Windows;
- supports top-directory-only and recursive traversal;
- emits normalized file and directory records with source root, full path, relative path, kind, length for files, UTC last-write timestamp, attributes, and reparse-point flag;
- returns entries in stable deterministic path order;
- overlapping roots do not duplicate the same path record;
- missing, inaccessible, invalid, raced-away, and I/O-failed paths are reported as structured issues without discarding valid results from other roots;
- cancellation is fail-closed before start and returns an explicitly marked partial result if observed after discovery has begun;
- reparse points are never traversed implicitly. `Skip` excludes them and records a warning. `IncludeWithoutTraversal` records the reparse entry but never follows it;
- discovery is read-only and performs no mutation of inputs.

## Qualification matrix

The FREEZE-01 gate requires all of the following on Windows:

1. locked dependency restore with zero lock drift;
2. warnings-as-errors Release build;
3. frozen FREEZE-00 source regression guard;
4. Foundation regression tests PASS;
5. Discovery unit/negative/integration tests PASS;
6. real Unicode/Arabic filesystem paths PASS;
7. real path length above legacy MAX_PATH PASS;
8. duplicate and overlapping roots PASS;
9. missing-root partial failure PASS;
10. protected/inaccessible Windows path produces a permission issue while preserving other-root results;
11. real NTFS junction/reparse-point test proves no implicit traversal;
12. cancellation behavior PASS;
13. real Engine SDK qualification PASS;
14. 5,000-file discovery performance budget PASS;
15. deterministic Discovery assembly rebuild/hash comparison PASS;
16. deterministic gate evidence bound to exact commit SHA.

No FREEZE-01 PASS may be declared from build success alone. All tests and evidence must pass with zero unresolved gate-blocking failures.
