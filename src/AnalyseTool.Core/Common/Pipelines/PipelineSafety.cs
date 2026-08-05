namespace AnalyseTool.Core.Common.Pipelines
{
    /// <summary>
    /// What the host knows about danger that a command cannot declare about itself.
    ///
    /// <para>Lists here rather than properties on <c>[RevitCommand]</c>, on the precedent this document
    /// already set for <c>RequiresUser</c>: a flag on the public contract is forever, and neither of
    /// these questions is one an author answers about their own command. "Is this node an AI?" is
    /// answered by whoever ships the AI nodes. "Is this deletion recoverable?" is a judgement about the
    /// cost of being wrong, and an author who thinks their command is safe is exactly whose opinion
    /// should not be load-bearing. Add a contract property when a third-party case makes one necessary.</para>
    /// </summary>
    internal static class PipelineSafety
    {
        /// <summary>Nodes whose output is a MODEL'S PROPOSAL rather than a reading of the document.
        /// What reaches a destructive command from one of these must pass an approval first.</summary>
        private static readonly HashSet<string> Ai = new(StringComparer.OrdinalIgnoreCase)
        {
            "AiTransform",
            "OllamaSuggestNames",
            "OllamaSuggestName",
            "OllamaSuggestTemplate",
            "OllamaEditParameters",
        };

        /// <summary>The gate itself, by name, since the validator has to recognise it in a graph.</summary>
        public const string ApprovalCommand = "Approval";

        /// <summary>
        /// Commands whose effect cannot be undone by running something else, so an approval in front of
        /// one is a BARRIER at any setting — <c>autoAccept</c> is refused there however it is written.
        ///
        /// <para>An INVERTED allowlist: loosening is permitted only for commands the host knows are
        /// reversible, so every command not named here — including every third-party one — keeps the
        /// strict behaviour. A list of "dangerous" names would have the opposite failure: anything
        /// nobody thought of would be treated as safe.</para>
        /// </summary>
        private static readonly HashSet<string> Reversible = new(StringComparer.OrdinalIgnoreCase)
        {
            "SetDataToParameters",
            "RenameFamily",
            "RenameFamilyType",
            "SetInstancesWorkset",
        };

        public static bool IsAi(string? command) => command is not null && Ai.Contains(command);

        public static bool IsApproval(string? command) =>
            string.Equals(command, ApprovalCommand, StringComparison.OrdinalIgnoreCase);

        /// <summary>True when the cost of a wrong automatic decision does not scale down with
        /// <c>maxItems</c> — a deletion, a purge, or arbitrary code about which nothing can be reasoned.</summary>
        public static bool IsIrreversible(string? command) =>
            command is not null && !Reversible.Contains(command);
    }
}
