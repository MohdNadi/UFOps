using System.Collections.Immutable;

namespace UFOps.Core;

public sealed record GoldenCorpusCase
{
    public string Id { get; }
    public string RelativeInputPath { get; }
    public string Sha256 { get; }
    public ImmutableDictionary<string, string> Expected { get; }

    public GoldenCorpusCase(
        string id,
        string relativeInputPath,
        string sha256,
        IEnumerable<KeyValuePair<string, string>> expected)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeInputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);
        ArgumentNullException.ThrowIfNull(expected);

        if (Path.IsPathRooted(relativeInputPath) || relativeInputPath.Split('/', '\\').Any(segment => segment == ".."))
        {
            throw new ArgumentException("Corpus input paths must remain inside the corpus root.", nameof(relativeInputPath));
        }

        if (sha256.Length != 64 || sha256.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("SHA-256 must contain exactly 64 hexadecimal characters.", nameof(sha256));
        }

        Id = id;
        RelativeInputPath = relativeInputPath.Replace('\\', '/');
        Sha256 = sha256.ToLowerInvariant();
        Expected = expected.ToImmutableDictionary(StringComparer.Ordinal);
    }
}

public sealed class GoldenCorpusManifest
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; }
    public string CorpusId { get; }
    public string Description { get; }
    public ImmutableArray<GoldenCorpusCase> Cases { get; }

    public GoldenCorpusManifest(int schemaVersion, string corpusId, string description, IEnumerable<GoldenCorpusCase> cases)
    {
        if (schemaVersion != CurrentSchemaVersion)
        {
            throw new ArgumentOutOfRangeException(nameof(schemaVersion), $"Only schema version {CurrentSchemaVersion} is supported.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(corpusId);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(cases);

        var materialized = cases.ToImmutableArray();
        if (materialized.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A corpus manifest must contain at least one case.", nameof(cases));
        }

        if (materialized.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != materialized.Length)
        {
            throw new ArgumentException("Corpus case IDs must be unique.", nameof(cases));
        }

        SchemaVersion = schemaVersion;
        CorpusId = corpusId;
        Description = description;
        Cases = materialized;
    }
}
