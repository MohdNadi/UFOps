using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using UFOps.Foundation.Storage;

namespace UFOps.Foundation.Host;

public sealed partial class MainWindow : Window
{
    private readonly FoundationDatabase _database;
    private readonly string _evidenceRoot;
    private bool _initialized;
    private string? _lastEvidencePath;

    public MainWindow()
    {
        InitializeComponent();
        var dataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "UFOps",
            "Foundation");
        _evidenceRoot = Path.Combine(dataRoot, "QualificationEvidence");
        _database = new FoundationDatabase(Path.Combine(dataRoot, "ufops-foundation.db"));
        DatabasePathText.Text = _database.DatabasePath;
        EvidencePathText.Text = "Not generated yet";
    }

    private async void OnRootLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        await RefreshHealthAsync();
    }

    private async void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        await RefreshHealthAsync();
    }

    private void OnOpenEvidenceClicked(object sender, RoutedEventArgs e)
    {
        if (!Directory.Exists(_evidenceRoot))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = _evidenceRoot,
            UseShellExecute = true
        });
    }

    private async Task RefreshHealthAsync()
    {
        RefreshButton.IsEnabled = false;
        OpenEvidenceButton.IsEnabled = false;
        HealthInfo.IsOpen = true;
        HealthInfo.Severity = InfoBarSeverity.Informational;
        HealthInfo.Title = "Checking foundation health...";
        HealthInfo.Message = string.Empty;

        var schema = 0;
        var quickCheck = "not-run";
        var journalMode = "not-run";
        var foreignKeys = false;
        var metadataRoundTrip = false;
        Exception? failure = null;

        try
        {
            await _database.InitializeAsync();
            schema = await _database.GetSchemaVersionAsync();
            quickCheck = await _database.QuickCheckAsync();
            journalMode = await _database.GetJournalModeAsync();
            foreignKeys = await _database.GetForeignKeysEnabledAsync();

            var qualificationValue = $"{DateTimeOffset.UtcNow:O}|{Guid.NewGuid():N}";
            await _database.SetMetadataAsync("runtime.qualification.last", qualificationValue);
            metadataRoundTrip = string.Equals(
                qualificationValue,
                await _database.GetMetadataAsync("runtime.qualification.last"),
                StringComparison.Ordinal);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        var healthy = failure is null
            && schema == FoundationDatabase.CurrentSchemaVersion
            && string.Equals(quickCheck, "ok", StringComparison.OrdinalIgnoreCase)
            && string.Equals(journalMode, "wal", StringComparison.OrdinalIgnoreCase)
            && foreignKeys
            && metadataRoundTrip;

        SchemaVersionText.Text = schema == 0 ? "Unavailable" : schema.ToString(System.Globalization.CultureInfo.InvariantCulture);
        QuickCheckText.Text = quickCheck;
        JournalModeText.Text = journalMode;
        ForeignKeysText.Text = foreignKeys ? "Enabled" : "Disabled / unavailable";
        MetadataRoundTripText.Text = metadataRoundTrip ? "PASS" : "FAIL";

        try
        {
            _lastEvidencePath = await WriteRuntimeEvidenceAsync(
                healthy,
                schema,
                quickCheck,
                journalMode,
                foreignKeys,
                metadataRoundTrip,
                failure);
            EvidencePathText.Text = _lastEvidencePath;
            OpenEvidenceButton.IsEnabled = true;
        }
        catch (Exception evidenceException)
        {
            healthy = false;
            failure ??= evidenceException;
            EvidencePathText.Text = $"Evidence generation failed ({evidenceException.GetType().Name}).";
        }

        HealthInfo.Severity = healthy ? InfoBarSeverity.Success : InfoBarSeverity.Error;
        HealthInfo.Title = healthy ? "Foundation health: PASS" : "Foundation health: FAIL";
        HealthInfo.Message = healthy
            ? "WinUI 3 launch, real SQLite initialization, WAL mode, foreign-key enforcement, metadata round-trip, integrity quick_check, and runtime evidence generation succeeded."
            : failure is null
                ? "One or more Foundation runtime invariants did not pass."
                : $"Qualification failed with {failure.GetType().Name}: {failure.Message}";

        RefreshButton.IsEnabled = true;
    }

    private async Task<string> WriteRuntimeEvidenceAsync(
        bool passed,
        int schema,
        string quickCheck,
        string journalMode,
        bool foreignKeys,
        bool metadataRoundTrip,
        Exception? failure)
    {
        Directory.CreateDirectory(_evidenceRoot);
        var timestamp = DateTimeOffset.UtcNow;
        var workDirectory = Path.Combine(_evidenceRoot, $"work-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDirectory);
        try
        {
            var executableHash = "unavailable";
            if (Environment.ProcessPath is { } processPath && File.Exists(processPath))
            {
                await using var stream = File.OpenRead(processPath);
                using var sha256 = SHA256.Create();
                var hash = await sha256.ComputeHashAsync(stream);
                executableHash = Convert.ToHexString(hash).ToLowerInvariant();
            }

            var evidencePath = Path.Combine(workDirectory, "RUNTIME_EVIDENCE.txt");
            await File.WriteAllLinesAsync(evidencePath,
            [
                "Gate=FREEZE-00 Foundation Runtime Qualification",
                $"Result={(passed ? "PASS" : "FAIL")}",
                $"TimestampUtc={timestamp:O}",
                $"OS={Environment.OSVersion.VersionString}",
                $"OSArchitecture={RuntimeInformation.OSArchitecture}",
                $"ProcessArchitecture={RuntimeInformation.ProcessArchitecture}",
                $"Framework={RuntimeInformation.FrameworkDescription}",
                $"Is64BitProcess={Environment.Is64BitProcess}",
                $"ExecutableSha256={executableHash}",
                $"SchemaVersion={schema}",
                $"QuickCheck={quickCheck}",
                $"JournalMode={journalMode}",
                $"ForeignKeysEnabled={foreignKeys}",
                $"MetadataRoundTrip={metadataRoundTrip}",
                $"FailureType={failure?.GetType().FullName ?? "NONE"}",
                $"FailureHResult={(failure?.HResult.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "NONE")}",
                "SensitiveContentIncluded=NO"
            ]);

            var evidenceHash = await ComputeSha256Async(evidencePath);
            await File.WriteAllTextAsync(
                Path.Combine(workDirectory, "MANIFEST_SHA256.txt"),
                $"{evidenceHash}  RUNTIME_EVIDENCE.txt{Environment.NewLine}");

            var zipPath = Path.Combine(
                _evidenceRoot,
                $"UFOPS_FREEZE00_RUNTIME_EVIDENCE_{timestamp:yyyyMMdd_HHmmss_fff}Z.zip");
            ZipFile.CreateFromDirectory(workDirectory, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
            return zipPath;
        }
        finally
        {
            if (Directory.Exists(workDirectory))
            {
                Directory.Delete(workDirectory, recursive: true);
            }
        }
    }

    private static async Task<string> ComputeSha256Async(string path)
    {
        await using var stream = File.OpenRead(path);
        using var sha256 = SHA256.Create();
        return Convert.ToHexString(await sha256.ComputeHashAsync(stream)).ToLowerInvariant();
    }
}
