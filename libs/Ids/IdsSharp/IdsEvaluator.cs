using System;
using System.Collections.Generic;
using System.Linq;

namespace IdsSharp
{
    /// <summary>What became of one requirement on one entity.</summary>
    public enum ResultKind
    {
        Passed,
        Failed,

        /// <summary>
        /// The requirement was NOT evaluated — an unsupported facet, or one this model cannot answer.
        ///
        /// <para>A third outcome rather than a lenient pass, and it is the most important decision in
        /// this library. A checker that quietly treats "I did not look" as "it is fine" produces a
        /// clean report for a model nobody checked, and a clean report is believed. Anything that
        /// consumes these results must decide for itself what to do with NotChecked; it cannot be
        /// mistaken for a pass because it is not spelled like one.</para>
        /// </summary>
        NotChecked,
    }

    /// <summary>One requirement, one entity, one verdict — the row a report is built from.</summary>
    public sealed class RequirementResult
    {
        public RequirementResult(
            Specification specification, Facet requirement, IModelEntity entity, ResultKind kind, string reason)
        {
            Specification = specification;
            Requirement = requirement;
            Entity = entity;
            Kind = kind;
            Reason = reason;
        }

        public Specification Specification { get; }
        public Facet Requirement { get; }
        public IModelEntity Entity { get; }
        public ResultKind Kind { get; }

        /// <summary>Why, in a sentence, naming the value that was there. A verdict without this is not
        /// actionable: "failed" tells whoever has to fix it nothing about what to change.</summary>
        public string Reason { get; }
    }

    /// <summary>What one specification found across the model.</summary>
    public sealed class SpecificationResult
    {
        public SpecificationResult(
            Specification specification,
            IReadOnlyList<RequirementResult> results,
            int applicableCount,
            string? cardinalityFailure)
        {
            Specification = specification;
            Results = results;
            ApplicableCount = applicableCount;
            CardinalityFailure = cardinalityFailure;
        }

        public Specification Specification { get; }
        public IReadOnlyList<RequirementResult> Results { get; }

        /// <summary>How many entities the applicability selected. Zero is worth reporting on its own:
        /// a specification that matched nothing has told you nothing, and it usually means the
        /// applicability is wrong rather than the model being perfect.</summary>
        public int ApplicableCount { get; }

        /// <summary>Set when minOccurs/maxOccurs was violated — "there must be at least one of these".</summary>
        public string? CardinalityFailure { get; }

        public bool Passed =>
            CardinalityFailure == null && !Results.Any(r => r.Kind == ResultKind.Failed);

        public int FailedCount => Results.Count(r => r.Kind == ResultKind.Failed);
        public int NotCheckedCount => Results.Count(r => r.Kind == ResultKind.NotChecked);
    }

    /// <summary>
    /// Runs the specifications of an <see cref="IdsDocument"/> over a set of entities.
    ///
    /// <para>Stateless and side-effect free. Everything model-specific happens behind
    /// <see cref="IModelEntity"/>, which is what keeps this testable with a handful of doubles and
    /// what lets the same code answer for a Revit document and for an exported IFC.</para>
    /// </summary>
    public static class IdsEvaluator
    {
        public static IReadOnlyList<SpecificationResult> Evaluate(
            IdsDocument document, IEnumerable<IModelEntity> entities)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (entities == null) throw new ArgumentNullException(nameof(entities));

            // Materialised once: the specifications each iterate the whole set, and an implementation
            // backed by a live model query would otherwise be re-run per specification.
            List<IModelEntity> all = entities.ToList();

            return document.Specifications
                .Select(specification => Evaluate(specification, all))
                .ToList();
        }

