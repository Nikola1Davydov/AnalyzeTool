using Newtonsoft.Json.Linq;

namespace AnalyseTool.Mcp.Bridge
{
    /// <summary>
    /// Checks an MCP caller's payload against the input schema the command PUBLISHED, so a mistake comes
    /// back as a sentence instead of as an empty answer.
    ///
    /// Why this exists: every command reads its arguments through <c>ctx.Payload.As&lt;T&gt;()</c>, and
    /// Newtonsoft silently drops a field it does not recognise. An agent that writes "catgoryContains"
    /// therefore gets a successful call with an unfiltered result — indistinguishable from "nothing
    /// matched", which is the failure mode #83 calls the worst one: the agent does not notice and keeps
    /// building on it. Nothing was validating this until now because there was nothing to validate
    /// against; declaring InputType everywhere (#89, #77) is what made it possible.
    ///
    /// FAIL-OPEN, on purpose. Every branch that does not clearly understand what it is looking at returns
    /// "no complaint" rather than guessing. A validator that blocks a legitimate call because a schema was
    /// shaped in a way it did not anticipate is worse than the silence it replaces: the silence loses a
    /// filter, the false rejection loses the command. So this only speaks up about things it is sure of.
    ///
    /// SCOPE: top-level properties only — the names an agent actually types — and only the obvious kind
    /// mismatches. No required-field, nested-object, enum or format checking. The published schema stays
    /// the full truth; this catches the two mistakes that are otherwise invisible.
    /// </summary>
    internal static class PayloadValidator
    {
        /// <summary>Returns null when there is nothing to complain about, otherwise a message meant to be
        /// read by whoever sent the payload.</summary>
        public static string? Validate(string command, string? inputSchemaJson, JToken? payload)
        {
            if (payload is null or JValue { Value: null }) return null;   // "no arguments" is the command's own business
            if (string.IsNullOrWhiteSpace(inputSchemaJson)) return null;

            JObject? schema = TryParseObject(inputSchemaJson);
            JObject? properties = schema?["properties"] as JObject;
            if (properties is null || properties.Count == 0) return null; // free-form or argument-less

            if (payload is not JObject arguments)
                return $"{command}: arguments must be a JSON object with named parameters, but a " +
                       $"{Kind(payload)} was sent. Expected parameters: {Join(properties.Properties().Select(p => p.Name))}.";

            List<string> problems = new();
            foreach (JProperty argument in arguments.Properties())
            {
                // Case-insensitively, because that is how Newtonsoft binds them: "familyids" reaches
                // FamilyIds today and must not start failing now.
                JProperty? declared = properties.Properties()
                    .FirstOrDefault(p => string.Equals(p.Name, argument.Name, StringComparison.OrdinalIgnoreCase));

                if (declared is null)
                {
                    string suggestion = Suggest(argument.Name, properties.Properties().Select(p => p.Name));
                    problems.Add($"'{argument.Name}' is not a parameter of this command.{suggestion}");
                    continue;
                }

                string? mismatch = KindMismatch(declared.Value as JObject, argument.Value);
                if (mismatch is not null)
                    problems.Add($"'{declared.Name}' expects {mismatch}, but a {Kind(argument.Value)} was sent.");
            }

            if (problems.Count == 0) return null;

            // Every problem at once: an agent that fixes them one per round trip pays a call for each.
            return $"{command}: {string.Join(" ", problems)} " +
                   $"Parameters of this command: {Join(properties.Properties().Select(p => p.Name))}. " +
                   "Its inputSchema in tools/list is the authority.";
        }

        /// <summary>
        /// Only STRUCTURAL mismatches — a list where an object belongs, a scalar where a list belongs.
        /// Deliberately not stricter than the deserializer that runs next: Newtonsoft happily reads "10"
        /// into an int, so flagging string-where-number-declared would reject calls that work today, and
        /// where it does refuse (a word into an int) it already says so itself. Structure is the part
        /// worth catching early, because the message here can name the command and list its parameters.
        ///
        /// Anything the schema does not state plainly — $ref, anyOf, no type at all — returns null, which
        /// means "no opinion".
        /// </summary>
        private static string? KindMismatch(JObject? declared, JToken value)
        {
            if (declared is null) return null;
            JToken? type = declared["type"];
            if (type is null) return null;

            List<string> allowed = type switch
            {
                JValue { Value: string single } => new List<string> { single },
                JArray many when many.All(t => t.Type == JTokenType.String) =>
                    many.Select(t => t.Value<string>()!).ToList(),
                _ => new List<string>(),
            };
            // "null" is a nullability marker, not a candidate shape. Counting it as one made every
            // nullable property — which is nearly all of them — accept anything at all.
            allowed = allowed.Where(a => a != "null").ToList();
            if (allowed.Count == 0) return null;

            // An optional argument sent explicitly as null is not a mistake; the deserializer treats it
            // as absent either way.
            if (value.Type == JTokenType.Null) return null;

            string actual = Kind(value);
            if (allowed.Contains(actual)) return null;
            // Scalar for scalar: leave it to Newtonsoft, which either converts it or explains itself.
            if (!IsStructural(actual) && !allowed.Any(IsStructural)) return null;

            return Join(allowed);
        }

        /// <summary>Array and object are the shapes a deserializer cannot bridge to anything else.</summary>
        private static bool IsStructural(string kind) => kind is "array" or "object";

        private static string Kind(JToken value) => value.Type switch
        {
            JTokenType.String => "string",
            JTokenType.Integer => "integer",
            JTokenType.Float => "number",
            JTokenType.Boolean => "boolean",
            JTokenType.Array => "array",
            JTokenType.Object => "object",
            JTokenType.Null => "null",
            _ => "value",
        };

        /// <summary>"Did you mean" for a near miss only. A suggestion that is merely the least-bad of a
        /// list of unrelated names sends the caller somewhere wrong with confidence, so the distance has
        /// to be small relative to the word.</summary>
        private static string Suggest(string typed, IEnumerable<string> candidates)
        {
            string? best = null;
            int bestDistance = int.MaxValue;
            foreach (string candidate in candidates)
            {
                int distance = Distance(typed.ToLowerInvariant(), candidate.ToLowerInvariant());
                if (distance < bestDistance) { bestDistance = distance; best = candidate; }
            }

            int tolerance = Math.Min(3, Math.Max(1, (best?.Length ?? 0) / 3));
            return best is not null && bestDistance <= tolerance ? $" Did you mean '{best}'?" : string.Empty;
        }

        private static int Distance(string a, string b)
        {
            int[] previous = new int[b.Length + 1];
            int[] current = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) previous[j] = j;

            for (int i = 1; i <= a.Length; i++)
            {
                current[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int substitute = previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                    current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), substitute);
                }
                (previous, current) = (current, previous);
            }
            return previous[b.Length];
        }

        private static JObject? TryParseObject(string json)
        {
            try { return JToken.Parse(json) as JObject; }
            catch { return null; }   // fail-open: an unreadable schema validates nothing
        }

        private static string Join(IEnumerable<string> names) => string.Join(", ", names);
    }
}
