namespace AnalyseTool.Sdk
{
    /// <summary>
    /// The contract every AnalyseTool command implements. The host discovers implementing types,
    /// registers each as a command (named by <see cref="RevitCommandAttribute"/> or the class name),
    /// and makes it callable from JS (<c>AT.invoke</c>) and from AI clients over MCP.
    /// </summary>
    public interface IRevitTask
    {
        /// <summary>
        /// Runs the command and returns a serializable result (or <c>null</c>). Touch the Revit model
        /// only inside <see cref="IRevitContext.RunInRevitAsync{T}"/>; do slow I/O outside it. Throw to
        /// report an error — the message is delivered back to the caller.
        /// </summary>
        /// <param name="context">Carries the request payload and the Revit-thread marshaller.</param>
        /// <param name="cancellationToken">Cancellation for the command.</param>
        Task<object?> ExecuteAsync(IRevitContext context, CancellationToken cancellationToken);
    }
}