        public static SpecificationResult Evaluate(
            Specification specification, IReadOnlyList<IModelEntity> entities)
        {
            List<IModelEntity> applicable = entities.Where(e => IsApplicable(specification, e)).ToList();

            string? cardinality = null;
            if (applicable.Count < specification.MinOccurs)
                cardinality =
                    $"{applicable.Count} entities match this specification, but at least " +
                    $"{specification.MinOccurs} were required.";
            else if (specification.MaxOccurs.HasValue && applicable.Count > specification.MaxOccurs.Value)
                cardinality =
                    $"{applicable.Count} entities match this specification, but at most " +
                    $"{specification.MaxOccurs.Value} were allowed.";

            List<RequirementResult> results = new List<RequirementResult>();
            foreach (IModelEntity entity in applicable)
                foreach (Facet requirement in specification.Requirements)
                    results.Add(Check(specification, requirement, entity));

            return new SpecificationResult(specification, results, applicable.Count, cardinality);
        }

        /// <summary>Every applicability facet must match. An empty applicability selects nothing rather
        /// than everything: a specification with no applicability is malformed, and treating it as
        /// "all entities" would apply someone's half-written rule to the whole model.</summary>
        private static bool IsApplicable(Specification specification, IModelEntity entity)
        {
            if (specification.Applicability.Count == 0) return false;

            foreach (Facet facet in specification.Applicability)
            {
                // An unsupported facet in APPLICABILITY cannot select, so the specification simply does
                // not apply. Reported through ApplicableCount = 0, which is why that count is published.
                if (!facet.IsSupported) return false;
                if (Satisfies(facet, entity, out _) != true) return false;
            }

            return true;
        }

        private static RequirementResult Check(
            Specification specification, Facet requirement, IModelEntity entity)
        {
            if (!requirement.IsSupported)
                return new RequirementResult(
                    specification, requirement, entity, ResultKind.NotChecked,
                    $"This version does not evaluate {requirement.Describe()}, so nothing was verified.");

            bool? satisfied = Satisfies(requirement, entity, out string reason);

            if (satisfied == null)
                return new RequirementResult(
                    specification, requirement, entity, ResultKind.NotChecked, reason);

            bool wanted = requirement.Cardinality != Cardinality.Prohibited;

            // Optional says nothing either way — it is guidance to the author, not a test.
            if (requirement.Cardinality == Cardinality.Optional)
                return new RequirementResult(
                    specification, requirement, entity, ResultKind.Passed,
                    satisfied.Value ? reason : $"{reason} (optional, so not a failure)");

            if (satisfied.Value == wanted)
                return new RequirementResult(specification, requirement, entity, ResultKind.Passed, reason);

            string why = requirement.Cardinality == Cardinality.Prohibited
                ? $"{requirement.Describe()} is prohibited here, but it is present."
                : reason;

            return new RequirementResult(specification, requirement, entity, ResultKind.Failed, why);
        }

        /// <summary>
        /// True / false / null, where null means "could not be determined from this model".
        ///
        /// <para>The three-way answer is what keeps <see cref="ResultKind.NotChecked"/> honest: a facet
        /// whose property set is a pattern needs enumeration, and an implementation that cannot
        /// enumerate must not have its silence read as a clean result.</para>
        /// </summary>
        private static bool? Satisfies(Facet facet, IModelEntity entity, out string reason)
        {
            switch (facet)
            {
                case EntityFacet e:
                    return SatisfiesEntity(e, entity, out reason);
                case AttributeFacet a:
                    return SatisfiesAttribute(a, entity, out reason);
                case PropertyFacet p:
                    return SatisfiesProperty(p, entity, out reason);
                default:
                    reason = $"{facet.Describe()} was not evaluated.";
                    return null;
            }
        }

        private static bool SatisfiesEntity(EntityFacet facet, IModelEntity entity, out string reason)
        {
            if (!facet.Name.Matches(entity.IfcClass))
            {
                reason = $"is {entity.IfcClass}, not {facet.Name.Describe()}";
                return false;
            }

            if (facet.PredefinedType != null && !facet.PredefinedType.Matches(entity.PredefinedType))
            {
                reason = $"predefined type is " +
                         (entity.PredefinedType == null ? "not set" : $"\"{entity.PredefinedType}\"") +
                         $", not {facet.PredefinedType.Describe()}";
                return false;
            }

            reason = $"is {entity.IfcClass}";
            return true;
        }

