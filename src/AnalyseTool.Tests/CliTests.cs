using AnalyseTool.Core.Common.Policy;
using Newtonsoft.Json.Linq;
using System.Diagnostics;

namespace AnalyseTool.Tests;

/// <summary>
/// The CLI driven as an administrator drives it: the exe from its build output, arguments in, exit
/// code and text out. Validation and signing only — nothing here touches the runner's profile.
/// </summary>
public class CliTests
{
    private string _dir = null!;

    [Before(Test)]
    public void MakeTempDir()
    {
        _dir = Path.Combine(Path.GetTempPath(), "at-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [After(Test)]
    public void RemoveTempDir()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private static string CliDll()
    {
        // <src>/AnalyseTool.Tests/bin/<cfg>/<tfm>/ -> <src>/AnalyseTool.Cli/bin/<cfg>/<tfm>/AnalyseTool.Cli.dll
        string testBin = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        string tfm = Path.GetFileName(testBin);
        string cfg = Path.GetFileName(Path.GetDirectoryName(testBin)!);
        string src = Path.GetFullPath(Path.Combine(testBin, "..", "..", "..", ".."));
        string dll = Path.Combine(src, "AnalyseTool.Cli", "bin", cfg, tfm, "AnalyseTool.Cli.dll");
        if (!File.Exists(dll)) throw new FileNotFoundException("Build AnalyseTool.Cli first (the test project references it for build order).", dll);
        return dll;
    }

    private static (int ExitCode, string Stdout, string Stderr) Run(params string[] args)
    {
        ProcessStartInfo psi = new("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        psi.ArgumentList.Add(CliDll());
        foreach (string a in args) psi.ArgumentList.Add(a);
        using Process p = Process.Start(psi)!;
        string stdout = p.StandardOutput.ReadToEnd();
        string stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, stdout, stderr);
    }

    [Test]
    public async Task Policy_validate_passes_a_good_file_and_names_the_problems_of_a_bad_one()
    {
        string good = Path.Combine(_dir, "good.json");
        File.WriteAllText(good, """{ "version": 1, "organization": { "name": "Contoso", "contact": "bim@contoso" }, "codeExecution": { "enabled": false }, "locked": ["codeExecution.enabled"] }""");
        (int ok, string outGood, _) = Run("policy", "validate", good);
        await Assert.That(ok).IsEqualTo(0);
        await Assert.That(outGood).Contains("OK");

        string bad = Path.Combine(_dir, "bad.json");
        File.WriteAllText(bad, """
            { "version": 1,
              "locked": ["nosuch"],
              "extensions": { "allowedFeeds": ["ftp://x"], "required": [ { "id": "a.b", "source": "github:other/x", "sha256": "zz" } ],
                              "roots": ["source:missing/x"] },
              "sources": { "two": { "url": "https://a", "path": "C:\\\\b" } },
              "telemetry": { "sink": "http://insecure" } }
            """);
        (int code, string outBad, _) = Run("policy", "validate", bad);
        await Assert.That(code).IsEqualTo(2);
        await Assert.That(outBad).Contains("nosuch");
        await Assert.That(outBad).Contains("ftp://x");
        await Assert.That(outBad).Contains("not covered by allowedFeeds");
        await Assert.That(outBad).Contains("sha256");
        await Assert.That(outBad).Contains("not declared in sources");
        await Assert.That(outBad).Contains("exactly one of");
        await Assert.That(outBad).Contains("telemetry.sink");

        (int missing, _, _) = Run("policy", "validate", Path.Combine(_dir, "nope.json"));
        await Assert.That(missing).IsEqualTo(2);
    }

    [Test]
    public async Task Keygen_sign_and_verify_round_trip_through_the_exe()
    {
        (int gen, string genOut, _) = Run("policy", "keygen", "--out", _dir);
        await Assert.That(gen).IsEqualTo(0);
        await Assert.That(File.Exists(Path.Combine(_dir, "policy-signing.key"))).IsTrue();
        await Assert.That(genOut).Contains("fingerprint");

        string policy = Path.Combine(_dir, "policy.json");
        File.WriteAllText(policy, """{ "version": 1, "organization": { "name": "Contoso", "contact": "x" } }""");
        (int sign, _, _) = Run("policy", "sign", policy, "--key", Path.Combine(_dir, "policy-signing.key"));
        await Assert.That(sign).IsEqualTo(0);
        await Assert.That(File.Exists(policy + ".sig")).IsTrue();

        (int verify, string verifyOut, _) = Run("policy", "verify", policy, "--pub", Path.Combine(_dir, "policy-signing.pub"));
        await Assert.That(verify).IsEqualTo(0);
        await Assert.That(verifyOut.Trim()).IsEqualTo("verified");

        File.AppendAllText(policy, " ");
        (int tampered, string tamperedOut, _) = Run("policy", "verify", policy, "--pub", Path.Combine(_dir, "policy-signing.pub"));
        await Assert.That(tampered).IsEqualTo(2);
        await Assert.That(tamperedOut.Trim()).IsEqualTo("invalid");

        // and the signature the exe wrote is the one Core accepts
        SignatureState state = PolicySignature.Verify(File.ReadAllText(Path.Combine(_dir, "policy-signing.pub")),
            File.ReadAllBytes(policy), File.ReadAllText(policy + ".sig"));
        await Assert.That(state).IsEqualTo(SignatureState.Invalid); // tampered above
    }

    [Test]
    public async Task Help_and_unknown_commands_behave()
    {
        (int help, string helpOut, _) = Run("--help");
        await Assert.That(help).IsEqualTo(0);
        await Assert.That(helpOut).Contains("policy validate");

        (int unknown, _, string err) = Run("frobnicate");
        await Assert.That(unknown).IsEqualTo(1);
        await Assert.That(err).Contains("policy");
    }

    [Test]
    public async Task Ext_validate_reports_a_package_and_its_hash()
    {
        string zip = Path.Combine(_dir, "acme.tool-1.0.0.zip");
        using (System.IO.Compression.ZipArchive archive = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create))
        {
            using Stream s = archive.CreateEntry("plugin.json").Open();
            using StreamWriter w = new(s);
            w.Write("""{ "id": "acme.tool", "version": "1.0.0", "ui": { "button": { "name": "Tool" } } }""");
        }
        (int code, string output, string err) = Run("ext", "validate", zip);
        await Assert.That(code).IsEqualTo(0).Because(err);
        await Assert.That(output).Contains("acme.tool 1.0.0");
        JObject _ = new(); // keep the JSON import honest for future asserts
        await Assert.That(output).Contains("sha256:");
    }
}
