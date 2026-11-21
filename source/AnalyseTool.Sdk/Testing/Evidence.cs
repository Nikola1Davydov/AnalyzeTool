using Autodesk.Revit.DB;

namespace AnalyseTool.Sdk.Testing
{
    /// <summary>What a failure points at: the page selects, isolates and draws it.</summary>
    public sealed class Evidence
    {
        /// <summary>The failing element; in a per-element check, the element under test when absent.</summary>
        public Element? Element { get; set; }

        /// <summary>Other elements involved: the other side of a clash, the duplicates.</summary>
        public IReadOnlyList<Element> Related { get; set; } = Array.Empty<Element>();

        /// <summary>A point to show.</summary>
        public XYZ? Point { get; set; }

        /// <summary>A box to show: a clearance zone, an overlap.</summary>
        public Outline? Box { get; set; }

        /// <summary>
        /// Tells failures of the same element apart in the baseline, when one element can fail one test in several ways
        /// (two clashes with different elements are told apart by <see cref="Related"/> already).
        /// </summary>
        public string? Key { get; set; }
    }
}
