using System.Diagnostics;
using UFOps.Core;
using UFOps.Discovery;
using UFOps.EngineSdk;

namespace UFOps.Discovery.Tests;

public sealed class DiscoveryEngineTests
{
    [Fact]
    public async Task DiscoversRecursiveUnicodeTreeInDeterministicOrder()
    {
        var root = TempRoot();
        try
        {
            var nested = Path.Combine(root, "عيادة-الرياض", "تقارير");
            Directory.CreateDirectory(nested);
            var firstFile = Path.Combine(root, "alpha.txt");
            var arabicFile = Path.Combine(nested, "ملف-مريض.txt");
            await File.WriteAllTextAsync(firstFile, "alpha", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(arabicFile, "بيانات", TestContext.Current.CancellationToken);

            var engine = new DiscoveryEngine();
            var request = new DiscoveryRequest([root]);
            var first = await engine.DiscoverAsync(request, TestContext.Current.CancellationToken);
            var second = await engine.DiscoverAsync(request, TestContext.Current.CancellationToken);

            Assert.True(first.IsSuccess);
            Assert.True(second.IsSuccess);
            Assert.False(first.Value.HasErrors);
            Assert.Contains(first.Value.Entries, entry => PathEquals(entry.FullPath, arabicFile));
            Assert.Equal(first.Value.Entries.Select(entry => entry.FullPath), second.Value.Entries.Select(entry => entry.FullPath));

            var actual = first.Value.Entries.Select(entry => entry.FullPath).ToArray();
            var expected = actual.OrderBy(path => path, PathComparer()).ThenBy(path => path, StringComparer.Ordinal).ToArray();
            Assert.Equal(expected, actual);
        }
        finally
        {
            DeleteTree(root);
        }
    }

    [Fact]
    public async Task TopDirectoryOnlyDoesNotDescendIntoChildren()
    {
        var root = TempRoot();
        try
        {
            var child = Path.Combine(root, "child");
            Directory.CreateDirectory(child);
            var topFile = Path.Combine(root, "top.txt");
            var nestedFile = Path.Combine(child, "nested.txt");
            await File.WriteAllTextAsync(topFile, "top", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(nestedFile, "nested", TestContext.Current.CancellationToken);

            var result = await new DiscoveryEngine().DiscoverAsync(
                new DiscoveryRequest([root], DiscoveryRecursion.TopDirectoryOnly),
                TestContext.Current.CancellationToken);

            Assert.True(result.IsSuccess);
            Assert.Contains(result.Value.Entries, entry => PathEquals(entry.FullPath, topFile));
            Assert.Contains(result.Value.Entries, entry => PathEquals(entry.FullPath, child));
            Assert.DoesNotContain(result.Value.Entries, entry => PathEquals(entry.FullPath, nestedFile));
        }
        finally
        {
            DeleteTree(root);
        }
    }

    [Fact]
    public async Task DuplicateAndOverlappingRootsDoNotDuplicateEntries()
    {
        var root = TempRoot();
        try
        {
            var child = Path.Combine(root, "child");
            Directory.CreateDirectory(child);
            var file = Path.Combine(child, "one.txt");
            await File.WriteAllTextAsync(file, "1", TestContext.Current.CancellationToken);

            var result = await new DiscoveryEngine().DiscoverAsync(
                new DiscoveryRequest([root, root, child]),
                TestContext.Current.CancellationToken);

            Assert.True(result.IsSuccess);
            var paths = result.Value.Entries.Select(entry => entry.FullPath).ToArray();
            Assert.Equal(paths.Length, paths.Distinct(PathComparer()).Count());
            Assert.Single(result.Value.Issues, issue => issue.Error.Code.Value == "DISCOVERY.DUPLICATE_ROOT");
            Assert.Contains(result.Value.Entries, entry => PathEquals(entry.FullPath, file));
        }
        finally
        {
            DeleteTree(root);
        }
    }

    [Fact]
    public async Task MissingRootProducesStructuredIssueWithoutHidingValidResults()
    {
        var root = TempRoot();
        try
        {
            var file = Path.Combine(root, "present.txt");
            var missing = Path.Combine(root, "does-not-exist");
            await File.WriteAllTextAsync(file, "present", TestContext.Current.CancellationToken);

            var result = await new DiscoveryEngine().DiscoverAsync(
                new DiscoveryRequest([root, missing]),
                TestContext.Current.CancellationToken);

            Assert.True(result.IsSuccess);
            Assert.True(result.Value.HasErrors);
            Assert.Contains(result.Value.Entries, entry => PathEquals(entry.FullPath, file));
            Assert.Contains(result.Value.Issues, issue =>
                PathEquals(issue.Locator, missing)
                && issue.Error.Code.Value == "DISCOVERY.NOT_FOUND"
                && issue.Severity == DiscoveryIssueSeverity.Error);
        }
        finally
        {
            DeleteTree(root);
        }
    }

    [Fact]
    public async Task PreCancelledDiscoveryFailsWithStructuredCancellation()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        var result = await new DiscoveryEngine().DiscoverAsync(new DiscoveryRequest([Path.GetTempPath()]), source.Token);
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCategory.Cancelled, result.Error!.Category);
        Assert.Equal("DISCOVERY.CANCELLED", result.Error.Code.Value);
    }

