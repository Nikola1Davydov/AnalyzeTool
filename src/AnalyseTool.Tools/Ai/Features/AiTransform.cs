using AnalyseTool.Sdk;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using System.ComponentModel;

namespace AnalyseTool.Tools.Ai
{
    /// <summary>
    /// The prompt between two nodes: rows in, the same rows with the model's answers added, rows out.
    ///
    /// <para>The transformation node of #92, and the reason the pipeline layer exists at all. A check you
    /// can specify is better served by a command with a dialog — it is discoverable, it ships with the
    /// plugin, and people trust it. What a dialog cannot hold is a step whose output has to be inspected,
    /// narrowed, approved and recorded before it reaches the model. That is a sequence with checkpoints,
    /// which is what a pipeline is.</para>
    ///
    /// <para>Three decisions carry the risk:</para>
    ///
    /// <para><b>Answers are matched by index, never by order.</b> Every row is sent with an index and the
    /// model must return it. Trusting position means one dropped element silently shifts every answer
    /// onto the wrong row — which no count and no eyeball catches, because the output still looks
    /// complete.</para>
    ///
    /// <para><b>A row with no answer comes back unchanged and says so</b> (<c>ai.answered: false</c>),
    /// rather than disappearing. A shorter list that looks like data is the failure this whole codebase
    /// has been fixing all week. Filter on <c>ai.answered</c> to act only on what was answered.</para>
    ///
    /// <para><b>Provenance rides with each row.</b> <c>ai.confidence</c> and <c>ai.why</c> are what the
    /// approval node's predicate reads and what a person reads in the report. Note the doc's warning that
    /// self-reported confidence is weak evidence — a tiebreaker, never a sole criterion.</para>
    ///
    /// <para>Read-only: it calls a model, not Revit. So it can be previewed — at the cost of a real API
    /// call, which is the honest price of seeing what it will actually say.</para>
    /// </summary>
    [RevitCommand(
        Description = "Runs a prompt over a list of rows and returns the same rows with the model's " +
                      "answers merged in. Payload: { items, prompt, produce: [\"field\"], fields: " +
                      "[\"shown\"], model, provider, batchSize }. 'produce' names the fields the model " +
                      "must return per row — it is the output contract. 'fields' limits what the model is " +
                      "shown (default: everything). Each row comes back with ai: { answered, confidence, " +
                      "why, model }; rows the model did not answer are returned UNCHANGED with " +
                      "answered:false, so filter on 'ai.answered' before acting. Touches nothing in the " +
                      "Revit model. Returns { items, answered, unanswered, model, errors }.",
        ReadOnly = true,
        InputType = typeof(AiTransform.Request),
        OutputType = typeof(AiTransformResult))]
    internal sealed class AiTransform : IRevitTask, IProgressAware
    {
        public IProgress<ProgressInfo>? Progress { get; set; }

        public async Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            Request req = ctx.Payload.As<Request>() ?? new Request();

            if (string.IsNullOrWhiteSpace(req.Model))
                throw new InvalidOperationException(
                    "No AI model selected. Set 'model' (and 'provider' for anything but the built-in Ollama).");
            if (string.IsNullOrWhiteSpace(req.Prompt))
                throw new InvalidOperationException("No prompt. This node does nothing without one.");
            if (req.Produce is not { Count: > 0 })
                throw new InvalidOperationException(
                    "'produce' is empty. Name the fields the model must return for each row — without them " +
                    "there is no contract to check its answer against, and nothing for the next node to read.");

            JArray rows = req.Items is { Count: > 0 } ? JArray.FromObject(req.Items) : new JArray();
            if (rows.Count == 0)
                return new AiTransformResult(Array.Empty<object>(), 0, 0, req.Model!, Array.Empty<string>());

            int batchSize = req.BatchSize is > 0 ? req.BatchSize.Value : 25;
            AiAnalysisService ai = new(req.Provider, req.Model!);

