using AnalyseTool.Sdk;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.ComponentModel;
using System.Globalization;

namespace AnalyseTool.Tools.Pipelines
{
    /// <summary>
    /// Narrows a list between two nodes: 300 warnings in, the 12 that matter out.
    ///
    /// <para>An ordinary command, and that is the point — it needs no capability flag and no special case
    /// in the engine, because it simply never calls <c>RunInRevitAsync</c> and therefore never touches the
    /// Revit thread. "Orchestrator node" describes what a command does, not a mode it runs in.</para>
    ///
    /// <para>Field comparison only. Deciding which warnings are "actually serious" is a judgement about
    /// meaning, not about values, and that is an AI node's job (#92) — this one stays predictable.</para>
    /// </summary>
    [RevitCommand("Filter",
        Description = "Keeps the items of a list that match the given field conditions. " +
                      "Payload: { items: [...], where: [{ field, op, value }], match: \"all\" | \"any\" }. " +
                      "Ops: equals, notEquals, contains, exists, notExists, greaterThan, lessThan. " +
                      "'field' may be a dotted path. Returns { items, kept, dropped }. " +
                      "EXAMPLE — unused family types: " +
                      "{ \"where\": [ { \"field\": \"instanceCount\", \"op\": \"equals\", \"value\": 0 } ] }. " +
                      "IN A PIPELINE the conditions go in the node's \"params\", NOT on the node itself: " +
                      "{ \"id\": \"unused\", \"command\": \"Filter\", \"params\": { \"where\": [...] }, " +
                      "\"bind\": { \"items\": \"listTypes.items\" } }. " +
                      "WITHOUT conditions it passes EVERYTHING through, which is refused before a " +
                      "model-changing node: filtering nothing and then purging means purging everything.",
        ReadOnly = true,
        InputType = typeof(FilterItems.Request),
        OutputType = typeof(FilterResult))]
    internal sealed class FilterItems : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct) =>
            Task.FromResult<object?>(Apply(ctx.Payload.As<Request>() ?? new Request()));

        /// <summary>
        /// The whole of what this command does, as a function of its request.
        ///
        /// <para>Separated from <see cref="ExecuteAsync"/> so it can be tested at all. Nothing here goes
        /// near Revit — this command never calls <c>RunInRevitAsync</c> — but reaching it through the
        /// interface would drag <c>UIApplication</c> into every fake context, and a test that needs Revit
        /// installed is a test nobody runs. Filter decides what a purge deletes; "you can read the JSON
        /// afterwards" is not a way to know it is right.</para>
        /// </summary>
        internal static FilterResult Apply(Request req)
        {
            JArray items = req.Items is { Count: > 0 } ? JArray.FromObject(req.Items) : new JArray();

            if (req.Where is not { Count: > 0 })
                // No conditions is not an error and not "keep nothing": a node whose rules were not filled
                // in yet should pass its input through, so a half-built pipeline still runs end to end.
                // What makes that safe is the graph validator, which refuses this shape in front of a
                // command that changes the model — the node itself cannot see what it feeds.
                return new FilterResult(Rows(items), items.Count, 0);

            bool matchAll = !string.Equals(req.Match, "any", StringComparison.OrdinalIgnoreCase);

            List<JToken> kept = items.Where(item =>
                matchAll ? req.Where.All(c => Matches(item, c)) : req.Where.Any(c => Matches(item, c))).ToList();

            return new FilterResult(Rows(kept), kept.Count, items.Count - kept.Count);
        }

        /// <summary>Hands the rows back as plain objects, for the same reason the request takes them that
        /// way — a declared <c>JArray</c> publishes a schema that refers to itself.</summary>
        private static IReadOnlyList<object> Rows(IEnumerable<JToken> items) => items.Cast<object>().ToList();

        private static bool Matches(JToken item, Condition condition)
        {
            if (string.IsNullOrWhiteSpace(condition.Field)) return false;

            JToken? actual = item.SelectToken(condition.Field);
            string op = condition.Op ?? "equals";

            // Existence is answered before anything else: every other operator on a missing field is
            // false, rather than accidentally true by comparing null to null.
            if (string.Equals(op, "exists", StringComparison.OrdinalIgnoreCase))
                return actual is not null && actual.Type != JTokenType.Null;
            if (string.Equals(op, "notExists", StringComparison.OrdinalIgnoreCase))
                return actual is null || actual.Type == JTokenType.Null;
            if (actual is null) return false;

            string actualText = actual.Type == JTokenType.Null ? string.Empty : actual.ToString();
            string expectedText = condition.Value?.ToString() ?? string.Empty;

            return op.ToLowerInvariant() switch
            {
                "equals" => string.Equals(actualText, expectedText, StringComparison.OrdinalIgnoreCase),
                "notequals" => !string.Equals(actualText, expectedText, StringComparison.OrdinalIgnoreCase),
                "contains" => actualText.Contains(expectedText, StringComparison.OrdinalIgnoreCase),
                "greaterthan" => Compare(actualText, expectedText) > 0,
                "lessthan" => Compare(actualText, expectedText) < 0,
                _ => false, // an unknown operator keeps nothing, so a typo is visible instead of silently passing everything
            };
        }

        /// <summary>Numeric when both sides are numbers, ordinal text otherwise — so "10" &gt; "9" holds,
        /// which string comparison alone would get backwards.</summary>
        private static int Compare(string left, string right)
        {
            if (decimal.TryParse(left, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal a) &&
                decimal.TryParse(right, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal b))
                return a.CompareTo(b);

            return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Both free-form members are declared as <see cref="object"/>, NOT as Newtonsoft
        /// <c>JArray</c>/<c>JToken</c>. Those implement <c>IEnumerable&lt;JToken&gt;</c>, so the schema
        /// generator reads them as "an array of JToken" — of "an array of JToken" — and publishes a
        /// <c>$ref</c> pointing at itself with <c>type: ["array","null"]</c>. The result forbids a scalar
        /// <c>value</c> outright: an agent could not write <c>{ "op": "equals", "value": 0 }</c> against
        /// the contract we had published, which is exactly how the first agent-authored pipeline came out
        /// with its conditions in the wrong place. <c>object</c> publishes the permissive schema we
        /// actually mean, and Newtonsoft still hands back JObject/JValue at run time.
        /// </summary>
        internal sealed class Request
        {
            [Description("The list to filter. Usually bound from a previous node.")]
            public List<object>? Items { get; set; }

            [Description("Conditions to apply. Empty keeps everything.")]
            public List<Condition>? Where { get; set; }

            [Description("\"all\" (default) requires every condition; \"any\" requires one.")]
            public string? Match { get; set; }
        }

        internal sealed class Condition
        {
            [Description("Field name, or a dotted path into the item (e.g. \"parameters.mark\").")]
            public string? Field { get; set; }

            [Description("equals, notEquals, contains, exists, notExists, greaterThan, lessThan.")]
            public string? Op { get; set; }

            [Description("Value to compare against, e.g. 0 or \"Wall\". Ignored by exists / notExists.")]
            public object? Value { get; set; }
        }
    }

    /// <summary>
    /// The kept items plus what was dropped.
    ///
    /// <para><see cref="Items"/> is untyped, and no declaration could fix that: this command's output IS
    /// its input, so its element type is whatever the previous node produced. The ENVELOPE is still worth
    /// declaring — a consumer knows it gets an object with these three fields — which is also what lets
    /// the node take part in MCP structured output at all, where a bare array could not.</para>
    ///
    /// <para><see cref="Dropped"/> earns its place: "kept 12" reads the same whether 300 came in or 12
    /// did, and in an unattended run nobody is watching to notice the difference.</para>
    /// </summary>
    internal sealed record FilterResult(
        [property: JsonProperty("items")] IReadOnlyList<object> Items,
        [property: JsonProperty("kept")] int Kept,
        [property: JsonProperty("dropped")] int Dropped);
}
