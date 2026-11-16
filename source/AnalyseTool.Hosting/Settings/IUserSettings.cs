using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;

namespace AnalyseTool.Hosting.Settings;

/// <summary>
///     Writes user settings — the layer of the configuration that the user changes in the interface.
/// </summary>
/// <remarks>
///     Read settings as usual, through <c>IOptions&lt;T&gt;</c> or <c>IOptionsMonitor&lt;T&gt;</c>: the file is reloaded on change,
///     so <c>IOptionsMonitor</c> sees an update right after <see cref="UpdateAsync{T}" />. Not for secrets — those go to DPAPI.
/// </remarks>
/// <example>
///     <code>
///     await userSettings.UpdateAsync&lt;AiOptions&gt;("Ai", options => options.Model = "gpt");
///     </code>
/// </example>
[PublicAPI]
public interface IUserSettings
{
    /// <summary>Gets the path of the user settings file.</summary>
    string FilePath { get; }

    /// <summary>
    ///     Reads the <paramref name="section" /> as <typeparamref name="T" />, applies <paramref name="update" /> and writes it back.
    /// </summary>
    /// <remarks>
    ///     The whole section is written, with the values from every layer below it. After the first write <c>appsettings.json</c>
    ///     no longer changes this section for this user: a new default in a later release reaches only users who never saved it.
    /// </remarks>
    /// <param name="section">The configuration section, nested sections separated by <c>:</c>.</param>
    /// <param name="update">Changes the current values.</param>
    /// <param name="cancellationToken">Cancels waiting for another write to finish.</param>
    Task UpdateAsync<T>(string section, Action<T> update, CancellationToken cancellationToken = default) where T : class, new();
}

/// <summary>
///     Stores user settings in a JSON file that is also a configuration source.
/// </summary>
internal sealed class UserSettings(string filePath, IConfiguration configuration) : IUserSettings
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    /// <inheritdoc />
    public string FilePath { get; } = filePath;

    /// <inheritdoc />
    public async Task UpdateAsync<T>(string section, Action<T> update, CancellationToken cancellationToken = default) where T : class, new()
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var value = configuration.GetSection(section).Get<T>() ?? new T();
            update(value);

            var root = await ReadRootAsync(cancellationToken).ConfigureAwait(false);
            SetSection(root, section, JsonSerializer.SerializeToNode(value, SerializerOptions));
            await WriteRootAsync(root, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task<JsonObject> ReadRootAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(FilePath))
        {
            return [];
        }

        var text = await File.ReadAllTextAsync(FilePath, cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(text) ? [] : JsonNode.Parse(text) as JsonObject ?? [];
    }

    private async Task WriteRootAsync(JsonObject root, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

        // Write to a temporary file first: a crash mid-write must not leave a broken settings file
        var temporaryPath = FilePath + ".tmp";
        await File.WriteAllTextAsync(temporaryPath, root.ToJsonString(SerializerOptions), cancellationToken).ConfigureAwait(false);
        File.Move(temporaryPath, FilePath, overwrite: true);
    }

    private static void SetSection(JsonObject root, string section, JsonNode? value)
    {
        var keys = section.Split(':');
        var current = root;

        foreach (var key in keys[..^1])
        {
            if (current[key] is not JsonObject child)
            {
                child = [];
                current[key] = child;
            }

            current = child;
        }

        current[keys[^1]] = value;
    }
}
