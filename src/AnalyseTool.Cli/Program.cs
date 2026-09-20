using AnalyseTool.Core.Common;
using AnalyseTool.Core.Common.Bootstrap;
using AnalyseTool.Core.Common.Extensions;
using AnalyseTool.Core.Common.Policy;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;

namespace AnalyseTool.Cli;

/// <summary>
/// <c>AnalyseTool.Cli.exe</c> — the command line into Core for administrators and BIM coordinators
/// (design §6). Exit codes: 0 ok, 1 error, 2 validation failed / refused.
/// </summary>
internal static class Program
{
    private const string Usage = """
        AnalyseTool.Cli — manage AnalyseTool without opening Revit

          policy show [--json]                  effective organization policy on this seat
          policy validate <policy.json>         schema + semantic checks (exit 2 on problems)
          policy keygen [--out <folder>]        new signing key pair (policy-signing.key / .pub)
          policy sign <policy.json> --key <policy-signing.key>
          policy verify <policy.json> --pub <policy-signing.pub>
          policy fingerprint <policy-signing.pub>

          ext list [--json]                     installed extensions (all zones)
          ext validate <package.zip>            check a package before publishing it
          ext install <package.zip> [--overwrite]

          org status [--json]                   membership and the policy it applies
          org join <url|domain|folder|source:name> [--key <base64>] [--yes]
          org leave

          diag collect [--out <file.zip>]       logs + policy + state for support

          update wait-and-install --pid <n> --msi <file> [--sha256 <hex>]
                                                (used by the plugin's per-user self-update)

        Options: --revit <year> (default: from the install folder, else 2025)
        """;