            Dictionary<int, JObject> answers = new();
            List<string> errors = new();
            string system = SystemPrompt(req.Produce!);

            for (int start = 0; start < rows.Count; start += batchSize)
            {
                ct.ThrowIfCancellationRequested();

                JArray batch = new(rows.Skip(start).Take(batchSize));
                string user = UserPrompt(req.Prompt!, Project(batch, start, req.Fields));

                try
                {
                    foreach (JObject answer in ParseAnswers(await ai.CompleteJsonAsync(system, user)))
                        if (answer["index"]?.Type is JTokenType.Integer)
                            answers[(int)answer["index"]!] = answer;
                }
                catch (Exception ex)
                {
                    // One batch failing must not throw away the batches that succeeded — they cost money
                    // and time. Its rows stay unanswered, which is visible in the counts and filterable.
                    Log.Warning(ex, "AiTransform: batch at {Start} failed", start);
                    errors.Add($"Rows {start + 1}-{Math.Min(start + batchSize, rows.Count)}: {ex.Message}");
                }

                Progress?.Report(new ProgressInfo(
                    Math.Min(start + batchSize, rows.Count) / (double)rows.Count,
                    $"Asking the model about rows {start + 1}-{Math.Min(start + batchSize, rows.Count)}…"));
            }

            // Every batch failed: nothing was produced, and returning "0 answered" would read like a model
            // that had nothing to say. Same rule as a read command that finds none of what it was asked about.
            if (answers.Count == 0 && errors.Count > 0)
                throw new InvalidOperationException(
                    "The model answered nothing. " + string.Join(" ", errors));

            JArray merged = Merge(rows, answers, req.Produce!, req.Model!);
            int answered = merged.Count(r => r["ai"]?["answered"]?.Value<bool>() == true);

            Log.Information("AiTransform: {Answered}/{Total} row(s) answered by {Model}",
                answered, rows.Count, req.Model);

            return new AiTransformResult(
                merged.Cast<object>().ToList(), answered, rows.Count - answered, req.Model!, errors);
        }

        /// <summary>
        /// The rows as the model sees them: an index it must echo, plus the fields it is allowed to read.
        ///
        /// <para><c>fields</c> is not only about tokens. A row from GetFamilyTypeRows carries every type
        /// parameter; sending all of them to answer a question about names buries the question and pays
        /// for the burial.</para>
        /// </summary>
        internal static JArray Project(JArray batch, int offset, List<string>? fields)
        {
            JArray projected = new();

            for (int i = 0; i < batch.Count; i++)
            {
                JObject item = new() { ["index"] = offset + i };

                if (batch[i] is not JObject row)
                {
                    // A list of bare values — ids, names — bound straight from a wildcard path.
                    item["value"] = batch[i];
                }
                else if (fields is { Count: > 0 })
                {
                    foreach (string field in fields)
                    {
                        JToken? value = row.SelectToken(field);
                        if (value is not null) item[field] = value;
                    }
                }
                else
                {
                    foreach (JProperty property in row.Properties())
                        if (property.Name != "index") item[property.Name] = property.Value;
                }

                projected.Add(item);
            }

            return projected;
        }

