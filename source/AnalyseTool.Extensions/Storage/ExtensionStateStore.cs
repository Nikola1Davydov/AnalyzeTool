using System.Text.Json.Nodes;

namespace AnalyseTool.Extensions.Storage;

/// <summary>
///     The user's choices about extensions that do not belong in the extension folder: which ones are off, and which version
///     an extension is pinned to. Kept in <c>extensions-state.json</c>, so pinning a version never changes the history it
///     points into.
/// </summary>
public sealed class ExtensionStateStore(ExtensionPaths paths)
{
    private readonly Lock _lock = new();

    /// <summary>Whether the extension is on; every extension is on until the user turns it off.</summary>
    public bool IsEnabled(string extensionId)
    {
        lock (_lock)
        {
            return !Read().Disabled.Contains(extensionId);
        }
    }

    /// <summary>Turns the extension on or off.</summary>
    public void SetEnabled(string extensionId, bool enabled)
    {
        lock (_lock)
        {
            var state = Read();
            if (enabled ? state.Disabled.Remove(extensionId) : state.Disabled.Add(extensionId))
            {
                Write(state);
            }
        }
    }

    /// <summary>Gets the version the extension is pinned to; <see langword="null" /> when it runs its current files.</summary>
    public string? GetPinnedVersion(string extensionId)
    {
        lock (_lock)
        {
            return Read().Pinned.GetValueOrDefault(extensionId);
        }
    }

    /// <summary>Pins the extension to a version, or unpins it with <see langword="null" />.</summary>
    public void SetPinnedVersion(string extensionId, string? versionId)
    {
        lock (_lock)
        {
            var state = Read();
            var changed = versionId is null ? state.Pinned.Remove(extensionId) : state.Pinned.GetValueOrDefault(extensionId) != versionId;
            if (versionId is not null)
            {
                state.Pinned[extensionId] = versionId;
            }

            if (changed)
            {
                Write(state);
            }
        }
    }

    private State Read()
    {
        var node = JsonFile.Read(paths.StateFile);
        var state = new State();
        if (node?["disabled"] is JsonArray disabled)
        {
            foreach (var id in disabled.Select(item => item?.GetValue<string>()).OfType<string>())
            {
                state.Disabled.Add(id);
            }
        }

        if (node?["pinned"] is JsonObject pinned)
        {
            foreach (var (id, version) in pinned)
            {
                if (version?.GetValue<string>() is { Length: > 0 } sha)
                {
                    state.Pinned[id] = sha;
                }
            }
        }

        return state;
    }

    private void Write(State state)
    {
        var pinned = new JsonObject();
        foreach (var (id, version) in state.Pinned.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
        {
            pinned[id] = version;
        }

        JsonFile.Write(paths.StateFile, new JsonObject
        {
            ["disabled"] = new JsonArray(state.Disabled.Order(StringComparer.OrdinalIgnoreCase).Select(id => (JsonNode)id).ToArray()),
            ["pinned"] = pinned
        });
    }

    private sealed class State
    {
        public HashSet<string> Disabled { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> Pinned { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
