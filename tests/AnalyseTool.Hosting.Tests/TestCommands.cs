using AnalyseTool.Hosting.Commands;
using AnalyseTool.Hosting.Mcp;

namespace AnalyseTool.Hosting.Tests;

// Command classes for the tests. They are registered explicitly with FromTypes, never by scanning this assembly:
// some of them are broken on purpose.

public sealed record GreetRequest(string Name);

[Command(Description = "Greets by name.")]
public sealed class GreetCommand : ICommand<GreetRequest, string>
{
    public Task<string> ExecuteAsync(GreetRequest request, CommandContext context)
    {
        return Task.FromResult($"Hello, {request.Name}");
    }
}

public sealed class DoNothing : ICommand<Empty>
{
    public Task<Empty> ExecuteAsync(CommandContext context)
    {
        return Task.FromResult(Empty.Value);
    }
}

[Command("Renamed")]
public sealed class OldNameCommand : ICommand<int>
{
    public Task<int> ExecuteAsync(CommandContext context)
    {
        return Task.FromResult(42);
    }
}

public sealed class RefuseCommand : ICommand<int>
{
    public Task<int> ExecuteAsync(CommandContext context)
    {
        throw new CommandException("refused", "Not today.");
    }
}

public sealed class CrashCommand : ICommand<int>
{
    public Task<int> ExecuteAsync(CommandContext context)
    {
        throw new InvalidOperationException("Boom.");
    }
}

/// <summary>Records what it saw and waits for <see cref="Recorder.Gate" /> — for concurrency and middleware tests.</summary>
public sealed class ProbeCommand(Recorder recorder) : ICommand<string>
{
    public async Task<string> ExecuteAsync(CommandContext context)
    {
        recorder.Add($"command:{context.Source}");
        await recorder.Gate.Task.WaitAsync(context.CancellationToken);
        return "done";
    }
}

public sealed class Recorder
{
    private readonly List<string> _entries = [];

    public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource<string> Ran { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public IReadOnlyList<string> Entries
    {
        get
        {
            lock (_entries)
            {
                return _entries.ToList();
            }
        }
    }

    public void Add(string entry)
    {
        lock (_entries)
        {
            _entries.Add(entry);
        }

        Ran.TrySetResult(entry);
    }
}

// Broken commands: each one must stop the registration

public sealed class DuplicateA : ICommand<int>
{
    public Task<int> ExecuteAsync(CommandContext context) => Task.FromResult(1);
}

[Command("DuplicateA")]
public sealed class DuplicateB : ICommand<int>
{
    public Task<int> ExecuteAsync(CommandContext context) => Task.FromResult(2);
}

[Command("My.Command")]
public sealed class DottedName : ICommand<int>
{
    public Task<int> ExecuteAsync(CommandContext context) => Task.FromResult(0);
}

public abstract record AbstractRequest;

public sealed class AbstractRequestCommand : ICommand<AbstractRequest, int>
{
    public Task<int> ExecuteAsync(AbstractRequest request, CommandContext context) => Task.FromResult(0);
}

public sealed class TwoInterfaces : ICommand<int>, ICommand<string, int>
{
    public Task<int> ExecuteAsync(CommandContext context) => Task.FromResult(0);

    public Task<int> ExecuteAsync(string request, CommandContext context) => Task.FromResult(0);
}

// MCP tools: commands marked with [McpTool]

[McpTool(ReadOnly = true)]
[Command(Description = "Greets by name over MCP.")]
public sealed class McpGreetCommand : ICommand<GreetRequest, string>
{
    public Task<string> ExecuteAsync(GreetRequest request, CommandContext context)
    {
        return Task.FromResult($"Hello, {request.Name} ({context.Source})");
    }
}

public sealed record McpPoint(int X, int Y);

[McpTool]
[Command(Description = "Returns a point.")]
public sealed class McpPointCommand : ICommand<McpPoint>
{
    public Task<McpPoint> ExecuteAsync(CommandContext context)
    {
        return Task.FromResult(new McpPoint(1, 2));
    }
}

[McpTool]
[Command(Description = "Always refuses.")]
public sealed class McpRefuseCommand : ICommand<int>
{
    public Task<int> ExecuteAsync(CommandContext context)
    {
        throw new CommandException("refused", "Not over MCP.");
    }
}

[McpTool]
[Command(Description = "Reports half the work, then finishes.")]
public sealed class McpProgressCommand : ICommand<Empty>
{
    public Task<Empty> ExecuteAsync(CommandContext context)
    {
        // Returns right after reporting: the server must still deliver the progress before the result
        context.Progress.Report(new ProgressInfo(0.5, "Half"));
        return Task.FromResult(Empty.Value);
    }
}

// Broken on purpose: an MCP tool needs a description and an object request

[McpTool]
public sealed class McpUndescribedCommand : ICommand<int>
{
    public Task<int> ExecuteAsync(CommandContext context)
    {
        return Task.FromResult(0);
    }
}

[McpTool]
[Command(Description = "Takes a bare string.")]
public sealed class McpScalarRequestCommand : ICommand<string, int>
{
    public Task<int> ExecuteAsync(string request, CommandContext context)
    {
        return Task.FromResult(request.Length);
    }
}

[McpTool(ReadOnly = true)]
[Command(Description = "Returns a tiny picture.")]
public sealed class McpPictureCommand : ICommand<McpImage>
{
    public Task<McpImage> ExecuteAsync(CommandContext context)
    {
        return Task.FromResult(new McpImage([1, 2, 3], "image/png", "Three bytes."));
    }
}

// Payload rules: a request with only optional inputs may come without a payload; a struct with a required one may not

public sealed record PageRequest(int Size = 10, string? Filter = null);

public sealed class PageCommand : ICommand<PageRequest, string>
{
    public Task<string> ExecuteAsync(PageRequest request, CommandContext context)
    {
        return Task.FromResult($"{request.Size}:{request.Filter ?? "none"}");
    }
}

public readonly record struct OffsetRequest(int Value);

public sealed class OffsetCommand : ICommand<OffsetRequest, int>
{
    public Task<int> ExecuteAsync(OffsetRequest request, CommandContext context)
    {
        return Task.FromResult(request.Value);
    }
}

public struct PlainSettings
{
    public int Level { get; set; }
}
