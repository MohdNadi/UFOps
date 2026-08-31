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
    public Guid EventId { get; init; }
    public DateTimeOffset TimestampUtc { get; init; }
    public OperationId? OperationId { get; init; }
    public string Component { get; init; } = string.Empty;
    public string EventName { get; init; } = string.Empty;
    public EvidenceOutcome Outcome { get; init; }
    public ImmutableDictionary<string, string> Properties { get; init; } = ImmutableDictionary<string, string>.Empty;

    public static EvidenceRecord Create(
        string component,
        string eventName,
        EvidenceOutcome outcome,
        DateTimeOffset timestampUtc,
        OperationId? operationId = null,
        IEnumerable<KeyValuePair<string, string>>? properties = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(component);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        return new EvidenceRecord
        {
            EventId = Guid.CreateVersion7(),
            TimestampUtc = timestampUtc,
            OperationId = operationId,
            Component = component,
            EventName = eventName,
            Outcome = outcome,
            Properties = properties?.ToImmutableDictionary(StringComparer.Ordinal) ?? ImmutableDictionary<string, string>.Empty
        };
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
        _path = Path.GetFullPath(path);
    }

    public async ValueTask AppendAsync(EvidenceRecord record, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(record);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
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
