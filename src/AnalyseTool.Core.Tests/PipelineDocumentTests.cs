using AnalyseTool.Core.Common.Pipelines;
using AnalyseTool.Core.Features.Pipelines;
using NUnit.Framework;

namespace AnalyseTool.Core.Tests
{
    /// <summary>
    /// The file format. Only <see cref="PipelineStore.Parse"/> is exercised here — Load and ListNames
    /// touch the profile folder, while parsing is pure and is where the interesting failures live.
    /// </summary>
    [TestFixture]
    public sealed class PipelineDocumentTests
    {
        [Test]
        public void A_key_the_format_does_not_know_is_kept_rather_than_dropped()
        {
            // The failure this exists for: an agent wrote its Filter conditions as a key ON the node
            // instead of inside "params". The deserializer dropped it, the Filter passed all 187 family
            // types through to a purge, and nothing could say why — the mistake no longer existed by the
            // time anything looked. Keeping it is what lets the validator name it.
            PipelineDocument doc = PipelineStore.Parse("""
                {
                  "schema": 1,
                  "nodes": [
                    { "id": "unused", "command": "Filter",
                      "where": [ { "field": "instanceCount", "op": "equals", "value": 0 } ] }
                  ]
                }
                """, "the test");

            Assert.That(doc.Nodes[0].Unknown, Is.Not.Null);
            Assert.That(doc.Nodes[0].Unknown!.Keys, Is.EqualTo(new[] { "where" }));
        }

        [Test]
        public void An_unknown_top_level_key_is_kept_too()
        {
            PipelineDocument doc = PipelineStore.Parse(
                """{ "schema": 1, "name": "x", "descriptin": "typo", "nodes": [] }""", "the test");

            Assert.That(doc.Unknown!.Keys, Is.EqualTo(new[] { "descriptin" }));
        }

        [Test]
        public void A_well_formed_node_collects_nothing()
        {
            // Otherwise every node would carry a warning and the check would be noise.
            PipelineDocument doc = PipelineStore.Parse("""
                {
                  "schema": 1, "id": "p", "name": "p",
                  "nodes": [
                    { "id": "unused", "command": "Filter", "contract": 1,
                      "params": { "where": [] }, "bind": { "items": "a.items" }, "onFailure": "continue" }
                  ],
                  "edges": [ { "from": "a", "to": "unused" } ],
                  "state": null
                }
                """, "the test");

            Assert.That(doc.Unknown, Is.Null.Or.Empty);
            Assert.That(doc.Nodes[0].Unknown, Is.Null.Or.Empty);
            Assert.That(doc.Nodes[0].OnFailure, Is.EqualTo(NodeFailureAction.Continue));
        }

        [Test]
        public void A_later_schema_is_refused_rather_than_half_understood()
        {
            Assert.That(() => PipelineStore.Parse("""{ "schema": 2, "nodes": [] }""", "the test"),
                Throws.InstanceOf<InvalidOperationException>().With.Message.Contains("schema 2"));
        }

        [Test]
        public void A_document_sent_as_a_json_string_parses_like_one_sent_as_an_object()
        {
            // The "pipeline" property is declared as object, so both arrive — and agents send both.
            const string json = """{ "schema": 1, "id": "p", "nodes": [] }""";

            Assert.That(PipelineStore.ParseInline(json, "the test").Id, Is.EqualTo("p"));
        }
    }
}
