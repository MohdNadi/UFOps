using System.Text.Json.Serialization;

namespace UFOps.TQ00;

internal sealed record CheckResult(
    string Category,
    string Name,
    string Status,
    string RequiredFor,
    string? Version = null,
    string? Path = null,
    string? Detail = null);

internal sealed record RawEvidence(string Name, string Content);

internal sealed class QualificationReport
{
    public string Tool { get; init; } = "UFOps TQ-00 Windows Qualification";
    public string ToolVersion { get; init; } = "1.0.0";
    public string TimestampUtc { get; init; } = DateTimeOffset.UtcNow.ToString("O");
    public string OperatingSystem { get; init; } = Environment.OSVersion.VersionString;
    public string Architecture { get; init; } = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString();
    public List<CheckResult> Checks { get; init; } = [];
    public Dictionary<string, int> Summary { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Notes { get; init; } = [];
}

internal static class Statuses
{
    public const string Pass = "PASS";
    public const string Missing = "MISSING";
    public const string OptionalMissing = "OPTIONAL_MISSING";
    public const string PresentDisabled = "PRESENT_DISABLED";
    public const string PresentUnverified = "PRESENT_UNVERIFIED";
    public const string ProbeFailed = "PROBE_FAILED";
}
