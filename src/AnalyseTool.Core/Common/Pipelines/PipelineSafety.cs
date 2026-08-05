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

        /// <summary>The two other nodes the palette has to recognise by name — the narrowing step and the
        /// hand-off — so that the role of every command is decided in ONE place.</summary>
        public const string FilterCommand = "Filter";
        public const string ReportCommand = "ExportToCsv";

        /// <summary>
        /// What part a command plays in a pipeline, in the order the parts usually appear in one:
        /// <c>read → narrow → ai → gate → write → report</c>.
        ///
        /// <para>Computed here and published by <c>GetCommands</c> rather than worked out again in the
        /// editor. The frontend already needs to know which node is an AI and which is the gate, and a
        /// second list over there would be a second answer to a question this file exists to answer —
        /// the two would agree until the day one of them was edited.</para>
        ///
        /// <para>It is also what turns a palette of 72 alphabetical names into the shape of a pipeline:
        /// read the model, narrow what came back, ask a model about it, gate the answer, write, hand
        /// off. Reading the groups top to bottom is reading the skeleton of the graph.</para>
        /// </summary>
        public static string RoleOf(string? command, bool readOnly, bool destructive)
        {
            // Named roles first: Filter, AiTransform, Approval and ExportToCsv are all read-only, so
            // testing the flags first would bury every one of them in "read".
            if (string.Equals(command, FilterCommand, StringComparison.OrdinalIgnoreCase)) return "narrow";
            if (IsAi(command)) return "ai";
            if (IsApproval(command)) return "gate";
            if (string.Equals(command, ReportCommand, StringComparison.OrdinalIgnoreCase)) return "report";

            if (destructive) return "write";
            if (readOnly) return "read";
            return "other";
        }

        public static bool IsAi(string? command) => command is not null && Ai.Contains(command);

        public static bool IsApproval(string? command) =>
            string.Equals(command, ApprovalCommand, StringComparison.OrdinalIgnoreCase);

        /// <summary>True when the cost of a wrong automatic decision does not scale down with
        /// <c>maxItems</c> — a deletion, a purge, or arbitrary code about which nothing can be reasoned.</summary>
        public static bool IsIrreversible(string? command) =>
            command is not null && !Reversible.Contains(command);
    }
}