    [Fact]
    public async Task LongPathBeyondLegacyMaxPathIsDiscoveredOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = TempRoot();
        try
        {
            var current = root;
            for (var index = 0; index < 12; index++)
            {
                current = Path.Combine(current, $"segment-{index:D2}-abcdefghijklmnop");
            }

            Directory.CreateDirectory(current);
            var longFile = Path.Combine(current, "ملف-طويل.txt");
            await File.WriteAllTextAsync(longFile, "long-path", TestContext.Current.CancellationToken);
            Assert.True(longFile.Length > 260, $"Test path must exceed 260 characters; actual length={longFile.Length}.");

            var result = await new DiscoveryEngine().DiscoverAsync(new DiscoveryRequest([root]), TestContext.Current.CancellationToken);
            Assert.True(result.IsSuccess);
            Assert.Contains(result.Value.Entries, entry => PathEquals(entry.FullPath, longFile));
        }
        finally
        {
            DeleteTree(root);
        }
    }

    [Fact]
    public async Task ReparsePointIsNeverTraversedImplicitlyOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = TempRoot();
        var target = TempRoot();
        var junction = Path.Combine(root, "junction");
        try
        {
            var targetFile = Path.Combine(target, "must-not-be-reached.txt");
            await File.WriteAllTextAsync(targetFile, "outside", TestContext.Current.CancellationToken);
            CreateJunction(junction, target);

            var skip = await new DiscoveryEngine().DiscoverAsync(
                new DiscoveryRequest([root], DiscoveryRecursion.Recursive, ReparsePointPolicy.Skip),
                TestContext.Current.CancellationToken);
            Assert.True(skip.IsSuccess);
            Assert.DoesNotContain(skip.Value.Entries, entry => PathEquals(entry.FullPath, junction));
            Assert.Contains(skip.Value.Issues, issue => issue.Error.Code.Value == "DISCOVERY.REPARSE_SKIPPED");
            Assert.DoesNotContain(skip.Value.Entries, entry => entry.FullPath.EndsWith("must-not-be-reached.txt", StringComparison.OrdinalIgnoreCase));

            var include = await new DiscoveryEngine().DiscoverAsync(
                new DiscoveryRequest([root], DiscoveryRecursion.Recursive, ReparsePointPolicy.IncludeWithoutTraversal),
                TestContext.Current.CancellationToken);
            Assert.True(include.IsSuccess);
            Assert.Contains(include.Value.Entries, entry => PathEquals(entry.FullPath, junction) && entry.IsReparsePoint);
            Assert.DoesNotContain(include.Value.Entries, entry => entry.FullPath.EndsWith("must-not-be-reached.txt", StringComparison.OrdinalIgnoreCase));
            Assert.True(File.Exists(targetFile), "Discovery must not mutate the reparse target.");
        }
        finally
        {
            DeleteJunction(junction);
            DeleteTree(root);
            DeleteTree(target);
        }
    }

    [Fact]
    public async Task WindowsProtectedPathProducesPermissionIssueAndPreservesOtherRoot()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = TempRoot();
        try
        {
            var validFile = Path.Combine(root, "valid.txt");
            await File.WriteAllTextAsync(validFile, "valid", TestContext.Current.CancellationToken);
            var systemDrive = Path.GetPathRoot(Environment.SystemDirectory)
                ?? throw new InvalidOperationException("Windows system drive could not be resolved.");
            var protectedRoot = Path.Combine(systemDrive, "System Volume Information");

            var result = await new DiscoveryEngine().DiscoverAsync(
                new DiscoveryRequest([root, protectedRoot]),
                TestContext.Current.CancellationToken);

            Assert.True(result.IsSuccess);
            Assert.Contains(result.Value.Entries, entry => PathEquals(entry.FullPath, validFile));
            Assert.Contains(result.Value.Issues, issue =>
                PathEquals(issue.Locator, protectedRoot)
                && issue.Error.Category == ErrorCategory.Permission
                && issue.Severity == DiscoveryIssueSeverity.Error);
        }
        finally
        {
            DeleteTree(root);
        }
    }

    [Fact]
    public async Task QualificationRunsAgainstRealFilesystemAndEngineSdkContract()
    {
        var root = TempRoot();
        var evidence = TempRoot();
        try
        {
            var engine = new DiscoveryEngine();
            Assert.IsAssignableFrom<IEngine>(engine);
            var result = await engine.QualifyAsync(
                new EngineExecutionContext(OperationId.New(), root, evidence),
                TestContext.Current.CancellationToken);

            Assert.True(result.IsSuccess);
            Assert.True(result.Value.Passed);
            Assert.Equal("discovery.fs", engine.Descriptor.Id.Value);
            Assert.Contains(engine.Descriptor.Capabilities, capability => capability.Id.Value == "discovery.enumerate");
        }
        finally
        {
            DeleteTree(root);
            DeleteTree(evidence);
        }
    }

    [Fact]
    public async Task DiscoveryOfFiveThousandFilesMeetsGatePerformanceBudget()
    {
        var root = TempRoot();
        try
        {
            for (var index = 0; index < 5000; index++)
            {
                await File.WriteAllTextAsync(
                    Path.Combine(root, $"item-{index:D5}.dat"),
                    index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    TestContext.Current.CancellationToken);
            }

            var stopwatch = Stopwatch.StartNew();
            var result = await new DiscoveryEngine().DiscoverAsync(new DiscoveryRequest([root]), TestContext.Current.CancellationToken);
            stopwatch.Stop();

            Assert.True(result.IsSuccess);
            Assert.False(result.Value.HasErrors);
            Assert.Equal(5001, result.Value.Entries.Length);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(15),
                $"Discovery performance budget exceeded: {stopwatch.Elapsed.TotalMilliseconds:F0} ms for 5000 files.");
        }
        finally
        {
            DeleteTree(root);
        }
    }

    private static string TempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "UFOps-Discovery-Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return Path.GetFullPath(path);
    }

    private static void DeleteTree(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static void DeleteJunction(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        var attributes = File.GetAttributes(path);
        Assert.True((attributes & FileAttributes.ReparsePoint) != 0, "Cleanup path must still be a reparse point.");
        Directory.Delete(path, recursive: false);
    }

    private static StringComparer PathComparer() =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static bool PathEquals(string left, string right) =>
        PathComparer().Equals(Path.GetFullPath(left), Path.GetFullPath(right));

    private static void CreateJunction(string junction, string target)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("mklink");
        startInfo.ArgumentList.Add("/J");
        startInfo.ArgumentList.Add(junction);
        startInfo.ArgumentList.Add(target);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start junction creation process.");
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            var error = process.StandardError.ReadToEnd();
            var output = process.StandardOutput.ReadToEnd();
            throw new InvalidOperationException($"mklink /J failed with exit code {process.ExitCode}: {error}{output}");
        }
    }
}
