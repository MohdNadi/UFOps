using System.Collections.Immutable;
using UFOps.Core;

namespace UFOps.Discovery;

public enum DiscoveryRecursion
{
    TopDirectoryOnly,
    Recursive
}

public enum ReparsePointPolicy
{
    Skip,
    IncludeWithoutTraversal
}

public enum DiscoveryEntryKind
{
    File,
    Directory
}

public enum DiscoveryIssueSeverity
{
    Warning,
    Error
}

public sealed record DiscoveryRequest
{
    public ImmutableArray<string> Roots { get; }
    public DiscoveryRecursion Recursion { get; }
    public ReparsePointPolicy ReparsePointPolicy { get; }

    public DiscoveryRequest(
        IEnumerable<string> roots,
        DiscoveryRecursion recursion = DiscoveryRecursion.Recursive,
        ReparsePointPolicy reparsePointPolicy = ReparsePointPolicy.Skip)
    {
        ArgumentNullException.ThrowIfNull(roots);
        var materialized = roots.ToImmutableArray();
        if (materialized.IsDefaultOrEmpty)
        {
            throw new ArgumentException("Discovery requires at least one explicit input root.", nameof(roots));
        }

        if (materialized.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Discovery roots cannot contain null, empty, or whitespace values.", nameof(roots));
        }

        Roots = materialized;
        Recursion = recursion;
        ReparsePointPolicy = reparsePointPolicy;
    }
}

public sealed record DiscoveryEntry
{
    public string SourceRoot { get; }
    public string FullPath { get; }
    public string RelativePath { get; }
    public DiscoveryEntryKind Kind { get; }
    public long? LengthBytes { get; }
    public DateTimeOffset LastWriteTimeUtc { get; }
    public FileAttributes Attributes { get; }
    public bool IsReparsePoint => (Attributes & FileAttributes.ReparsePoint) != 0;

    public DiscoveryEntry(
        string sourceRoot,
        string fullPath,
        string relativePath,
        DiscoveryEntryKind kind,
        long? lengthBytes,
        DateTimeOffset lastWriteTimeUtc,
        FileAttributes attributes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (lastWriteTimeUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Discovery timestamps must be UTC.", nameof(lastWriteTimeUtc));
        }

        if (kind == DiscoveryEntryKind.Directory && lengthBytes is not null)
        {
            throw new ArgumentException("Directory entries cannot carry a file length.", nameof(lengthBytes));
        }

        if (kind == DiscoveryEntryKind.File && lengthBytes is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lengthBytes), "File length cannot be negative.");
        }

        SourceRoot = sourceRoot;
        FullPath = fullPath;
        RelativePath = relativePath;
        Kind = kind;
        LengthBytes = lengthBytes;
        LastWriteTimeUtc = lastWriteTimeUtc;
        Attributes = attributes;
    }
}

public sealed record DiscoveryIssue
{
    public string Locator { get; }
    public DiscoveryIssueSeverity Severity { get; }
    public UFOpsError Error { get; }

    public DiscoveryIssue(string locator, DiscoveryIssueSeverity severity, UFOpsError error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locator);
        ArgumentNullException.ThrowIfNull(error);
        Locator = locator;
        Severity = severity;
        Error = error;
    }
}

public sealed class DiscoveryResult
{
    public ImmutableArray<string> InputRoots { get; }
    public ImmutableArray<DiscoveryEntry> Entries { get; }
    public ImmutableArray<DiscoveryIssue> Issues { get; }
    public bool WasCancelled { get; }
    public bool HasErrors => Issues.Any(issue => issue.Severity == DiscoveryIssueSeverity.Error);

    public DiscoveryResult(
        IEnumerable<string> inputRoots,
        IEnumerable<DiscoveryEntry> entries,
        IEnumerable<DiscoveryIssue> issues,
        bool wasCancelled)
    {
        ArgumentNullException.ThrowIfNull(inputRoots);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(issues);

        InputRoots = inputRoots.ToImmutableArray();
        Entries = entries.ToImmutableArray();
        Issues = issues.ToImmutableArray();
        WasCancelled = wasCancelled;
    }
}
