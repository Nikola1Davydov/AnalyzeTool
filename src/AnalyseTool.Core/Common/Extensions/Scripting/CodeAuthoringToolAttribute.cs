namespace AnalyseTool.Core.Common.Extensions.Scripting
{
    /// <summary>
    /// Marks a built-in command as part of authoring code with AI: an AI caller may reach it only while
    /// C# execution is switched on in Settings (the MCP bridge leaves it out of tools/list and refuses
    /// an invoke otherwise). Windows are not gated by this — a command that must refuse everyone checks
    /// <see cref="CodeExecutionSettings.Enabled"/> itself.
    ///
    /// An attribute on the command rather than a list of names in the bridge: a new authoring command
    /// declares it where it is written, instead of being exposed until someone remembers the list.
    /// Internal, so only the platform's own commands can carry it.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    internal sealed class CodeAuthoringToolAttribute : Attribute
    {
    }
}
