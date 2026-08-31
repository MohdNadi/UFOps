using System.Collections.Immutable;
using UFOps.Core;

namespace UFOps.EngineSdk;

public sealed record EngineExecutionContext
{
    public OperationId OperationId { get; }
    public string WorkingDirectory { get; }
    public string EvidenceDirectory { get; }

    public EngineExecutionContext(OperationId operationId, string workingDirectory, string evidenceDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceDirectory);
        OperationId = operationId;
        WorkingDirectory = Path.GetFullPath(workingDirectory);
        EvidenceDirectory = Path.GetFullPath(evidenceDirectory);
    }
}

public sealed record QualificationCheck
{
    public string Id { get; }
    public bool Passed { get; }
    public string Detail { get; }

    public QualificationCheck(string id, bool passed, string detail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);
        Id = id;
        Passed = passed;
        Detail = detail;
    }
}

public sealed class EngineQualification
{
    public ImmutableArray<QualificationCheck> Checks { get; }
    public bool Passed => Checks.All(check => check.Passed);

    public EngineQualification(IEnumerable<QualificationCheck> checks)
    {
        ArgumentNullException.ThrowIfNull(checks);
        var materialized = checks.ToImmutableArray();
        if (materialized.IsDefaultOrEmpty)
        {
            throw new ArgumentException("Engine qualification must contain at least one check.", nameof(checks));
        }

        if (materialized.Select(check => check.Id).Distinct(StringComparer.Ordinal).Count() != materialized.Length)
        {
            throw new ArgumentException("Qualification check IDs must be unique.", nameof(checks));
        }

        Checks = materialized;
    }
}

public interface IEngine
{
    EngineDescriptor Descriptor { get; }

    ValueTask<Result<EngineQualification>> QualifyAsync(
        EngineExecutionContext context,
        CancellationToken cancellationToken = default);
}
