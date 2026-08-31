using System.Collections.Immutable;
using System.Security;
using UFOps.Core;
using UFOps.EngineSdk;

namespace UFOps.Discovery;

public sealed class DiscoveryEngine : IEngine
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static readonly IComparer<string> PathSortComparer = PathComparer;

    public EngineDescriptor Descriptor { get; } = new(
        new EngineId("discovery.fs"),
        "Filesystem Discovery",
        new Version(1, 0, 0),
        [
            new EngineCapability(
                new CapabilityId("discovery.enumerate"),
                1,
                "Deterministic discovery of explicit filesystem roots without mutation."),
            new EngineCapability(
                new CapabilityId("discovery.safe-reparse"),
                1,
                "Fail-safe reparse-point handling with no implicit traversal."),
            new EngineCapability(
                new CapabilityId("discovery.partial-failure"),
                1,
                "Structured inaccessible-path and partial-failure reporting.")
        ]);

    public ValueTask<Result<DiscoveryResult>> DiscoverAsync(
        DiscoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromResult(CancelledFailure());
        }

        var issues = new List<DiscoveryIssue>();
        var normalizedRoots = NormalizeRoots(request.Roots, issues);
        if (normalizedRoots.Count == 0)
        {
            return ValueTask.FromResult(Result.Failure<DiscoveryResult>(new UFOpsError(
                new ErrorCode("DISCOVERY.NO_VALID_ROOTS"),
                ErrorCategory.Validation,
                "None of the supplied discovery roots could be normalized.")));
        }

        var entries = new Dictionary<string, DiscoveryEntry>(PathComparer);
        var wasCancelled = false;

        foreach (var root in normalizedRoots)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                wasCancelled = true;
                AddCancellationIssue(issues, root);
                break;
            }

            ProcessRoot(root, request, entries, issues, cancellationToken, ref wasCancelled);
            if (wasCancelled)
            {
                break;
            }
        }

        var orderedEntries = entries.Values
            .OrderBy(entry => entry.FullPath, PathSortComparer)
            .ThenBy(entry => entry.FullPath, StringComparer.Ordinal)
            .ToImmutableArray();

        var orderedIssues = issues
            .OrderBy(issue => issue.Locator, PathSortComparer)
            .ThenBy(issue => issue.Locator, StringComparer.Ordinal)
            .ThenBy(issue => issue.Error.Code.Value, StringComparer.Ordinal)
            .ToImmutableArray();

        return ValueTask.FromResult(Result.Success(new DiscoveryResult(
            normalizedRoots,
            orderedEntries,
            orderedIssues,
            wasCancelled)));
    }

    public async ValueTask<Result<EngineQualification>> QualifyAsync(
        EngineExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (cancellationToken.IsCancellationRequested)
        {
            return Result.Failure<EngineQualification>(new UFOpsError(
                new ErrorCode("DISCOVERY.CANCELLED"),
                ErrorCategory.Cancelled,
                "Discovery qualification was cancelled before it started."));
        }

        Directory.CreateDirectory(context.WorkingDirectory);
        Directory.CreateDirectory(context.EvidenceDirectory);

        var qualificationRoot = Path.Combine(
            context.WorkingDirectory,
            $"discovery-qualification-{Guid.NewGuid():N}");
        var arabicDirectory = Path.Combine(qualificationRoot, "بيانات");
        var arabicFile = Path.Combine(arabicDirectory, "ملف-اختبار.txt");
        Directory.CreateDirectory(arabicDirectory);
        await File.WriteAllTextAsync(arabicFile, "UFOps discovery qualification", cancellationToken);

        try
        {
            var request = new DiscoveryRequest([qualificationRoot]);
            var first = await DiscoverAsync(request, cancellationToken);
            var second = await DiscoverAsync(request, cancellationToken);

            var checks = new List<QualificationCheck>
            {
                new(
                    "real-filesystem-discovery",
                    first.IsSuccess && first.Value.Entries.Any(entry => PathComparer.Equals(entry.FullPath, arabicFile)),
                    "A real Unicode/Arabic file must be discovered from the qualification filesystem."),
                new(
                    "no-unexpected-errors",
                    first.IsSuccess && !first.Value.HasErrors,
                    "Qualification discovery must complete without structured error-severity issues."),
                new(
                    "deterministic-order",
                    first.IsSuccess && second.IsSuccess && PathsEqual(first.Value.Entries, second.Value.Entries),
                    "Two consecutive scans of the unchanged tree must return the same ordered path sequence."),
                new(
                    "no-mutation",
                    File.Exists(arabicFile) && string.Equals(
                        await File.ReadAllTextAsync(arabicFile, cancellationToken),
                        "UFOps discovery qualification",
                        StringComparison.Ordinal),
                    "Discovery must not modify the qualification input file.")
            };

            return Result.Success(new EngineQualification(checks));
        }
        finally
        {
            if (Directory.Exists(qualificationRoot))
            {
                Directory.Delete(qualificationRoot, recursive: true);
            }
        }
    }

    private static List<string> NormalizeRoots(
        ImmutableArray<string> roots,
        ICollection<DiscoveryIssue> issues)
    {
        var normalized = new List<string>(roots.Length);
        var seen = new HashSet<string>(PathComparer);

        foreach (var rawRoot in roots)
        {
            try
            {
                var root = NormalizePath(rawRoot);
                if (seen.Add(root))
                {
                    normalized.Add(root);
                }
                else
                {
                    issues.Add(new DiscoveryIssue(
                        root,
                        DiscoveryIssueSeverity.Warning,
                        new UFOpsError(
                            new ErrorCode("DISCOVERY.DUPLICATE_ROOT"),
                            ErrorCategory.Input,
                            "A duplicate input root was ignored.")));
                }
            }
            catch (ArgumentException exception)
            {
                issues.Add(CreateIssue(rawRoot, exception));
            }
            catch (NotSupportedException exception)
            {
                issues.Add(CreateIssue(rawRoot, exception));
            }
            catch (PathTooLongException exception)
            {
                issues.Add(CreateIssue(rawRoot, exception));
            }
        }

        normalized.Sort(PathSortComparer);
        return normalized;
    }

    private static void ProcessRoot(
        string root,
        DiscoveryRequest request,
        IDictionary<string, DiscoveryEntry> entries,
        ICollection<DiscoveryIssue> issues,
        CancellationToken cancellationToken,
        ref bool wasCancelled)
    {
        if (!TryGetAttributes(root, out var rootAttributes, out var rootIssue))
        {
            issues.Add(rootIssue!);
            return;
        }

        if (IsReparsePoint(rootAttributes) && request.ReparsePointPolicy == ReparsePointPolicy.Skip)
        {
            AddReparseSkippedIssue(issues, root);
            return;
        }

        if (!TryAddEntry(root, root, rootAttributes, entries, issues))
        {
            return;
        }

        if (!IsDirectory(rootAttributes)
            || request.Recursion == DiscoveryRecursion.TopDirectoryOnly && IsReparsePoint(rootAttributes))
        {
            return;
        }

        if (IsReparsePoint(rootAttributes))
        {
            return;
        }

        var directories = new Stack<string>();
        directories.Push(root);

        while (directories.Count > 0)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                wasCancelled = true;
                AddCancellationIssue(issues, directories.Peek());
                return;
            }

            var currentDirectory = directories.Pop();
            string[] children;
            try
            {
                children = Directory.GetFileSystemEntries(currentDirectory);
            }
            catch (UnauthorizedAccessException exception)
            {
                issues.Add(CreateIssue(currentDirectory, exception));
                continue;
            }
            catch (DirectoryNotFoundException exception)
            {
                issues.Add(CreateIssue(currentDirectory, exception));
                continue;
            }
            catch (IOException exception)
            {
                issues.Add(CreateIssue(currentDirectory, exception));
                continue;
            }
            catch (SecurityException exception)
            {
                issues.Add(CreateIssue(currentDirectory, exception));
                continue;
            }

            Array.Sort(children, PathSortComparer);
            var childDirectories = new List<string>();

            foreach (var child in children)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    wasCancelled = true;
                    AddCancellationIssue(issues, child);
                    return;
                }

                if (!TryGetAttributes(child, out var attributes, out var issue))
                {
                    issues.Add(issue!);
                    continue;
                }

                if (IsReparsePoint(attributes) && request.ReparsePointPolicy == ReparsePointPolicy.Skip)
                {
                    AddReparseSkippedIssue(issues, child);
                    continue;
                }

                TryAddEntry(root, child, attributes, entries, issues);

                if (request.Recursion == DiscoveryRecursion.Recursive
                    && IsDirectory(attributes)
                    && !IsReparsePoint(attributes))
                {
                    childDirectories.Add(child);
                }
            }

            for (var index = childDirectories.Count - 1; index >= 0; index--)
            {
                directories.Push(childDirectories[index]);
            }
        }
    }

    private static bool TryAddEntry(
        string root,
        string path,
        FileAttributes attributes,
        IDictionary<string, DiscoveryEntry> entries,
        ICollection<DiscoveryIssue> issues)
    {
        if (entries.ContainsKey(path))
        {
            return true;
        }

        try
        {
            var isDirectory = IsDirectory(attributes);
            var lastWriteTime = isDirectory
                ? Directory.GetLastWriteTimeUtc(path)
                : File.GetLastWriteTimeUtc(path);
            long? length = isDirectory ? null : new FileInfo(path).Length;
            var relative = PathComparer.Equals(root, path) ? "." : Path.GetRelativePath(root, path);

            entries.Add(path, new DiscoveryEntry(
                root,
                path,
                relative,
                isDirectory ? DiscoveryEntryKind.Directory : DiscoveryEntryKind.File,
                length,
                new DateTimeOffset(lastWriteTime.ToUniversalTime()),
                attributes));
            return true;
        }
        catch (UnauthorizedAccessException exception)
        {
            issues.Add(CreateIssue(path, exception));
        }
        catch (FileNotFoundException exception)
        {
            issues.Add(CreateIssue(path, exception));
        }
        catch (DirectoryNotFoundException exception)
        {
            issues.Add(CreateIssue(path, exception));
        }
        catch (IOException exception)
        {
            issues.Add(CreateIssue(path, exception));
        }
        catch (SecurityException exception)
        {
            issues.Add(CreateIssue(path, exception));
        }

        return false;
    }

    private static bool TryGetAttributes(
        string path,
        out FileAttributes attributes,
        out DiscoveryIssue? issue)
    {
        try
        {
            attributes = File.GetAttributes(path);
            issue = null;
            return true;
        }
        catch (UnauthorizedAccessException exception)
        {
            attributes = default;
            issue = CreateIssue(path, exception);
        }
        catch (FileNotFoundException exception)
        {
            attributes = default;
            issue = CreateIssue(path, exception);
        }
        catch (DirectoryNotFoundException exception)
        {
            attributes = default;
            issue = CreateIssue(path, exception);
        }
        catch (PathTooLongException exception)
        {
            attributes = default;
            issue = CreateIssue(path, exception);
        }
        catch (IOException exception)
        {
            attributes = default;
            issue = CreateIssue(path, exception);
        }
        catch (SecurityException exception)
        {
            attributes = default;
            issue = CreateIssue(path, exception);
        }
        catch (ArgumentException exception)
        {
            attributes = default;
            issue = CreateIssue(path, exception);
        }
        catch (NotSupportedException exception)
        {
            attributes = default;
            issue = CreateIssue(path, exception);
        }

        return false;
    }

    private static DiscoveryIssue CreateIssue(string locator, Exception exception)
    {
        var (code, category, retryable) = exception switch
        {
            UnauthorizedAccessException => ("DISCOVERY.ACCESS_DENIED", ErrorCategory.Permission, false),
            SecurityException => ("DISCOVERY.SECURITY_ERROR", ErrorCategory.Permission, false),
            FileNotFoundException or DirectoryNotFoundException => ("DISCOVERY.NOT_FOUND", ErrorCategory.Input, false),
            PathTooLongException => ("DISCOVERY.PATH_TOO_LONG", ErrorCategory.Validation, false),
            ArgumentException or NotSupportedException => ("DISCOVERY.INVALID_PATH", ErrorCategory.Validation, false),
            IOException => ("DISCOVERY.IO_ERROR", ErrorCategory.Io, true),
            _ => ("DISCOVERY.INTERNAL_ERROR", ErrorCategory.Internal, false)
        };

        return new DiscoveryIssue(
            string.IsNullOrWhiteSpace(locator) ? "<invalid-root>" : locator,
            DiscoveryIssueSeverity.Error,
            new UFOpsError(new ErrorCode(code), category, exception.Message, retryable));
    }

    private static void AddReparseSkippedIssue(ICollection<DiscoveryIssue> issues, string path)
    {
        issues.Add(new DiscoveryIssue(
            path,
            DiscoveryIssueSeverity.Warning,
            new UFOpsError(
                new ErrorCode("DISCOVERY.REPARSE_SKIPPED"),
                ErrorCategory.Input,
                "Reparse point was skipped by the active discovery policy.")));
    }

    private static void AddCancellationIssue(ICollection<DiscoveryIssue> issues, string path)
    {
        issues.Add(new DiscoveryIssue(
            path,
            DiscoveryIssueSeverity.Warning,
            new UFOpsError(
                new ErrorCode("DISCOVERY.CANCELLED"),
                ErrorCategory.Cancelled,
                "Discovery was cancelled; returned entries are partial.")));
    }

    private static Result<DiscoveryResult> CancelledFailure() => Result.Failure<DiscoveryResult>(new UFOpsError(
        new ErrorCode("DISCOVERY.CANCELLED"),
        ErrorCategory.Cancelled,
        "Discovery was cancelled before it started."));

    private static string NormalizePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return Path.TrimEndingDirectorySeparator(fullPath);
    }

    private static bool IsDirectory(FileAttributes attributes) =>
        (attributes & FileAttributes.Directory) != 0;

    private static bool IsReparsePoint(FileAttributes attributes) =>
        (attributes & FileAttributes.ReparsePoint) != 0;

    private static bool PathsEqual(
        ImmutableArray<DiscoveryEntry> first,
        ImmutableArray<DiscoveryEntry> second)
    {
        if (first.Length != second.Length)
        {
            return false;
        }

        for (var index = 0; index < first.Length; index++)
        {
            if (!PathComparer.Equals(first[index].FullPath, second[index].FullPath))
            {
                return false;
            }
        }

        return true;
    }
}
