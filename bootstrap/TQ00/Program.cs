using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace UFOps.TQ00;

internal static class Program
{
    private const string ToolVersion = "1.0.0";

    public static int Main(string[] args)
    {
        try
        {
            if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
            {
                RunSelfTest();
                Console.WriteLine("SELF_TEST=PASS");
                return 0;
            }

            if (!OperatingSystem.IsWindows())
            {
                Console.Error.WriteLine("TQ-00 must be executed on Windows.");
                return 3;
            }

            var outputDirectory = ResolveOutputDirectory(args);
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine("UFOps TQ-00 Windows Qualification");
            Console.WriteLine($"Version: {ToolVersion}");
            Console.WriteLine("Mode: READ-ONLY");
            Console.WriteLine();

            var engine = new ProbeEngine();
            engine.Run();

            var checks = engine.Checks.ToList();
            var summary = checks
                .GroupBy(static x => x.Status, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(static g => g.Key, static g => g.Count(), StringComparer.OrdinalIgnoreCase);

            var report = new QualificationReport
            {
                ToolVersion = ToolVersion,
                TimestampUtc = DateTimeOffset.UtcNow.ToString("O"),
                OperatingSystem = RuntimeInformation.OSDescription,
                Architecture = RuntimeInformation.OSArchitecture.ToString(),
                Checks = checks,
                Summary = summary,
                Notes =
                [
                    "TQ-00 is read-only. It does not install, uninstall, enable, disable, or reconfigure software or Windows features.",
                    "MISSING means the component was not proven present by the implemented probes; it is not an installation attempt.",
                    "Office Desktop is an independent compatibility oracle and is not a UFOps runtime dependency.",
                    "Product implementation remains blocked until the Windows qualification evidence is reviewed."
                ]
            };

            var evidenceZip = ReportWriter.WriteEvidencePackage(outputDirectory, report, engine.RawEvidence);
            var hash = ReportWriter.ComputeSha256(evidenceZip);

            Console.WriteLine(ReportWriter.BuildHumanSummary(report));
            Console.WriteLine($"Evidence package: {evidenceZip}");
            Console.WriteLine($"SHA-256: {hash}");
            Console.WriteLine("TQ00_SCAN_COMPLETE=PASS");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("TQ00_SCAN_COMPLETE=FAIL");
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    private static string ResolveOutputDirectory(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--output", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFullPath(args[i + 1]);
            }
        }

        var executablePath = Environment.ProcessPath;
        return executablePath is null
            ? Environment.CurrentDirectory
            : Path.GetDirectoryName(executablePath) ?? Environment.CurrentDirectory;
    }

    private static void RunSelfTest()
    {
        Assert(ProbeEngine.Sanitize("A B/C") == "a_b_c", "Sanitize");
        Assert(ProbeEngine.FirstNonEmptyLine("\r\n  alpha  \r\nbeta") == "alpha", "FirstNonEmptyLine");

        var tempRoot = Path.Combine(Path.GetTempPath(), "ufops-tq00-selftest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var file = Path.Combine(tempRoot, "sha.txt");
            File.WriteAllText(file, "abc", new UTF8Encoding(false));
            var hash = ReportWriter.ComputeSha256(file);
            Assert(hash == "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", "SHA256");

            var report = new QualificationReport
            {
                Checks = [new CheckResult("SelfTest", "Check", Statuses.Pass, "Self-test")],
                Summary = new Dictionary<string, int> { [Statuses.Pass] = 1 }
            };
            var zip = ReportWriter.WriteEvidencePackage(tempRoot, report, [new RawEvidence("sample", "ok")]);
            Assert(File.Exists(zip), "Evidence ZIP creation");
            Assert(new FileInfo(zip).Length > 0, "Evidence ZIP non-empty");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static void Assert(bool condition, string name)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Self-test failed: " + name);
        }
    }
}
