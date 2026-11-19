namespace AnalyseTool.Hosting.Commands;

/// <summary>
///     A command without input — the controller of an ASP.NET Core app. The command name is the class name without the
///     <c>Command</c> suffix, or the name from <see cref="CommandAttribute" />.
/// </summary>
/// <typeparam name="TResponse">The result type; <see cref="Empty" /> for no result.</typeparam>
/// <remarks>
///     Dependencies come through the constructor; a new instance is created in its own scope for every call. Touch the Revit
///     model only through <c>IRevitDispatcher</c> or an add-in service over it; which document to work with is the add-in's
///     decision, not the module's.
/// </remarks>
[PublicAPI]
public interface ICommand<TResponse>
{
    /// <summary>Runs the command.</summary>
    Task<TResponse> ExecuteAsync(CommandContext context);
}

/// <summary>
///     A command with input. The payload of the call is deserialized into <typeparamref name="TRequest" /> before the call.
///     A call without a payload runs with the defaults when every input of the request is optional — a record whose
///     parameters all have defaults — and fails with <c>bad_request</c> otherwise.
/// </summary>
/// <typeparam name="TRequest">The input type; must be a concrete class or struct.</typeparam>
/// <typeparam name="TResponse">The result type; <see cref="Empty" /> for no result.</typeparam>
/// <inheritdoc cref="ICommand{TResponse}" path="/remarks" />
[PublicAPI]
public interface ICommand<TRequest, TResponse>
{
    /// <summary>Runs the command.</summary>
    Task<TResponse> ExecuteAsync(TRequest request, CommandContext context);
}

/// <summary>
///     «No result» for <see cref="ICommand{TResponse}" /> and <see cref="ICommand{TRequest,TResponse}" />; the frontend gets <c>null</c>.
/// </summary>
[PublicAPI]
public readonly record struct Empty
{
    /// <summary>Gets the only value.</summary>
    public static Empty Value => default;
}

/// <summary>
///     Optional metadata of a command class.
/// </summary>
/// <param name="name">Overrides the command name; by default the class name without the <c>Command</c> suffix.</param>
[PublicAPI]
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class CommandAttribute(string? name = null) : Attribute
{
    /// <summary>Gets the explicit command name, if any.</summary>
    public string? Name { get; } = name;

    /// <summary>Gets or sets what the command does, for people and AI clients.</summary>
    public string? Description { get; set; }
}
