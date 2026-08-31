using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace UFOps.TQ00;

internal sealed class ProbeEngine
{
    private readonly List<CheckResult> _checks = [];
    private readonly List<RawEvidence> _raw = [];

    public IReadOnlyList<CheckResult> Checks => _checks;
    public IReadOnlyList<RawEvidence> RawEvidence => _raw;

    public void Run()
    {
        AddPlatformChecks();
        AddCoreToolingChecks();
        AddDocumentOracleChecks();
        AddOfficeOracleChecks();
        AddDiagnosticsChecks();
        AddPerformanceChecks();
        AddWindowsFeatureChecks();
        AddWindowsConfigurationChecks();
        AddStorageInventory();

        _checks.Sort(static (a, b) =>
        {
            var c = string.Compare(a.Category, b.Category, StringComparison.OrdinalIgnoreCase);
            return c != 0 ? c : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
    }

    private void AddPlatformChecks()
    {
        _checks.Add(new(
            "Platform",
            "Windows OS",
            OperatingSystem.IsWindows() ? Statuses.Pass : Statuses.Missing,
            "All Windows qualification",
            Environment.OSVersion.VersionString));

        var arch = RuntimeInformation.OSArchitecture;
        _checks.Add(new(
            "Platform",
            "x64 architecture",
            arch == Architecture.X64 ? Statuses.Pass : Statuses.Missing,
            "Primary release target",
            arch.ToString(),
            Detail: arch == Architecture.X64 ? null : "Primary TQ-00 release target is Windows x64."));
    }

    private void AddCoreToolingChecks()
    {
        ProbeTool("Core", "PowerShell 7", "pwsh.exe", ["--version"], true, "Build/test orchestration");
        ProbeTool("Core", "Git", "git.exe", ["--version"], true, "Source control and qualification");
        ProbeTool("Core", ".NET CLI", "dotnet.exe", ["--info"], true, "C#/.NET build");

        var dotnet = FindOnPath("dotnet.exe");
        if (dotnet is null)
        {
            _checks.Add(new("Core", ".NET 10 SDK", Statuses.Missing, "Foundation implementation"));
        }
        else
        {
            var sdkOutput = RunAndCapture("dotnet_list_sdks", dotnet, "--list-sdks");
            var sdkLine = sdkOutput.Output
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault(static line => line.StartsWith("10.", StringComparison.Ordinal));

            _checks.Add(new(
                "Core",
                ".NET 10 SDK",
                sdkLine is null ? Statuses.Missing : Statuses.Pass,
                "Foundation implementation",
                Version: sdkLine,
                Path: dotnet,
                Detail: sdkLine is null ? ".NET CLI exists, but a .NET 10 SDK was not proven installed." : null));
        }

        var vswhere = FirstExisting(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Visual Studio", "Installer", "vswhere.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft Visual Studio", "Installer", "vswhere.exe"));

        if (vswhere is null)
        {
            _checks.Add(new("Core", "Visual Studio / Build Tools", Statuses.Missing, "WinUI 3 build and Windows tooling",
                Detail: "vswhere.exe was not found in standard Visual Studio Installer locations."));
        }
        else
        {
            var result = RunAndCapture("vswhere", vswhere, "-latest", "-products", "*", "-property", "installationVersion");
            var version = FirstNonEmptyLine(result.Output);
            _checks.Add(new("Core", "Visual Studio / Build Tools",
                result.ExitCode == 0 && !string.IsNullOrWhiteSpace(version) ? Statuses.Pass : Statuses.PresentUnverified,
                "WinUI 3 build and Windows tooling",
                Version: version,
                Path: vswhere,
                Detail: result.Error));
        }

        var sdkRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Windows Kits", "10", "bin");
        var sdkVersions = Directory.Exists(sdkRoot)
            ? Directory.GetDirectories(sdkRoot)
                .Select(Path.GetFileName)
                .Where(static x => !string.IsNullOrWhiteSpace(x) && char.IsDigit(x![0]))
                .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : [];

        _checks.Add(new(
            "Core",
            "Windows SDK",
            sdkVersions.Length > 0 ? Statuses.Pass : Statuses.Missing,
            "WinUI, signing, packaging, Windows APIs",
            Version: sdkVersions.LastOrDefault(),
            Path: sdkRoot));
    }

    private void AddDocumentOracleChecks()
    {
        ProbeTool("Data", "SQLite CLI", "sqlite3.exe", ["--version"], false, "Independent SQLite oracle");
        ProbeTool("Documents", "Poppler pdftotext", "pdftotext.exe", ["-v"], false, "Independent PDF text oracle");
        ProbeTool("Documents", "qpdf", "qpdf.exe", ["--version"], false, "Independent PDF structural oracle");
    }

    private void AddOfficeOracleChecks()
    {
        ProbeOffice("Microsoft Excel Desktop", "excel.exe", "Excel compatibility oracle");
        ProbeOffice("Microsoft Word Desktop", "winword.exe", "Word compatibility oracle");
    }

    private void AddDiagnosticsChecks()
    {
        var tools = new[]
        {
            ("Sysinternals Streams", "streams.exe", "MOTW / ADS independent oracle"),
            ("Sysinternals Handle", "handle.exe", "Locked-handle diagnostics"),
            ("Sysinternals ProcDump", "procdump.exe", "Crash dump qualification"),
            ("Sysinternals Process Explorer", "procexp.exe", "Process/handle diagnostics"),
            ("Sysinternals RAMMap", "rammap.exe", "Memory-pressure diagnostics"),
            ("Sysinternals PsKill", "pskill.exe", "Fault-injection qualification"),
            ("DiskSpd", "diskspd.exe", "Storage performance qualification")
        };

        foreach (var (name, executable, use) in tools)
        {
            ProbePresence("Diagnostics", name, executable, false, use);
        }
    }

    private void AddPerformanceChecks()
    {
        ProbePresence("Performance", "Windows Performance Recorder", "wpr.exe", false, "ETW performance qualification");
        ProbePresence("Performance", "Windows Performance Analyzer", "wpa.exe", false, "ETW performance analysis");
    }

    private void AddWindowsFeatureChecks()
    {
        var hyperV = RunAndCapture("feature_hyperv", "dism.exe", "/Online", "/Get-FeatureInfo", "/FeatureName:Microsoft-Hyper-V-All", "/English");
        _checks.Add(ParseFeature("Virtualization", "Hyper-V", hyperV.Output, "Isolated fault/recovery Windows VM"));

        var sandbox = RunAndCapture("feature_sandbox", "dism.exe", "/Online", "/Get-FeatureInfo", "/FeatureName:Containers-DisposableClientVM", "/English");
        _checks.Add(ParseFeature("Virtualization", "Windows Sandbox", sandbox.Output, "Disposable clean Windows qualification"));
    }

    private void AddWindowsConfigurationChecks()
    {
        _checks.Add(ReadRegistryDword(
            "Windows",
            "Long paths enabled",
            RegistryHive.LocalMachine,
            @"SYSTEM\CurrentControlSet\Control\FileSystem",
            "LongPathsEnabled",
            1,
            false,
            "Long-path corpus qualification"));

        _checks.Add(ReadRegistryDword(
            "Windows",
            "Developer Mode",
            RegistryHive.LocalMachine,
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock",
            "AllowDevelopmentWithoutDevLicense",
            1,
            false,
            "Developer convenience; not a product runtime requirement"));
    }

    private void AddStorageInventory()
    {
        try
        {
            var lines = DriveInfo.GetDrives()
                .Where(static d => d.IsReady)
                .Select(static d => $"{d.Name}\t{d.DriveType}\t{d.DriveFormat}\t{d.TotalSize}\t{d.AvailableFreeSpace}")
                .ToArray();

            _raw.Add(new("volumes", "Drive\tType\tFileSystem\tTotalBytes\tFreeBytes" + Environment.NewLine + string.Join(Environment.NewLine, lines)));
            _checks.Add(new("Storage", "Volume inventory", lines.Length > 0 ? Statuses.Pass : Statuses.PresentUnverified,
                "Filesystem qualification planning", Detail: "Captured in raw/volumes.txt"));
        }
        catch (Exception ex)
        {
            _checks.Add(new("Storage", "Volume inventory", Statuses.ProbeFailed,
                "Filesystem qualification planning", Detail: ex.Message));
        }
    }

    private void ProbeTool(string category, string name, string executable, string[] args, bool required, string requiredFor)
    {
        var path = FindOnPath(executable);
        if (path is null)
        {
            _checks.Add(new(category, name, required ? Statuses.Missing : Statuses.OptionalMissing,
                requiredFor, Detail: executable + " was not found on PATH."));
            return;
        }

        var result = RunAndCapture("tool_" + Sanitize(name), path, args);
        var version = FirstNonEmptyLine(result.Output);
        var status = result.ExitCode == 0 || !string.IsNullOrWhiteSpace(version)
            ? Statuses.Pass
            : Statuses.PresentUnverified;

        _checks.Add(new(category, name, status, requiredFor, version, path, result.Error));
    }

    private void ProbePresence(string category, string name, string executable, bool required, string requiredFor)
    {
        var path = FindOnPath(executable);
        _checks.Add(new(category, name,
            path is null ? (required ? Statuses.Missing : Statuses.OptionalMissing) : Statuses.Pass,
            requiredFor, Path: path));
    }

    private void ProbeOffice(string name, string executable, string requiredFor)
    {
        var path = FindOfficeExecutable(executable);
        _checks.Add(new("Office Oracle", name,
            path is null ? Statuses.OptionalMissing : Statuses.Pass,
            requiredFor,
            Path: path,
            Detail: path is null ? "Not required by UFOps at runtime; used only as an independent compatibility oracle." : null));
    }

    private string? FindOfficeExecutable(string executable)
    {
        var fromPath = FindOnPath(executable);
        if (fromPath is not null)
        {
            return fromPath;
        }

        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + executable);
                    if (key?.GetValue(null) is string value && File.Exists(value))
                    {
                        return value;
                    }
                }
                catch
                {
                    // Continue to deterministic filesystem probes.
                }
            }
        }

        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
                 })
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            foreach (var officeRoot in new[]
                     {
                         Path.Combine(root, "Microsoft Office", "root"),
                         Path.Combine(root, "Microsoft Office")
                     })
            {
                if (!Directory.Exists(officeRoot))
                {
                    continue;
                }

                try
                {
                    var match = Directory.EnumerateFiles(officeRoot, executable, SearchOption.AllDirectories).FirstOrDefault();
                    if (match is not null)
                    {
                        return match;
                    }
                }
                catch
                {
                    // Access to an optional search subtree is not a fatal qualification failure.
                }
            }
        }

        return null;
    }

    private CheckResult ReadRegistryDword(
        string category,
        string name,
        RegistryHive hive,
        string subKey,
        string valueName,
        int expected,
        bool required,
        string requiredFor)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(subKey);
            var raw = key?.GetValue(valueName);
            if (raw is int value)
            {
                return new(category, name,
                    value == expected ? Statuses.Pass : Statuses.PresentDisabled,
                    requiredFor,
                    Version: value.ToString(CultureInfo.InvariantCulture));
            }

            return new(category, name, required ? Statuses.Missing : Statuses.OptionalMissing,
                requiredFor, Detail: "Registry DWORD was not present." );
        }
        catch (Exception ex)
        {
            return new(category, name, Statuses.ProbeFailed, requiredFor, Detail: ex.Message);
        }
    }

    private CheckResult ParseFeature(string category, string name, string output, string requiredFor)
    {
        if (output.Contains("State : Enabled", StringComparison.OrdinalIgnoreCase))
        {
            return new(category, name, Statuses.Pass, requiredFor, Version: "Enabled");
        }

        if (output.Contains("State : Disabled", StringComparison.OrdinalIgnoreCase))
        {
            return new(category, name, Statuses.PresentDisabled, requiredFor, Version: "Disabled");
        }

        if (output.Contains("Feature Name", StringComparison.OrdinalIgnoreCase))
        {
            return new(category, name, Statuses.PresentUnverified, requiredFor);
        }

        return new(category, name, Statuses.OptionalMissing, requiredFor,
            Detail: "The feature was not proven present by the read-only DISM probe.");
    }

    private CommandResult RunAndCapture(string evidenceName, string executable, params string[] args)
    {
        var result = RunProcess(executable, args, TimeSpan.FromSeconds(30));
        var content = result.Output;
        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            content += Environment.NewLine + "ERROR: " + result.Error;
        }
        _raw.Add(new(evidenceName, content.Trim()));
        return result;
    }

    internal static CommandResult RunProcess(string executable, IEnumerable<string> args, TimeSpan timeout)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var arg in args)
            {
                process.StartInfo.ArgumentList.Add(arg);
            }

            if (!process.Start())
            {
                return new(-1, string.Empty, "Process did not start.");
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit((int)timeout.TotalMilliseconds))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return new(-2, stdoutTask.GetAwaiter().GetResult(), "Probe timed out.");
            }

            Task.WaitAll(stdoutTask, stderrTask);
            var stdout = stdoutTask.Result.Trim();
            var stderr = stderrTask.Result.Trim();
            var combined = string.Join(Environment.NewLine, new[] { stdout, stderr }.Where(static x => !string.IsNullOrWhiteSpace(x)));
            return new(process.ExitCode, combined, process.ExitCode == 0 ? null : stderr);
        }
        catch (Exception ex)
        {
            return new(-1, string.Empty, ex.Message);
        }
    }

    internal static string? FindOnPath(string executable)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var part in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(part.Trim('"'), executable);
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
            catch
            {
                // Ignore malformed PATH entries and continue.
            }
        }

        return null;
    }

    private static string? FirstExisting(params string[] paths) => paths.FirstOrDefault(File.Exists);

    internal static string? FirstNonEmptyLine(string? text) => text?
        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .FirstOrDefault();

    internal static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        return new string(value.Select(c => invalid.Contains(c) || char.IsWhiteSpace(c) ? '_' : char.ToLowerInvariant(c)).ToArray());
    }
}

internal sealed record CommandResult(int ExitCode, string Output, string? Error);
