# FREEZE-00 Foundation

Status: IMPLEMENTATION CANDIDATE — not frozen until Windows qualification passes.

This gate establishes only shared infrastructure that later product engines depend on. It does not claim Discovery, Query, file actions, document extraction, Teach-by-Example, classification, reporting, or UI product behavior.

## Locked technical baseline

- C# / .NET 10, SDK 10.0.400
- WinUI 3 through stable Microsoft.WindowsAppSDK 2.4.0 (integration is required before this gate can freeze)
- SQLite through Microsoft.Data.Sqlite 10.0.11
- Central package management and NuGet lock files
- Warnings as errors and deterministic builds
- xUnit v3 for Foundation qualification

## Foundation contracts

- Operation identity and immutable operation plan/result records
- Structured errors and typed result contract
- Engine identity, capability declaration, and qualification contract
- Append-only JSONL evidence writer
- Golden Corpus manifest model and JSON schema
- Real SQLite Foundation database with schema versioning, WAL, metadata persistence, and integrity check

## Non-negotiable constraints

Mocks, stubs, fake filesystem behavior, fake SQLite behavior, and placeholder product capabilities cannot satisfy this gate. Material changes to agreed architecture or launch scope require owner approval. Passing the gate cannot be achieved by removing, weakening, or deferring agreed v1.0 capabilities.