        /// <summary>
        /// The answers laid back onto the rows they belong to.
        ///
        /// <para>By index, and only the declared fields: a model that invents an extra key must not be
        /// able to add a column nobody asked for, and one that omits a declared field must leave a row
        /// that says so rather than one that quietly looks finished.</para>
        /// </summary>
        internal static JArray Merge(
            JArray rows, IReadOnlyDictionary<int, JObject> answers, List<string> produce, string model)
        {
            JArray merged = new();

            for (int i = 0; i < rows.Count; i++)
            {
                JObject row = rows[i] is JObject original
                    ? (JObject)original.DeepClone()
                    : new JObject { ["value"] = rows[i].DeepClone() };

                bool answered = answers.TryGetValue(i, out JObject? answer);
                List<string> missing = new();

                if (answered)
                {
                    foreach (string field in produce)
                    {
                        JToken? value = answer![field];
                        if (value is null || value.Type == JTokenType.Null) missing.Add(field);
                        else row[field] = value;
                    }

                    // Half an answer is not an answer. Acting on a row whose new name came back but whose
                    // reason did not is acting on something the contract did not deliver.
                    if (missing.Count > 0) answered = false;
                }

                JObject provenance = new()
                {
                    ["answered"] = answered,
                    ["model"] = model,
                };

                if (answer is not null)
                {
                    if (answer["confidence"] is { } confidence) provenance["confidence"] = confidence;
                    if (answer["why"] is { } why) provenance["why"] = why;
                }
                if (missing.Count > 0) provenance["missing"] = new JArray(missing);

                row["ai"] = provenance;
                merged.Add(row);
            }

            return merged;
        }

        private static string SystemPrompt(List<string> produce) =>
            $$"""
            You answer a question about each row of a table, for a Revit model.

            INPUT: a JSON array of rows. Every row has an "index" plus the fields you are shown.
            OUTPUT: ONLY a raw JSON array. No wrapper object, no markdown, no commentary.

            Each element of your array must have exactly these fields:
              "index"      — integer, copied from the input row unchanged
              {{string.Join("\n  ", produce.Select(f => $"\"{f}\"".PadRight(12) + " — your answer for this row"))}}
              "confidence" — number between 0 and 1, how sure you are for THIS row
              "why"        — one short sentence, why this answer

            RULES:
            - Return every input row exactly once, identified by its "index".
            - "index" identifies the row. Do NOT renumber, reorder or skip.
            - If you cannot answer a row, still return it, with a low "confidence" and a "why" that
              says what is missing. Do not guess to fill the shape.
            - Apply ONE consistent rule across the whole batch.
            """;

        private static string UserPrompt(string prompt, JArray rows) =>
            $"""
            Instruction: {prompt}
            Rows: {rows.ToString(Formatting.None)}
            """;

        /// <summary>The model's array, however it wrapped it. Same tolerance the naming call needs: a
        /// bare array, or one inside {"result": …} / {"rows": …}, both happen with the same model.</summary>
        private static IEnumerable<JObject> ParseAnswers(string raw)
        {
            JToken parsed;
            try { parsed = JToken.Parse(raw); }
            catch (JsonException) { return Array.Empty<JObject>(); }

            JArray? array = parsed as JArray
                            ?? (parsed as JObject)?.Properties()
                                .Select(p => p.Value).OfType<JArray>().FirstOrDefault();

            return array?.OfType<JObject>() ?? Enumerable.Empty<JObject>();
        }

        internal sealed class Request
        {
            [Description("The rows to run the prompt over. Usually bound from a previous node.")]
            public List<object>? Items { get; set; }

            [Description("The instruction, applied consistently to every row.")]
            public string? Prompt { get; set; }

            [Description("Fields the model must return for each row — the output contract. " +
                         "They are merged into the row, so the next node reads them as ordinary fields.")]
            public List<string>? Produce { get; set; }

            [Description("Fields of each row to SHOW the model. Empty shows everything, which on rows " +
                         "carrying every type parameter buries the question and pays for the burial.")]
            public List<string>? Fields { get; set; }

            [Description("Model name to use.")]
            public string? Model { get; set; }

            [Description("AI provider id (AiGetProviders); omit for the built-in local Ollama.")]
            public string? Provider { get; set; }

            [Description("Rows per request. Default 25.")]
            public int? BatchSize { get; set; }
        }
    }

    internal sealed record AiTransformResult(
        [property: JsonProperty("items")] IReadOnlyList<object> Items,
        [property: JsonProperty("answered")] int Answered,
        [property: JsonProperty("unanswered")] int Unanswered,
        [property: JsonProperty("model")] string Model,
        [property: JsonProperty("errors")] IReadOnlyList<string> Errors);
}
