using System.Collections.Generic;
using System.Linq;
using IdsSharp;
using NUnit.Framework;

namespace IdsSharp.Tests
{
    [TestFixture]
    public sealed class IdsEvaluatorTests
    {
        private static Specification Spec(
            IReadOnlyList<Facet> applicability, IReadOnlyList<Facet> requirements,
            int minOccurs = 0, int? maxOccurs = null) =>
            new Specification("spec", applicability, requirements, minOccurs: minOccurs, maxOccurs: maxOccurs);

        private static EntityFacet Walls() => new EntityFacet(ValueRestriction.Exact("IFCWALL"));

        private static PropertyFacet FireRating(ValueRestriction? value = null,
            Cardinality cardinality = Cardinality.Required) =>
            new PropertyFacet(
                ValueRestriction.Exact("Pset_WallCommon"),
                ValueRestriction.Exact("FireRating"),
                value,
                cardinality: cardinality);

        [Test]
        public void Only_applicable_entities_are_checked()
        {
            SpecificationResult result = IdsEvaluator.Evaluate(
                Spec(new[] { Walls() }, new Facet[] { FireRating() }),
                new IModelEntity[]
                {
                    new FakeEntity("IFCWALL", "1").WithProperty("Pset_WallCommon", "FireRating", "F90"),
                    new FakeEntity("IFCDOOR", "2"),
                });

            Assert.That(result.ApplicableCount, Is.EqualTo(1));
            Assert.That(result.Results, Has.Count.EqualTo(1));
            Assert.That(result.Passed, Is.True);
        }

        [Test]
        public void An_empty_applicability_selects_nothing_rather_than_everything()
        {
            // A half-written specification must not silently apply to the whole model. The direction of
            // this default is the difference between "your rule did nothing" and "your rule condemned
            // every element in the project".
            SpecificationResult result = IdsEvaluator.Evaluate(
                Spec(new Facet[0], new Facet[] { FireRating() }),
                new IModelEntity[] { new FakeEntity("IFCWALL") });

            Assert.That(result.ApplicableCount, Is.Zero);
            Assert.That(result.Results, Is.Empty);
        }

        [Test]
        public void A_missing_property_fails_and_the_reason_names_it()
        {
            SpecificationResult result = IdsEvaluator.Evaluate(
                Spec(new[] { Walls() }, new Facet[] { FireRating() }),
                new IModelEntity[] { new FakeEntity("IFCWALL") });

            Assert.That(result.Passed, Is.False);
            Assert.That(result.Results[0].Kind, Is.EqualTo(ResultKind.Failed));
            Assert.That(result.Results[0].Reason, Does.Contain("Pset_WallCommon.FireRating"));
        }

        [Test]
        public void A_property_that_is_present_but_empty_does_not_satisfy_an_existence_requirement()
        {
            // The most common real finding. "It has a FireRating parameter, which is blank" is exactly
            // the state a checking run exists to surface, and treating presence as enough would hide it.
            SpecificationResult result = IdsEvaluator.Evaluate(
                Spec(new[] { Walls() }, new Facet[] { FireRating() }),
                new IModelEntity[]
                {
                    new FakeEntity("IFCWALL").WithProperty("Pset_WallCommon", "FireRating", ""),
                });

            Assert.That(result.Results[0].Kind, Is.EqualTo(ResultKind.Failed));
            Assert.That(result.Results[0].Reason, Does.Contain("empty"));
        }

        [Test]
        public void A_wrong_value_fails_and_the_reason_carries_what_was_there()
        {
            // A verdict without the actual value is not actionable — whoever has to fix it would have to
            // go and look the element up to find out what it currently says.
            SpecificationResult result = IdsEvaluator.Evaluate(
                Spec(new[] { Walls() },
                    new Facet[] { FireRating(ValueRestriction.Create(pattern: "F[0-9]{2,3}")) }),
                new IModelEntity[]
                {
                    new FakeEntity("IFCWALL").WithProperty("Pset_WallCommon", "FireRating", "keine"),
                });

            Assert.That(result.Results[0].Kind, Is.EqualTo(ResultKind.Failed));
            Assert.That(result.Results[0].Reason, Does.Contain("keine"));
        }

        [Test]
        public void A_prohibited_requirement_fails_when_it_is_satisfied()
        {
            SpecificationResult result = IdsEvaluator.Evaluate(
                Spec(new[] { Walls() }, new Facet[] { FireRating(cardinality: Cardinality.Prohibited) }),
                new IModelEntity[]
                {
                    new FakeEntity("IFCWALL", "1").WithProperty("Pset_WallCommon", "FireRating", "F90"),
                    new FakeEntity("IFCWALL", "2"),
                });

            Assert.That(result.Results.Count(r => r.Kind == ResultKind.Failed), Is.EqualTo(1));
            Assert.That(result.Results.Count(r => r.Kind == ResultKind.Passed), Is.EqualTo(1));
        }

        [Test]
        public void An_optional_requirement_never_fails()
        {
            SpecificationResult result = IdsEvaluator.Evaluate(
                Spec(new[] { Walls() }, new Facet[] { FireRating(cardinality: Cardinality.Optional) }),
                new IModelEntity[] { new FakeEntity("IFCWALL") });

            Assert.That(result.Results[0].Kind, Is.EqualTo(ResultKind.Passed));
            Assert.That(result.Passed, Is.True);
        }

