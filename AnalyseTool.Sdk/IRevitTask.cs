namespace AnalyseTool.Sdk
{
    /// <summary>
    /// A command with no declared input. The whole JSON payload (if any) is available via
    /// <see cref="IRevitContext.Payload"/>.
    /// </summary>
    public interface IRevitTask
    {
        Task<object?> ExecuteAsync(IRevitContext context, CancellationToken cancellationToken);
    }

    /// <summary>
    /// A command with a typed input. The host deserializes the incoming JSON payload into
    /// <typeparamref name="TInput"/> and uses the type's shape to publish a JSON Schema to MCP
    /// clients — so an AI knows exactly which arguments the command accepts. Implement via the
    /// <see cref="RevitTask{TInput}"/> base class.
    /// </summary>
    public interface IRevitTask<in TInput> : IRevitTask
    {
        Task<object?> ExecuteAsync(TInput input, IRevitContext context, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Base class for a typed-input command. Implement
    /// <see cref="ExecuteAsync(TInput, IRevitContext, CancellationToken)"/> — the payload is
    /// deserialized into <typeparamref name="TInput"/> for you (no manual <c>ctx.Payload.As&lt;T&gt;()</c>).
    /// The <typeparamref name="TInput"/> type also drives the MCP tool's input schema.
    /// </summary>
    public abstract class RevitTask<TInput> : IRevitTask<TInput>
    {
        Task<object?> IRevitTask.ExecuteAsync(IRevitContext context, CancellationToken cancellationToken)
            => ExecuteAsync(context.Payload.As<TInput>()!, context, cancellationToken);

        public abstract Task<object?> ExecuteAsync(TInput input, IRevitContext context, CancellationToken cancellationToken);
    }
}
