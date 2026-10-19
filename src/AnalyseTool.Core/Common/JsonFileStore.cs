using Newtonsoft.Json;
using Serilog;
using System.IO;

namespace AnalyseTool.Core.Common
{
    /// <summary>
    /// One small JSON settings file under the profile folder: read, write, and read-modify-write under
    /// one lock. The settings stores (code execution, extension state, extension sources) each used to
    /// carry their own copy of load / save / swallow, and the copies shared three faults:
    /// <list type="bullet">
    /// <item>writes were not atomic — a crash or a full disk mid-write left a torn file, which the next
    /// load read as "no settings" and silently replaced with defaults (registered folders vanished);</item>
    /// <item>read-modify-write had no lock, so a change from the Settings page and one over MCP could
    /// both read the old file and the second save would undo the first;</item>
    /// <item>every failure was swallowed without a word.</item>
    /// </list>
    /// Here a write goes to a temp file that then replaces the real one, a change is read and written
    /// under the store's lock, and failures are logged. Missing file = defaults, quietly: that is the
    /// normal first run.
    /// </summary>
    internal sealed class JsonFileStore<T> where T : class, new()
    {
        private readonly string _path;
        private readonly object _gate = new();

        public JsonFileStore(string path) => _path = path;

        public string Path => _path;

        /// <summary>The stored value, or a new default when the file is missing or unreadable.</summary>
        public T Read()
        {
            lock (_gate)
            {
                return ReadUnlocked();
            }
        }

        /// <summary>Replaces the stored value. False when it could not be written (logged).</summary>
        public bool Write(T value)
        {
            lock (_gate)
            {
                return WriteUnlocked(value);
            }
        }

        /// <summary>Reads, lets <paramref name="change"/> modify, and writes back — all under the lock,
        /// so two concurrent changes both land. Returns the value as written.</summary>
        public T Update(Action<T> change)
        {
            lock (_gate)
            {
                T value = ReadUnlocked();
                change(value);
                WriteUnlocked(value);
                return value;
            }
        }

        private T ReadUnlocked()
        {
            try
            {
                if (File.Exists(_path))
                    return JsonConvert.DeserializeObject<T>(File.ReadAllText(_path)) ?? new T();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                Log.Warning(ex, "Settings file {Path} could not be read; defaults apply until it is written again", _path);
            }
            return new T();
        }

        private bool WriteUnlocked(T value)
        {
            // Unique per write: a temp name shared by two processes (two Revit sessions) would collide.
            string temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
                File.WriteAllText(temp, JsonConvert.SerializeObject(value, Formatting.Indented));
                // Same folder, so a rename: the real file is either the old content or the new, never half.
                File.Move(temp, _path, overwrite: true);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warning(ex, "Settings file {Path} could not be written", _path);
                try { File.Delete(temp); } catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
                return false;
            }
        }
    }
}