    public static async Task<int> Main(string[] args)
    {
        try
        {
            Args a = new(args);
            if (a.Count == 0 || a.Has("--help") || a.Has("-h")) { Console.WriteLine(Usage); return 0; }

            CoreServices.InitializeHeadless(a.Option("--revit") ?? DetectRevitYear());

            return (a.Positional(0) ?? string.Empty, a.Positional(1) ?? string.Empty) switch
            {
                ("policy", "show") => PolicyShow(a),
                ("policy", "validate") => PolicyValidate(a),
                ("policy", "keygen") => PolicyKeygen(a),
                ("policy", "sign") => PolicySign(a),
                ("policy", "verify") => PolicyVerify(a),
                ("policy", "fingerprint") => PolicyFingerprint(a),
                ("ext", "list") => ExtList(a),
                ("ext", "validate") => ExtValidate(a),
                ("ext", "install") => ExtInstall(a),
                ("org", "status") => OrgStatus(a),
                ("org", "join") => await OrgJoinAsync(a),
                ("org", "leave") => OrgLeave(),
                ("diag", "collect") => DiagCollect(a),
                ("update", "wait-and-install") => UpdateWaitAndInstall(a),
                _ => Fail(Usage),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("error: " + ex.Message);
            return 1;
        }
    }

    // ---- policy ---------------------------------------------------------------------------------

    private static int PolicyShow(Args a)
    {
        PolicyState state = PolicyStore.Current;
        var view = new
        {
            present = state.IsPresent,
            origin = state.Origin.ToString().ToLowerInvariant(),
            machineFile = state.Path,
            machineFileExists = File.Exists(state.Path),
            pointerEnforced = state.PointerEnforced,
            membership = state.Membership is null ? null : new { state.Membership.PolicyUrl, state.Membership.OrganizationName, state.Membership.FetchedAt, signed = state.Membership.SigningKey is not null },
            organization = state.Document.Organization,
            locked = state.Document.Locked,
            problems = state.Problems,
            effective = new
            {
                codeExecution = new { value = state.Document.CodeExecution?.Enabled, layer = state.LayerOf("codeExecution"), locked = state.IsLocked(PolicySettings.CodeExecutionEnabled) },
                mcp = new { value = state.Document.Mcp?.Enabled, layer = state.LayerOf("mcp"), locked = state.IsLocked(PolicySettings.McpEnabled) },
                extensionRoots = ExtensionSources.PolicyRoots(),
                allowedFeeds = state.Document.Extensions?.AllowedFeeds,
                allowInstallFromRepository = PolicyFeedRules.InstallFromRepositoryAllowed,
                catalog = ExtensionSourceCatalog.PolicyCatalogSource,
                required = RequiredExtensions.Declared.Select(r => new { r.Id, r.Source, pinned = r.Sha256 is not null }),
                sources = state.Document.Sources?.Keys,
                unresolvedSources = PolicySourceResolver.UnresolvedSources(),
            },
            plugin = SharedData.ToolData.PLUGIN_VERSION,
            revit = CoreServices.RevitVersion,
        };
        return Print(view, a.Has("--json"));
    }

    private static int PolicyValidate(Args a)
    {
        string? file = a.Positional(2);
        if (file is null) return Fail("policy validate <policy.json>");
        IReadOnlyList<string> problems = PolicyValidation.Validate(file);
        if (problems.Count == 0) { Console.WriteLine($"OK: {file} is a valid policy."); return 0; }
        foreach (string p in problems) Console.WriteLine("- " + p);
        return 2;
    }

    private static int PolicyKeygen(Args a)
    {
        string dir = a.Option("--out") ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(dir);
        (string pub, string priv) = PolicySignature.GenerateKeyPair();
        string privPath = Path.Combine(dir, "policy-signing.key");
        string pubPath = Path.Combine(dir, "policy-signing.pub");
        if (File.Exists(privPath)) return Fail($"{privPath} exists — not overwriting a signing key.");
        File.WriteAllText(privPath, priv);
        File.WriteAllText(pubPath, pub);
        Console.WriteLine($"private key: {privPath}  (keep it out of the repository)");
        Console.WriteLine($"public key:  {pubPath}   (goes into the machine pointer's signingKey, or is typed at Join)");
        Console.WriteLine($"fingerprint: {PolicySignature.Fingerprint(pub)}");
        return 0;
    }

    private static int PolicySign(Args a)
    {
        string? file = a.Positional(2);
        string? key = a.Option("--key");
        if (file is null || key is null) return Fail("policy sign <policy.json> --key <policy-signing.key>");
        string sig = PolicySignature.Sign(File.ReadAllText(key), File.ReadAllBytes(file));
        string sigPath = PolicySignature.SignaturePathFor(file);
        File.WriteAllText(sigPath, sig);
        Console.WriteLine($"signed: {sigPath}");
        return 0;
    }

    private static int PolicyVerify(Args a)
    {
        string? file = a.Positional(2);
        string? pub = a.Option("--pub");
        if (file is null || pub is null) return Fail("policy verify <policy.json> --pub <policy-signing.pub>");
        string sigPath = PolicySignature.SignaturePathFor(file);
        SignatureState state = PolicySignature.Verify(File.ReadAllText(pub), File.ReadAllBytes(file),
            File.Exists(sigPath) ? File.ReadAllText(sigPath) : null);
        Console.WriteLine(state.ToString().ToLowerInvariant());
        return state == SignatureState.Verified ? 0 : 2;
    }

    private static int PolicyFingerprint(Args a)
    {
        string? pub = a.Positional(2);
        if (pub is null) return Fail("policy fingerprint <policy-signing.pub>");
        Console.WriteLine(PolicySignature.Fingerprint(File.ReadAllText(pub)) ?? "not a public key");
        return 0;
    }

    // ---- ext ------------------------------------------------------------------------------------

    private static int ExtList(Args a)
    {
        var rows = ExtensionCatalog.EnumerateAll(CoreServices.RevitVersion).Select(d => new
        {
            id = d.Manifest.Id,
            version = d.Manifest.Version,
            zone = d.Zone.ToString().ToLowerInvariant(),
            enabled = ExtensionStateStore.IsEnabled(d.Manifest.Id),
            required = RequiredExtensions.IsRequired(d.Manifest.Id),
            compatible = d.IsCompatibleWithHost,
            kind = d.DeclaresDll ? "dll" : d.HasScript ? "script" : "js",
            directory = d.Directory,
        }).OrderBy(r => r.id, StringComparer.OrdinalIgnoreCase).ToList();

        if (a.Has("--json")) return Print(rows, json: true);
        if (rows.Count == 0) { Console.WriteLine("no extensions"); return 0; }
        foreach (var r in rows)
            Console.WriteLine($"{r.id,-40} {r.version,-10} {r.zone,-8} {(r.enabled ? "on " : "off")} {(r.required ? "required " : "")}{(r.compatible ? "" : "incompatible ")}{r.kind}");
        return 0;
    }

    private static int ExtValidate(Args a)
    {
        string? zip = a.Positional(2);
        if (zip is null) return Fail("ext validate <package.zip>");
        ExtensionPackageInfo info = ExtensionPackage.Validate(zip);
        Console.WriteLine($"OK: {info.Manifest.Id} {info.Manifest.Version} (publisher: {info.Manifest.Publisher ?? "-"}; binary years: " +
                          $"{(info.BinaryYears.Count == 0 ? "none (script/UI)" : string.Join(", ", info.BinaryYears))})");
        Console.WriteLine($"sha256: {PackageHash.Sha256Of(zip)}");
        return 0;
    }

    private static int ExtInstall(Args a)
    {
        string? zip = a.Positional(2);
        if (zip is null) return Fail("ext install <package.zip> [--overwrite]");
        ExtensionInstallResult result = ExtensionInstaller.InstallPackage(zip, a.Has("--overwrite"), CoreServices.RevitVersion);
        if (result.AlreadyInstalled) { Console.WriteLine($"already installed: {result.Info.Manifest.Id} (use --overwrite)"); return 2; }
        Console.WriteLine($"installed: {result.Info.Manifest.Id} {result.Info.Manifest.Version} -> {result.Directory}");
        return 0;
    }

    // ---- org ------------------------------------------------------------------------------------

    private static int OrgStatus(Args a)
    {
        PolicyState state = PolicyStore.Current;
        var view = new
        {
            joined = state.Membership is not null,
            enforced = state.PointerEnforced,
            policyUrl = state.Membership?.PolicyUrl,
            organization = state.Document.Organization?.Name,
            contact = state.Document.Organization?.Contact,
            fetchedAt = state.Membership?.FetchedAt,
            applied = state.Organization is not null,
            signed = state.Membership?.SigningKey is not null,
            fingerprint = PolicySignature.Fingerprint(state.Membership?.SigningKey),
            minimumVersion = state.Document.MinimumVersion,
            plugin = SharedData.ToolData.PLUGIN_VERSION,
            problems = state.Problems,
        };
        return Print(view, a.Has("--json"));
    }

    private static async Task<int> OrgJoinAsync(Args a)
    {
        string? reference = a.Positional(2);
        if (reference is null) return Fail("org join <url|domain|folder|source:name> [--key <base64>] [--yes]");
        if (PolicyStore.Current.PointerEnforced) return Refuse("this computer's organization is set by an administrator");

        string? key = a.Option("--key");
        foreach (PolicyCandidate candidate in PolicyDiscovery.Candidates(reference))
        {
            OrgPolicyFetch fetch = await OrgPolicySource.FetchAsync(candidate.Reference, key, null, CancellationToken.None);
            if (!fetch.Accepted) { Console.WriteLine($"{candidate.Reference}: {fetch.Problem}"); continue; }

            PolicyDocument doc = fetch.Document!;
            Console.WriteLine($"organization: {doc.Organization?.Name} ({doc.Organization?.Contact})");
            Console.WriteLine($"location:     {fetch.Location}");
            Console.WriteLine($"signature:    {fetch.State.ToString().ToLowerInvariant()}");
            Console.WriteLine($"locks:        {string.Join(", ", doc.Locked)}");
            Console.WriteLine($"required:     {string.Join(", ", (doc.Extensions?.Required ?? new()).Select(r => r.Id))}");
            Console.WriteLine($"sources:      {string.Join(", ", doc.Sources?.Keys ?? Enumerable.Empty<string>())}");
            if (!a.Has("--yes")) { Console.WriteLine("re-run with --yes to join"); return 2; }

            OrgMembershipStore.Save(new OrgMembership
            {
                PolicyUrl = candidate.Reference,
                SigningKey = key,
                OrganizationName = doc.Organization?.Name,
                FetchedAt = DateTimeOffset.Now,
                CachedPolicy = fetch.Text,
                LastLocation = fetch.Location,
                LastSignature = fetch.Signature,
            });
            PolicyStore.Reload();
            Console.WriteLine("joined; applying catalog and required extensions…");
            await PolicyBackgroundApply.RunOnceAsync(CancellationToken.None);
            foreach (RequiredExtensionOutcome o in RequiredExtensions.LastReport().Outcomes)
                Console.WriteLine($"  {o.Id}: {o.State} {o.Version} {o.Detail}");
            return 0;
        }
        return Fail("no policy found");
    }

    private static int OrgLeave()
    {
        PolicyState state = PolicyStore.Current;
        if (state.PointerEnforced) return Refuse("this computer's organization is set by an administrator");
        if (state.Membership is null) { Console.WriteLine("not joined"); return 0; }
        OrgMembershipStore.Delete();
        PolicyStore.Reload();
        Console.WriteLine($"left {state.Membership.OrganizationName}");
        return 0;
    }

    // ---- diag -----------------------------------------------------------------------------------

    private static int DiagCollect(Args a)
    {
        string outFile = a.Option("--out") ?? Path.Combine(Directory.GetCurrentDirectory(), $"analysetool-diag-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        using ZipArchive zip = ZipFile.Open(outFile, ZipArchiveMode.Create);

        void AddFile(string path, string entry)
        {
            if (File.Exists(path)) zip.CreateEntryFromFile(path, entry);
        }

        string logs = Path.Combine(PathProvider.ProfilePath, "logs");
        if (Directory.Exists(logs))
            foreach (string log in Directory.GetFiles(logs).OrderByDescending(File.GetLastWriteTimeUtc).Take(3))
                AddFile(log, "logs/" + Path.GetFileName(log));
        AddFile(PathProvider.PolicyPath, "machine/policy.json");
        foreach (string name in new[] { "org.json", "sources.json", "extensions.json", "extensions-state.json", "codeexec.json", "catalog.json" })
            AddFile(Path.Combine(PathProvider.ProfilePath, name), "profile/" + name);

        // mcp.json carries the bridge's shared secret: shipped redacted, never as-is.
        string mcp = Path.Combine(PathProvider.ProfilePath, "mcp.json");
        if (File.Exists(mcp))
        {
            try
            {
                JObject redacted = JObject.Parse(File.ReadAllText(mcp));
                if (redacted["Token"] is not null) redacted["Token"] = "<redacted>";
                using Stream entry = zip.CreateEntry("profile/mcp.json").Open();
                using StreamWriter writer = new(entry);
                writer.Write(redacted.ToString(Formatting.Indented));
            }
            catch { /* unreadable: leave it out */ }
        }

        PolicyState state = PolicyStore.Current;
        var status = new
        {
            collectedAt = DateTimeOffset.Now,
            plugin = SharedData.ToolData.PLUGIN_VERSION,
            revit = CoreServices.RevitVersion,
            machine = Environment.MachineName,
            user = Environment.UserName,
            policy = new { state.IsPresent, state.Origin, state.Path, state.PointerEnforced, state.Problems, organization = state.Document.Organization?.Name },
            unresolvedSources = PolicySourceResolver.UnresolvedSources(),
            extensions = ExtensionCatalog.EnumerateAll(CoreServices.RevitVersion).Select(d => new
            {
                id = d.Manifest.Id, version = d.Manifest.Version, zone = d.Zone.ToString(), d.Directory,
                enabled = ExtensionStateStore.IsEnabled(d.Manifest.Id), compatible = d.IsCompatibleWithHost,
                error = ExtensionDiagnostics.GetError(d.Manifest.Id),
            }),
        };
        using (Stream entry = zip.CreateEntry("status.json").Open())
        using (StreamWriter writer = new(entry))
            writer.Write(JsonConvert.SerializeObject(status, Formatting.Indented));

        Console.WriteLine($"written: {outFile}");
        return 0;
    }

    // ---- update ---------------------------------------------------------------------------------

    /// <summary>The half of a per-user self-update that must outlive Revit: wait for the Revit process
    /// to exit, verify the installer, run it silently. Started detached by the plugin (design §7).</summary>
    private static int UpdateWaitAndInstall(Args a)
    {
        string? msi = a.Option("--msi");
        if (msi is null || !int.TryParse(a.Option("--pid"), out int pid))
            return Fail("update wait-and-install --pid <n> --msi <file> [--sha256 <hex>]");
        if (!File.Exists(msi)) return Fail($"{msi} not found");
        if (PackageHash.Verify(msi, a.Option("--sha256"), "installer") is string bad) return Fail(bad);

        try
        {
            using Process revit = Process.GetProcessById(pid);
            Console.WriteLine($"waiting for process {pid} ({revit.ProcessName}) to exit…");
            revit.WaitForExit();
        }
        catch (ArgumentException) { /* already gone */ }

        Console.WriteLine("installing…");
        using Process msiexec = Process.Start(new ProcessStartInfo("msiexec.exe", $"/i \"{msi}\" /qn /norestart") { UseShellExecute = false })
            ?? throw new InvalidOperationException("msiexec did not start");
        msiexec.WaitForExit();
        Console.WriteLine($"msiexec exit code {msiexec.ExitCode}");
        return msiexec.ExitCode == 0 ? 0 : 1;
    }

    // ---- helpers --------------------------------------------------------------------------------

    /// <summary>The plugin folder is <c>…\Addins\&lt;year&gt;\AnalyseTool</c>; the year is the parent's name.</summary>
    private static string DetectRevitYear()
    {
        string? parent = Path.GetFileName(Path.GetDirectoryName(PathProvider.RootDirectory.TrimEnd(Path.DirectorySeparatorChar)));
        return parent is { Length: 4 } && parent.All(char.IsDigit) ? parent : "2025";
    }

    private static int Print(object view, bool json)
    {
        if (json) { Console.WriteLine(JsonConvert.SerializeObject(view, Formatting.Indented)); return 0; }
        foreach (JProperty p in JObject.FromObject(view).Properties())
            Console.WriteLine($"{p.Name,-28} {(p.Value.Type is JTokenType.Object or JTokenType.Array ? p.Value.ToString(Formatting.None) : p.Value.ToString())}");
        return 0;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }

    /// <summary>A policy said no: exit 2, like a failed validation.</summary>
    private static int Refuse(string message)
    {
        Console.Error.WriteLine(message);
        return 2;
    }

    /// <summary>Tiny argument reader: positionals by index, <c>--name value</c> options, <c>--flag</c> switches.</summary>
    private sealed class Args
    {
        private readonly string[] _args;
        public Args(string[] args) => _args = args;
        public int Count => _args.Length;
        public string this[int i] => i < _args.Length ? _args[i] : string.Empty;
        public bool Has(string flag) => _args.Any(x => string.Equals(x, flag, StringComparison.OrdinalIgnoreCase));

        public string? Option(string name)
        {
            for (int i = 0; i < _args.Length - 1; i++)
                if (string.Equals(_args[i], name, StringComparison.OrdinalIgnoreCase)) return _args[i + 1];
            string inline = name + "=";
            return _args.FirstOrDefault(x => x.StartsWith(inline, StringComparison.OrdinalIgnoreCase))?[inline.Length..];
        }

        /// <summary>The n-th positional argument, skipping options and their values.</summary>
        public string? Positional(int index)
        {
            int seen = 0;
            for (int i = 0; i < _args.Length; i++)
            {
                if (_args[i].StartsWith("--", StringComparison.Ordinal))
                {
                    bool isSwitch = _args[i] is "--json" or "--yes" or "--overwrite" or "--help" or "-h" || _args[i].Contains('=');
                    if (!isSwitch) i++;
                    continue;
                }
                if (seen++ == index) return _args[i];
            }
            return null;
        }
    }
}
