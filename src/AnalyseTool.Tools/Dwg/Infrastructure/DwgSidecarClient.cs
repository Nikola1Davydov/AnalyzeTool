using Newtonsoft.Json;
using Serilog;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace AnalyseTool.Tools.Dwg
{
    /// <summary>A failure reported by the sidecar, or by the attempt to run it. <see cref="Code"/> is the
    /// wire code (<c>not_found</c>, <c>unsupported_format</c>, <c>read_failed</c>, <c>parser_panic</c>…)
    /// so a caller can branch without matching the message text.</summary>
    public sealed class DwgSidecarException : Exception
    {
        public DwgSidecarException(string code, string message) : base(message) => Code = code;

        public string Code { get; }
    }

    /// <summary>
    /// Talks to <c>analysetool-dwg</c>, the out-of-process DWG/DXF reader.
    ///
    /// One process per request, deliberately. A persistent server would save a few milliseconds of
    /// start-up and cost a lifetime to manage — and the whole reason this reader is a process at all is
    /// that DWG is a reverse-engineered format whose parser CAN die on a malformed file. Dying takes one
    /// request with it and nothing else; a long-lived process would carry the damage forward.
    ///
    /// The transport is line-delimited JSON over stdio, the same shape OpenCADStudio's <c>--serve</c>
    /// speaks, so the backend can be swapped for that app without touching anything above this class.
    /// </summary>
    internal sealed class DwgSidecarClient
    {
        /// <summary>The wire version this client understands. The sidecar reports its own in <c>ping</c>;
        /// a mismatch is refused at once, because a silently misread response is wrong geometry in
        /// someone's model and that is far worse than a clear failure.</summary>
        public const int SupportedProtocol = 1;

        /// <summary>Overrides the executable path — set it to a <c>cargo build</c> output to test a
        /// sidecar you are working on without deploying it.</summary>
        public const string PathOverrideVariable = "ANALYSETOOL_DWG_SIDECAR";

        private const string ExecutableName = "analysetool-dwg.exe";
        private const string SubFolder = "dwg";

        /// <summary>Generous on purpose: parsing a 200 MB survey drawing is minutes of work, and a
        /// timeout that fires on a healthy read is indistinguishable, to the user, from a broken tool.
        /// Cancellation is the responsive path — the caller's token kills the process at once.</summary>
        private static readonly TimeSpan CallTimeout = TimeSpan.FromMinutes(10);

        private static readonly SemaphoreSlim HandshakeGate = new(1, 1);
        private static DwgSidecarInfo? _verified;

        private static long _nextId;

        /// <summary>Identity and protocol version of the deployed sidecar.</summary>
        public Task<DwgSidecarInfo> PingAsync(CancellationToken ct) =>
            CallAsync<DwgSidecarInfo>(new DwgRequest { Op = "ping" }, ct);

        /// <summary>What is in the drawing: layers with per-type counts, blocks, units, extents. Reads the
        /// file; creates nothing.</summary>
        public async Task<DwgStructure> GetStructureAsync(string path, string? space, bool failsafe, CancellationToken ct)
        {
            await EnsureCompatibleAsync(ct).ConfigureAwait(false);
            return await CallAsync<DwgStructure>(
                new DwgRequest { Op = "structure", Path = path, Space = space, Failsafe = failsafe }, ct)
                .ConfigureAwait(false);
        }

        /// <summary>The entities themselves, filtered, in drawing units with angles in radians.</summary>
        public async Task<DwgEntities> ReadAsync(
            string path,
            IReadOnlyList<string>? layers,
            IReadOnlyList<string>? types,
            string? space,
            int? maxEntities,
            bool failsafe,
            CancellationToken ct)
        {
            await EnsureCompatibleAsync(ct).ConfigureAwait(false);
            return await CallAsync<DwgEntities>(
                new DwgRequest
                {
                    Op = "read",
                    Path = path,
                    Layers = layers,
                    Types = types,
                    Space = space,
                    MaxEntities = maxEntities,
                    Failsafe = failsafe,
                }, ct).ConfigureAwait(false);
        }

        /// <summary>Pings once per Revit session and remembers the answer. The handshake is what turns a
        /// protocol mismatch into a sentence the user can act on instead of a field that silently
        /// deserializes to zero.</summary>
        private async Task EnsureCompatibleAsync(CancellationToken ct)
        {
            if (_verified is not null) return;

            await HandshakeGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_verified is not null) return;

                DwgSidecarInfo info = await PingAsync(ct).ConfigureAwait(false);
                if (info.Protocol != SupportedProtocol)
                {
                    throw new DwgSidecarException(
                        "protocol_mismatch",
                        $"The DWG reader speaks protocol {info.Protocol}, this build expects {SupportedProtocol}. " +
                        $"Reader: {info.Name} {info.Version} at '{ResolveExecutable()}'.");
                }

                Log.Information("DWG reader ready: {Name} {Version} ({Codec}), protocol {Protocol}",
                    info.Name, info.Version, info.Codec, info.Protocol);
                _verified = info;
            }
            finally
            {
                HandshakeGate.Release();
            }
        }

        private async Task<T> CallAsync<T>(DwgRequest request, CancellationToken ct)
        {
            request.Id = Interlocked.Increment(ref _nextId);
            string executable = ResolveExecutable();

            ProcessStartInfo startInfo = new(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(executable) ?? Environment.CurrentDirectory,
                // Spelled out, all three: layer names and text are routinely Cyrillic or CJK, and the
                // Windows console code page would replace every one of them with a question mark.
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false),
            };

            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(CallTimeout);

            using Process process = StartProcess(startInfo, executable);
            try
            {
                // stderr is drained on its own task throughout: the sidecar logs diagnostics there, and a
                // full pipe would block it mid-response while we wait on stdout for a line that can no
                // longer arrive.
                Task<string> diagnostics = process.StandardError.ReadToEndAsync();

                await process.StandardInput.WriteLineAsync(JsonConvert.SerializeObject(request)).ConfigureAwait(false);
                await process.StandardInput.FlushAsync().ConfigureAwait(false);
                process.StandardInput.Close();

                string? line = await process.StandardOutput.ReadLineAsync(timeout.Token).ConfigureAwait(false);
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);

                string stderr = (await diagnostics.ConfigureAwait(false)).Trim();
                if (stderr.Length > 0)
                    Log.Debug("DWG reader stderr for {Op}: {Diagnostics}", request.Op, stderr);

                if (string.IsNullOrWhiteSpace(line))
                {
                    throw new DwgSidecarException(
                        "no_response",
                        $"The DWG reader exited with code {process.ExitCode} without answering." +
                        (stderr.Length > 0 ? $" It reported: {stderr}" : string.Empty));
                }

                DwgResponse<T>? response = JsonConvert.DeserializeObject<DwgResponse<T>>(line);
                if (response is null)
                    throw new DwgSidecarException("bad_response", "The DWG reader answered with something that is not a response.");

                if (!response.Ok || response.Result is null)
                {
                    DwgWireError error = response.Error ?? new DwgWireError { Code = "unknown", Message = "no error detail" };
                    throw new DwgSidecarException(error.Code, error.Message);
                }

                // Suppressed rather than constrained: T is unconstrained so the compiler cannot see
                // that the null check above already settled it.
                return response.Result!;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                Kill(process);
                throw new DwgSidecarException(
                    "timeout", $"The DWG reader did not answer within {CallTimeout.TotalMinutes:0} minutes.");
            }
            catch (OperationCanceledException)
            {
                Kill(process);
                throw;
            }
            catch (JsonException e)
            {
                throw new DwgSidecarException("bad_response", $"The DWG reader's answer could not be read: {e.Message}");
            }
            finally
            {
                Kill(process);
            }
        }

        private static Process StartProcess(ProcessStartInfo startInfo, string executable)
        {
            try
            {
                return Process.Start(startInfo)
                       ?? throw new DwgSidecarException("sidecar_missing", $"Could not start the DWG reader at '{executable}'.");
            }
            catch (Exception e) when (e is not DwgSidecarException)
            {
                throw new DwgSidecarException(
                    "sidecar_missing",
                    $"Could not start the DWG reader at '{executable}': {e.Message}. It ships in the plugin's " +
                    $"'{SubFolder}' folder; build it with `cargo build --release` in src/AnalyseTool.Dwg.Sidecar, " +
                    $"or point {PathOverrideVariable} at your own build.");
            }
        }

        /// <summary>Kills the process if it is still alive. Called on every exit path including the happy
        /// one, where it is a no-op — a reader left running because an exception took the normal path out
        /// would hold a file handle on the user's drawing.</summary>
        private static void Kill(Process process)
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch (Exception e)
            {
                Log.Debug(e, "Could not stop the DWG reader process");
            }
        }

        /// <summary>Where the sidecar lives: the override variable, else the 'dwg' folder next to this
        /// assembly, which is where <c>PluginAssets.targets</c> deploys it.</summary>
        internal static string ResolveExecutable()
        {
            string? overridePath = Environment.GetEnvironmentVariable(PathOverrideVariable);
            if (!string.IsNullOrWhiteSpace(overridePath)) return overridePath!;

            string root = Path.GetDirectoryName(typeof(DwgSidecarClient).Assembly.Location) ?? string.Empty;
            return Path.Combine(root, SubFolder, ExecutableName);
        }
    }
}
