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
        Id = IdentifierRules.Validate(id, nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeInputPath);
        ArgumentNullException.ThrowIfNull(expected);

        var normalizedPath = relativeInputPath.Replace('\\', '/');
        if (Path.IsPathRooted(relativeInputPath) || normalizedPath.StartsWith('/') || normalizedPath.EndsWith('/'))
        {
            throw new ArgumentException("Corpus input paths must be non-rooted file paths inside the corpus root.", nameof(relativeInputPath));
        }

        var segments = normalizedPath.Split('/');
        if (segments.Any(segment => string.IsNullOrWhiteSpace(segment) || segment is "." or ".." || segment.Contains(':')))
        {
            throw new ArgumentException("Corpus input paths contain an unsafe or ambiguous segment.", nameof(relativeInputPath));
        }

        foreach (var invalid in Path.GetInvalidPathChars())
        {
            if (normalizedPath.Contains(invalid))
            {
                throw new ArgumentException("Corpus input path contains an invalid path character.", nameof(relativeInputPath));
            }
        }

        var expectedBuilder = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        foreach (var pair in expected)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pair.Key);
            ArgumentNullException.ThrowIfNull(pair.Value);
            if (!expectedBuilder.TryAdd(pair.Key, pair.Value))
            {
                throw new ArgumentException($"Duplicate expected-result key: {pair.Key}.", nameof(expected));
            }
        }

        if (expectedBuilder.Count == 0)
        {
            throw new ArgumentException("A corpus case must declare at least one expected result.", nameof(expected));
        }

        RelativeInputPath = normalizedPath;
        Sha256 = IdentifierRules.ValidateSha256(sha256, nameof(sha256));
        Expected = expectedBuilder.ToImmutable();
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

        corpusId = IdentifierRules.Validate(corpusId, nameof(corpusId));
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(cases);

        var materialized = cases.ToImmutableArray();
        if (materialized.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A corpus manifest must contain at least one case.", nameof(cases));
        }

        if (materialized.Any(item => item is null))
        {
            throw new ArgumentException("A corpus manifest cannot contain null cases.", nameof(cases));
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
