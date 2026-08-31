using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace UFOps.TQ00;

internal static class ReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static string WriteEvidencePackage(string outputDirectory, QualificationReport report, IReadOnlyList<RawEvidence> rawEvidence)
    {
        Directory.CreateDirectory(outputDirectory);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var workDirectory = Path.Combine(outputDirectory, $"UFOPS_TQ00_{stamp}");
        var rawDirectory = Path.Combine(workDirectory, "raw");
        Directory.CreateDirectory(rawDirectory);

        File.WriteAllText(Path.Combine(workDirectory, "TQ00_RESULT.json"), JsonSerializer.Serialize(report, JsonOptions), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(workDirectory, "TQ00_RESULT.csv"), BuildCsv(report.Checks), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        File.WriteAllText(Path.Combine(workDirectory, "TQ00_SUMMARY.txt"), BuildHumanSummary(report), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        foreach (var raw in rawEvidence)
        {
            File.WriteAllText(Path.Combine(rawDirectory, ProbeEngine.Sanitize(raw.Name) + ".txt"), raw.Content ?? string.Empty, new UTF8Encoding(false));
        }

        var manifestPath = Path.Combine(workDirectory, "MANIFEST_SHA256.txt");
        File.WriteAllText(manifestPath, BuildManifest(workDirectory, manifestPath), new UTF8Encoding(false));

        var zipPath = Path.Combine(outputDirectory, $"UFOPS_TQ00_EVIDENCE_{stamp}.zip");
        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }

        ZipFile.CreateFromDirectory(workDirectory, zipPath, CompressionLevel.Optimal, includeBaseDirectory: true);
        return zipPath;
    }

    public static string BuildHumanSummary(QualificationReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Tool: {report.Tool} v{report.ToolVersion}");
        sb.AppendLine($"Timestamp UTC: {report.TimestampUtc}");
        sb.AppendLine($"OS: {report.OperatingSystem}");
        sb.AppendLine($"Architecture: {report.Architecture}");
        sb.AppendLine();

        foreach (var pair in report.Summary.OrderBy(static x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine($"{pair.Key,-22} {pair.Value}");
        }

        sb.AppendLine();
        foreach (var check in report.Checks)
        {
            sb.Append($"[{check.Status}] {check.Category} / {check.Name}");
            if (!string.IsNullOrWhiteSpace(check.Version)) sb.Append(" | ").Append(check.Version);
            if (!string.IsNullOrWhiteSpace(check.Path)) sb.Append(" | ").Append(check.Path);
            if (!string.IsNullOrWhiteSpace(check.Detail)) sb.Append(" | ").Append(check.Detail.ReplaceLineEndings(" "));
            sb.AppendLine();
        }

        return sb.ToString();
    }

    public static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string BuildManifest(string root, string manifestPath)
    {
        var entries = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !string.Equals(Path.GetFullPath(path), Path.GetFullPath(manifestPath), StringComparison.OrdinalIgnoreCase))
            .Select(path => new
            {
                Relative = Path.GetRelativePath(root, path).Replace('\\', '/'),
                Hash = ComputeSha256(path)
            })
            .OrderBy(static x => x.Relative, StringComparer.OrdinalIgnoreCase);

        return string.Join(Environment.NewLine, entries.Select(static x => $"{x.Hash}  {x.Relative}")) + Environment.NewLine;
    }

    private static string BuildCsv(IEnumerable<CheckResult> checks)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Category,Name,Status,Version,Path,RequiredFor,Detail");
        foreach (var c in checks)
        {
            sb.AppendLine(string.Join(',', new[]
            {
                Csv(c.Category), Csv(c.Name), Csv(c.Status), Csv(c.Version), Csv(c.Path), Csv(c.RequiredFor), Csv(c.Detail)
            }));
        }
        return sb.ToString();
    }

    private static string Csv(string? value)
    {
        value ??= string.Empty;
        return '"' + value.Replace("\"", "\"\"") + '"';
    }
}