        private static bool? SatisfiesAttribute(AttributeFacet facet, IModelEntity entity, out string reason)
        {
            IEnumerable<string> names = facet.Name.SimpleValue != null
                ? new[] { facet.Name.SimpleValue }
                : entity.EnumerateAttributeNames().Where(n => facet.Name.Matches(n));

            bool sawAny = false;
            foreach (string name in names)
            {
                sawAny = true;
                if (!entity.TryGetAttribute(name, out string? value)) continue;

                // An attribute that exists but is blank does not satisfy a bare existence requirement.
                // IDS treats an empty value as no value, and this is the finding people are looking for.
                if (facet.Value == null)
                {
                    if (string.IsNullOrEmpty(value)) continue;
                    reason = $"attribute {name} is \"{value}\"";
                    return true;
                }

                if (facet.Value.Matches(value))
                {
                    reason = $"attribute {name} is \"{value}\"";
                    return true;
                }
            }

            if (!sawAny && facet.Name.SimpleValue == null)
            {
                reason = $"{facet.Describe()} needs the attribute names of this entity, which this " +
                         "model does not enumerate.";
                return null;
            }

            reason = facet.Value == null
                ? $"attribute {facet.Name.Describe()} is missing or empty"
                : $"no attribute {facet.Name.Describe()} with a value {facet.Value.Describe()}";
            return false;
        }

        private static bool? SatisfiesProperty(PropertyFacet facet, IModelEntity entity, out string reason)
        {
            // The fast, exact path — both sides literal, which is what nearly every real specification
            // is written with.
            if (facet.PropertySet.SimpleValue != null && facet.BaseName.SimpleValue != null)
            {
                bool found = entity.TryGetProperty(
                    facet.PropertySet.SimpleValue, facet.BaseName.SimpleValue, out string? value);

                string label = $"{facet.PropertySet.SimpleValue}.{facet.BaseName.SimpleValue}";

                if (!found)
                {
                    reason = $"{label} is not on this element";
                    return false;
                }

                if (facet.Value == null)
                {
                    if (string.IsNullOrEmpty(value))
                    {
                        reason = $"{label} is present but empty";
                        return false;
                    }
                    reason = $"{label} is \"{value}\"";
                    return true;
                }

                if (facet.Value.Matches(value))
                {
                    reason = $"{label} is \"{value}\"";
                    return true;
                }

                reason = $"{label} is " + (value == null ? "not set" : $"\"{value}\"") +
                         $", which is not {facet.Value.Describe()}";
                return false;
            }

            // A pattern on either side needs the entity to list what it has.
            List<KeyValuePair<string, string>> properties = entity.EnumerateProperties().ToList();
            if (properties.Count == 0)
            {
                reason = $"{facet.Describe()} needs the property names of this entity, which this " +
                         "model does not enumerate.";
                return null;
            }

            foreach (KeyValuePair<string, string> property in properties)
            {
                int dot = property.Key.IndexOf('.');
                if (dot <= 0) continue;

                string set = property.Key.Substring(0, dot);
                string name = property.Key.Substring(dot + 1);

                if (!facet.PropertySet.Matches(set) || !facet.BaseName.Matches(name)) continue;

                if (facet.Value == null)
                {
                    if (string.IsNullOrEmpty(property.Value)) continue;
                    reason = $"{property.Key} is \"{property.Value}\"";
                    return true;
                }

                if (facet.Value.Matches(property.Value))
                {
                    reason = $"{property.Key} is \"{property.Value}\"";
                    return true;
                }
            }

            reason = $"no property matching {facet.Describe()}";
            return false;
        }
    }
}
