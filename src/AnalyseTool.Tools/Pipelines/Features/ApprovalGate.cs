using AnalyseTool.Sdk;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.ComponentModel;
using System.Text.RegularExpressions;

namespace AnalyseTool.Tools.Pipelines
{
    /// <summary>
    /// The gate between a proposal and the model. **A filter, not a barrier.**
    ///
    /// <para>Stated as a barrier — the run halts until someone clicks — the invariant "AI never writes
    /// without a human" would kill the point of a pipeline, which is press and forget. So this does not
    /// stop the run. Rows that pass a machine-checkable predicate flow on and get written; the rest are
    /// parked with the reason they were parked, and <b>the run completes</b>. You come back to 400
    /// renamed families and 12 questions, not to a pipeline standing on node 3.</para>
    ///
    /// <para>The predicate lives in the <c>.atpipe</c> — versioned, reviewable, travelling with the
    /// pipeline — and NOT as a "trust the AI" setting. A global toggle is a global answer to a per-case
    /// question: ticked once in a confident moment, it silently covers every future run.</para>
    ///
    /// <para>The three conditions are NOT of equal strength, and treating them as interchangeable is the
    /// trap:</para>
    /// <list type="bullet">
    ///   <item><c>valueMatches</c> — checks the VALUE against a rule. Strong: verified against reality.
    ///     A naming standard is already a regex, so "the model proposed a name, it matches the standard,
    ///     fewer than 50 rows" is a fully mechanical check that needs no human at all.</item>
    ///   <item><c>maxItems</c> — bounds blast radius. Medium: bounds damage, not correctness.</item>
    ///   <item><c>minConfidence</c> — what the model said about ITSELF. Weak. An LLM's self-reported
    ///     confidence is not calibrated and is least reliable on exactly the atypical rows. A
    ///     tiebreaker, never a sole criterion — and this node refuses to run on it alone.</item>
    /// </list>
    ///
    /// <para>No <c>autoAccept</c> at all parks everything. That is the safe default, not an oversight:
    /// a gate whose rules were not filled in must not be a gate that is open.</para>
    /// </summary>
    [RevitCommand("Approval",
        Description = "Splits rows into what may be written automatically and what a person must look at. " +
                      "Payload: { items, field, autoAccept: { valueMatches, maxItems, minConfidence } }. " +
                      "Rows passing every declared condition come back in 'accepted'; the rest in 'parked' " +
                      "with the reason. The run CONTINUES either way — bind a write command to 'accepted' " +
                      "and ExportToCsv to 'parked'. No autoAccept parks everything. 'minConfidence' alone " +
                      "is REFUSED: a model's opinion of itself is not evidence. Returns " +
                      "{ accepted, parked, acceptedCount, parkedCount }.",
        ReadOnly = true,
        InputType = typeof(ApprovalGate.Request),
        OutputType = typeof(ApprovalResult))]
    internal sealed class ApprovalGate : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct) =>
            Task.FromResult<object?>(Apply(ctx.Payload.As<Request>() ?? new Request()));

        internal static ApprovalResult Apply(Request req)
        {
            JArray rows = req.Items is { Count: > 0 } ? JArray.FromObject(req.Items) : new JArray();
            AcceptRule? rule = req.AutoAccept;

            string? refusal = Refuse(rule, req.Field);
            if (refusal is not null) throw new InvalidOperationException(refusal);

            JArray accepted = new();
            JArray parked = new();

            foreach (JToken item in rows)
            {
                JObject row = item is JObject o ? (JObject)o.DeepClone() : new JObject { ["value"] = item.DeepClone() };
                string? why = Reject(row, rule, req.Field);

                row["approval"] = new JObject
                {
                    ["accepted"] = why is null,
                    ["why"] = why ?? "matched every auto-accept condition",
                };

                (why is null ? accepted : parked).Add(row);
            }

            // Over the blast radius, so NOTHING goes through. Taking an arbitrary first N would bound the
            // damage and hide the fact that the situation is not the one the author had in mind — which
            // is the "cheerful 0 written" failure wearing a different hat.
            if (rule?.MaxItems is > 0 && accepted.Count > rule.MaxItems)
            {
                foreach (JToken row in accepted)
                {
                    ((JObject)row["approval"]!)["accepted"] = false;
                    ((JObject)row["approval"]!)["why"] =
                        $"{accepted.Count} rows passed but the limit is {rule.MaxItems}; " +
                        "more changed than this pipeline expected, so all of them need a look.";
                    parked.Add(row);
                }
                accepted = new JArray();
            }

            return new ApprovalResult(
                accepted.Cast<object>().ToList(), parked.Cast<object>().ToList(), accepted.Count, parked.Count);
        }

        /// <summary>Configurations this node will not run under, whatever the rows are.</summary>
        internal static string? Refuse(AcceptRule? rule, string? field)
        {
            if (rule is null) return null; // no rule is a valid rule: everything is parked

            bool hasStrong = !string.IsNullOrWhiteSpace(rule.ValueMatches);
            bool hasBound = rule.MaxItems is > 0;

            if (rule.MinConfidence is > 0 && !hasStrong && !hasBound)
                return "autoAccept declares only 'minConfidence'. A model's confidence in itself is not " +
                       "calibrated and is least reliable on exactly the rows that are unusual, so it can " +
                       "narrow an approval but never grant one. Add 'valueMatches' (a rule the value must " +
                       "satisfy) or at least 'maxItems'.";

            if (hasStrong && string.IsNullOrWhiteSpace(field))
                return "autoAccept has 'valueMatches' but no 'field' saying which value to match it against.";

            if (hasStrong)
            {
                try { _ = new Regex(rule.ValueMatches!); }
                catch (ArgumentException ex)
                {
                    return $"'valueMatches' is not a valid regular expression: {ex.Message}";
                }
            }

            return null;
        }

        /// <summary>Why this row may not go through automatically, or null when it may.</summary>
        private static string? Reject(JObject row, AcceptRule? rule, string? field)
        {
            if (rule is null)
                return "no autoAccept rule on this node, so every row waits for a person";

            if (!string.IsNullOrWhiteSpace(rule.ValueMatches))
            {
                JToken? value = row.SelectToken(field!);
                if (value is null || value.Type == JTokenType.Null)
                    return $"'{field}' is missing, so there is nothing to check against the rule";

                if (!Regex.IsMatch(value.ToString(), rule.ValueMatches!))
                    return $"'{field}' is \"{value}\", which does not match {rule.ValueMatches}";
            }

            if (rule.MinConfidence is > 0)
            {
                JToken? confidence = row.SelectToken("ai.confidence");
                if (confidence is null || confidence.Type is not (JTokenType.Float or JTokenType.Integer))
                    return "no ai.confidence on this row, and the rule asks for one";

                if (confidence.Value<double>() < rule.MinConfidence)
                    return $"confidence {confidence.Value<double>():0.##} is below {rule.MinConfidence}";
            }

            return null;
        }

        internal sealed class Request
        {
            [Description("The rows to gate. Usually bound from an AI node's 'items'.")]
            public List<object>? Items { get; set; }

            [Description("Which field of each row 'valueMatches' is checked against, e.g. \"mark\". " +
                         "May be a dotted path.")]
            public string? Field { get; set; }

            [Description("What may pass without a person. Omit to park everything.")]
            public AcceptRule? AutoAccept { get; set; }
        }

        internal sealed class AcceptRule
        {
            [Description("Regular expression the row's 'field' must match. The strong condition — it " +
                         "checks the value against a rule, e.g. a naming standard.")]
            public string? ValueMatches { get; set; }

            [Description("If more rows than this would pass, NONE do — more changed than the pipeline " +
                         "expected. Bounds damage, not correctness.")]
            public int? MaxItems { get; set; }

            [Description("Minimum ai.confidence. The WEAK condition: it may narrow an approval, never " +
                         "grant one, so it is refused on its own.")]
            public double? MinConfidence { get; set; }
        }
    }

    internal sealed record ApprovalResult(
        [property: JsonProperty("accepted")] IReadOnlyList<object> Accepted,
        [property: JsonProperty("parked")] IReadOnlyList<object> Parked,
        [property: JsonProperty("acceptedCount")] int AcceptedCount,
        [property: JsonProperty("parkedCount")] int ParkedCount);
}
