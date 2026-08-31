using System.Collections.Immutable;
using System.Text;
using System.Text.Json;

namespace UFOps.Core;

public enum EvidenceOutcome
{
    Info,
    Pass,
    Warning,
    Fail
}

public sealed record EvidenceRecord
{
    public Guid EventId { get; }
    public DateTimeOffset TimestampUtc { get; }
    public OperationBinding? OperationBinding { get; }
    public OperationId? OperationId => OperationBinding?.OperationId;
    public string Component { get; }
    public string EventName { get; }
    public EvidenceOutcome Outcome { get; }
    public ImmutableDictionary<string, string> Properties { get; }

    private EvidenceRecord(
        Guid eventId,
        DateTimeOffset timestampUtc,
        OperationBinding? operationBinding,
        string component,
        string eventName,
        EvidenceOutcome outcome,
        ImmutableDictionary<string, string> properties)
    {
        if (eventId == Guid.Empty)
        {
            throw new ArgumentException("Evidence event ID cannot be empty.", nameof(eventId));
        }

        OperationPlan.EnsureUtc(timestampUtc, nameof(timestampUtc));
        EventId = eventId;
        TimestampUtc = timestampUtc;
        OperationBinding = operationBinding;
        Component = component;
        EventName = eventName;
        Outcome = outcome;
        Properties = properties;
    }

    public static EvidenceRecord Create(
        string component,
        string eventName,
        EvidenceOutcome outcome,
        DateTimeOffset timestampUtc,
        OperationBinding? operationBinding = null,
        IEnumerable<KeyValuePair<string, string>>? properties = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(component);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        OperationPlan.EnsureUtc(timestampUtc, nameof(timestampUtc));

        var materialized = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        if (properties is not null)
        {
            foreach (var pair in properties)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(pair.Key);
                ArgumentNullException.ThrowIfNull(pair.Value);
                if (!materialized.TryAdd(pair.Key, pair.Value))
                {
                    throw new ArgumentException($"Duplicate evidence property: {pair.Key}.", nameof(properties));
                }
            }
        }

        return new EvidenceRecord(
            Guid.CreateVersion7(),
            timestampUtc,
            operationBinding,
            component,
            eventName,
            outcome,
            materialized.ToImmutable());
    }
}

public sealed class JsonLinesEvidenceWriter : IDisposable
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private bool _disposed;

    public JsonLinesEvidenceWriter(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = System.IO.Path.GetFullPath(path);
    }

    public string Path => _path;

    public async ValueTask AppendAsync(EvidenceRecord record, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(record);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
            var json = JsonSerializer.Serialize(record, _jsonOptions);
            await File.AppendAllTextAsync(_path, json + "\n", Utf8WithoutBom, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _gate.Dispose();
        _disposed = true;
    }
}
