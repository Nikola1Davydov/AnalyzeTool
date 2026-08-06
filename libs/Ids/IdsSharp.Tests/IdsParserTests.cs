using System.Linq;
using IdsSharp;
using NUnit.Framework;

namespace IdsSharp.Tests
{
    [TestFixture]
    public sealed class IdsParserTests
    {
        private const string Minimal = """
            <ids xmlns="http://standards.buildingsmart.org/IDS">
              <info><title>Test</title><author>a@b.c</author></info>
              <specifications>
                <specification name="Walls carry a fire rating" ifcVersion="IFC4">
                  <applicability minOccurs="1" maxOccurs="unbounded">
                    <entity><name><simpleValue>IFCWALL</simpleValue></name></entity>
                  </applicability>
                  <requirements>
                    <property dataType="IFCLABEL">
                      <propertySet><simpleValue>Pset_WallCommon</simpleValue></propertySet>
                      <baseName><simpleValue>FireRating</simpleValue></baseName>
                    </property>
                  </requirements>
                </specification>
              </specifications>
            </ids>
            """;

        [Test]
        public void A_minimal_document_round_trips_into_the_model()
        {
            IdsDocument document = IdsParser.Parse(Minimal);

            Assert.That(document.Title, Is.EqualTo("Test"));
            Assert.That(document.Specifications, Has.Count.EqualTo(1));

            Specification specification = document.Specifications[0];
            Assert.Multiple(() =>
            {
                Assert.That(specification.Name, Is.EqualTo("Walls carry a fire rating"));
                Assert.That(specification.MinOccurs, Is.EqualTo(1));
                Assert.That(specification.MaxOccurs, Is.Null, "unbounded is no maximum, not a maximum of 0");
                Assert.That(specification.IfcVersions, Is.EqualTo(new[] { "IFC4" }));
                Assert.That(specification.Applicability[0], Is.TypeOf<EntityFacet>());
                Assert.That(specification.Requirements[0], Is.TypeOf<PropertyFacet>());
            });
        }

        [Test]
        public void Restrictions_are_read_into_the_value_model()
        {
            IdsDocument document = IdsParser.Parse("""
                <ids>
                  <specifications>
                    <specification name="s">
                      <applicability>
                        <entity><name><simpleValue>IFCDOOR</simpleValue></name></entity>
                      </applicability>
                      <requirements>
                        <property>
                          <propertySet><simpleValue>Pset_DoorCommon</simpleValue></propertySet>
                          <baseName><simpleValue>FireRating</simpleValue></baseName>
                          <value>
                            <xs:restriction base="xs:string" xmlns:xs="http://www.w3.org/2001/XMLSchema">
                              <xs:enumeration value="T30" />
                              <xs:enumeration value="T90" />
                            </xs:restriction>
                          </value>
                        </property>
                      </requirements>
                    </specification>
                  </specifications>
                </ids>
                """);

            PropertyFacet facet = (PropertyFacet)document.Specifications[0].Requirements[0];

            Assert.That(facet.Value, Is.Not.Null);
            Assert.That(facet.Value!.Matches("T30"), Is.True);
            Assert.That(facet.Value.Matches("T60"), Is.False);
        }

        [Test]
        public void An_unknown_facet_is_kept_as_unsupported_rather_than_dropped()
        {
            // Dropping it would make the specification look fully satisfied while a requirement of it was
            // never mentioned again — the failure this library exists to avoid on someone else's behalf.
            IdsDocument document = IdsParser.Parse("""
                <ids>
                  <specifications>
                    <specification name="s">
                      <applicability>
                        <entity><name><simpleValue>IFCWALL</simpleValue></name></entity>
                      </applicability>
                      <requirements>
                        <material><value><simpleValue>Concrete</simpleValue></value></material>
                      </requirements>
                    </specification>
                  </specifications>
                </ids>
                """);

            Facet facet = document.Specifications[0].Requirements[0];

            Assert.That(facet, Is.TypeOf<UnsupportedFacet>());
            Assert.That(facet.IsSupported, Is.False);
            Assert.That(((UnsupportedFacet)facet).ElementName, Is.EqualTo("material"));
        }

        [Test]
        public void Cardinality_is_read()
        {
            IdsDocument document = IdsParser.Parse("""
                <ids>
                  <specifications>
                    <specification name="s">
                      <applicability>
                        <entity><name><simpleValue>IFCWALL</simpleValue></name></entity>
                      </applicability>
                      <requirements>
                        <attribute cardinality="prohibited">
                          <name><simpleValue>Description</simpleValue></name>
                        </attribute>
                      </requirements>
                    </specification>
                  </specifications>
                </ids>
                """);

            Assert.That(document.Specifications[0].Requirements[0].Cardinality,
                Is.EqualTo(Cardinality.Prohibited));
        }

        [Test]
        public void A_file_that_is_not_ids_is_refused_by_name()
        {
            Assert.That(() => IdsParser.Parse("<project><wall /></project>"),
                Throws.TypeOf<IdsParseException>().With.Message.Contains("not an IDS file"));
        }

        [Test]
        public void Malformed_xml_is_refused_rather_than_silently_checking_nothing()
        {
            Assert.That(() => IdsParser.Parse("<ids><specifications>"),
                Throws.TypeOf<IdsParseException>().With.Message.Contains("well-formed"));
        }

        [Test]
        public void A_specification_without_applicability_is_refused()
        {
            // With no applicability there is no way to know which entities it is about, and the safe
            // reading — "nothing" — would be a rule that silently never runs.
            Assert.That(
                () => IdsParser.Parse("""
                    <ids><specifications><specification name="s">
                      <requirements />
                    </specification></specifications></ids>
                    """),
                Throws.TypeOf<IdsParseException>().With.Message.Contains("applicability"));
        }

        [Test]
        public void A_broken_pattern_is_refused_at_load_rather_than_matching_nothing()
        {
            // Left to itself it would match nothing, which looks exactly like a strict rule doing its
            // job while being a rule that never ran.
            Assert.That(
                () => IdsParser.Parse("""
                    <ids><specifications><specification name="s">
                      <applicability>
                        <entity><name><simpleValue>IFCWALL</simpleValue></name></entity>
                      </applicability>
                      <requirements>
                        <attribute>
                          <name><simpleValue>Name</simpleValue></name>
                          <value>
                            <xs:restriction xmlns:xs="http://www.w3.org/2001/XMLSchema">
                              <xs:pattern value="^K-(\d+" />
                            </xs:restriction>
                          </value>
                        </attribute>
                      </requirements>
                    </specification></specifications></ids>
                    """),
                Throws.TypeOf<IdsParseException>().With.Message.Contains("not a valid expression"));
        }

        [Test]
        public void A_document_without_the_ids_namespace_still_loads()
        {
            // Files in the wild carry several versions of the namespace and some carry none. Refusing
            // them would be strictness that helps nobody and stops the library being usable.
            IdsDocument document = IdsParser.Parse("""
                <ids><specifications><specification name="s">
                  <applicability>
                    <entity><name><simpleValue>IFCWALL</simpleValue></name></entity>
                  </applicability>
                  <requirements />
                </specification></specifications></ids>
                """);

            Assert.That(document.Specifications, Has.Count.EqualTo(1));
        }
    }
}