        [Test]
        public void An_unsupported_requirement_is_reported_as_not_checked_never_as_passed()
        {
            // The most important behaviour in the library. A checker that treats "I did not look" as
            // "it is fine" produces a clean report for a model nobody checked — and a clean report is
            // believed. It must be a third outcome, spelled differently from a pass.
            SpecificationResult result = IdsEvaluator.Evaluate(
                Spec(new[] { Walls() },
                    new Facet[] { new UnsupportedFacet("material", Cardinality.Required, null) }),
                new IModelEntity[] { new FakeEntity("IFCWALL") });

            Assert.That(result.Results[0].Kind, Is.EqualTo(ResultKind.NotChecked));
            Assert.That(result.NotCheckedCount, Is.EqualTo(1));
            Assert.That(result.Results[0].Reason, Does.Contain("material"));
        }

        [Test]
        public void A_pattern_facet_a_model_cannot_enumerate_is_not_checked_rather_than_passed()
        {
            // The same rule from the other direction: the requirement is supported, but THIS model
            // cannot answer it. Silence must not read as a clean result.
            PropertyFacet anyFireProperty = new PropertyFacet(
                ValueRestriction.Create(pattern: "Pset_.*"),
                ValueRestriction.Create(pattern: "Fire.*"));

            SpecificationResult result = IdsEvaluator.Evaluate(
                Spec(new[] { Walls() }, new Facet[] { anyFireProperty }),
                new IModelEntity[] { new FakeEntity("IFCWALL", enumerable: false) });

            Assert.That(result.Results[0].Kind, Is.EqualTo(ResultKind.NotChecked));
            Assert.That(result.Results[0].Reason, Does.Contain("enumerate"));
        }

        [Test]
        public void A_pattern_facet_is_answered_when_the_model_can_enumerate()
        {
            PropertyFacet anyFireProperty = new PropertyFacet(
                ValueRestriction.Create(pattern: "Pset_.*"),
                ValueRestriction.Create(pattern: "Fire.*"));

            SpecificationResult result = IdsEvaluator.Evaluate(
                Spec(new[] { Walls() }, new Facet[] { anyFireProperty }),
                new IModelEntity[]
                {
                    new FakeEntity("IFCWALL").WithProperty("Pset_WallCommon", "FireRating", "F90"),
                });

            Assert.That(result.Results[0].Kind, Is.EqualTo(ResultKind.Passed));
        }

        [Test]
        public void MinOccurs_turns_a_specification_into_a_must_exist_check()
        {
            // "There must be at least one fire door" is a real requirement, and one that a per-entity
            // checker cannot express: with nothing applicable there are no entities to fail.
            SpecificationResult result = IdsEvaluator.Evaluate(
                Spec(new[] { Walls() }, new Facet[] { FireRating() }, minOccurs: 1),
                new IModelEntity[] { new FakeEntity("IFCDOOR") });

            Assert.That(result.Passed, Is.False);
            Assert.That(result.CardinalityFailure, Does.Contain("at least 1"));
        }

        [Test]
        public void A_specification_matching_nothing_says_so_instead_of_passing_quietly()
        {
            // Zero applicable is usually a wrong applicability, not a perfect model. It passes — that is
            // correct — but the count is published so a report can point out that nothing was examined.
            SpecificationResult result = IdsEvaluator.Evaluate(
                Spec(new[] { Walls() }, new Facet[] { FireRating() }),
                new IModelEntity[] { new FakeEntity("IFCDOOR") });

            Assert.That(result.Passed, Is.True);
            Assert.That(result.ApplicableCount, Is.Zero);
        }

        [Test]
        public void A_predefined_type_narrows_applicability()
        {
            EntityFacet externalWalls = new EntityFacet(
                ValueRestriction.Exact("IFCWALL"), ValueRestriction.Exact("SOLIDWALL"));

            SpecificationResult result = IdsEvaluator.Evaluate(
                Spec(new[] { externalWalls }, new Facet[] { FireRating() }),
                new IModelEntity[]
                {
                    new FakeEntity("IFCWALL", "1", predefinedType: "SOLIDWALL"),
                    new FakeEntity("IFCWALL", "2", predefinedType: "PARTITIONING"),
                    new FakeEntity("IFCWALL", "3"),
                });

            Assert.That(result.ApplicableCount, Is.EqualTo(1));
            Assert.That(result.Results[0].Entity.Id, Is.EqualTo("1"));
        }

        [Test]
        public void An_attribute_requirement_reads_the_entity_itself()
        {
            AttributeFacet named = new AttributeFacet(ValueRestriction.Exact("Name"));

            SpecificationResult result = IdsEvaluator.Evaluate(
                Spec(new[] { Walls() }, new Facet[] { named }),
                new IModelEntity[]
                {
                    new FakeEntity("IFCWALL", "1").WithAttribute("Name", "Wall-01"),
                    new FakeEntity("IFCWALL", "2").WithAttribute("Name", ""),
                    new FakeEntity("IFCWALL", "3"),
                });

            Assert.That(result.Results.Select(r => r.Kind),
                Is.EqualTo(new[] { ResultKind.Passed, ResultKind.Failed, ResultKind.Failed }));
        }
    }
}
