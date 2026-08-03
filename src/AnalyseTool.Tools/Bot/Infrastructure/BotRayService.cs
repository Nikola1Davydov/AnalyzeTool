using Autodesk.Revit.DB;

namespace AnalyseTool.Tools.Bot
{
    /// <summary>
    /// The bot's only sense: cast a ray, report what it meets first. Everything else in this slice
    /// (headroom, look-around) is a pattern of rays on top of this.
    ///
    /// <para>Bind one instance to one <see cref="BotScanScope"/> and reuse it for the whole scan — the
    /// intersector caches per-view state, so rebuilding it per ray is pure waste.</para>
    /// </summary>
    public sealed class BotRayService
    {
        // A ray starting exactly on a face reports that face at ~0 distance. Anything closer than a
        // millimetre is the surface we are standing on/next to, not an obstruction.
        private const double SelfHitToleranceFt = 0.003;

        private readonly Document _doc;
        private readonly ReferenceIntersector _intersector;

        public BotRayService(Document doc, View3D view, bool includeLinks)
        {
            _doc = doc;
            _intersector = new ReferenceIntersector(
                new ElementIsElementTypeFilter(true), // inverted: instances only — types have no geometry to hit
                FindReferenceTarget.Element,
                view)
            {
                FindReferencesInRevitLinks = includeLinks,
            };
        }

        /// <summary>
        /// Nearest thing along <paramref name="direction"/> from <paramref name="origin"/>, or null when the
        /// ray leaves the model without meeting anything.
        /// </summary>
        /// <param name="skip">Optional veto by element id (host document ids), e.g. "ignore the floor I'm on".</param>
        public BotHit? Nearest(XYZ origin, XYZ direction, Func<long, bool>? skip = null)
        {
            if (direction.GetLength() < 1e-9)
                throw new ArgumentException("Ray direction is a zero-length vector.", nameof(direction));

            IList<ReferenceWithContext> hits = _intersector.Find(origin, direction.Normalize());
            if (hits == null || hits.Count == 0) return null;

            BotHit? best = null;
            foreach (ReferenceWithContext candidate in hits.OrderBy(h => h.Proximity))
            {
                if (candidate.Proximity <= SelfHitToleranceFt) continue;

                Reference reference = candidate.GetReference();
                if (skip != null && skip(reference.ElementId.Value)) continue;

                BotHit? hit = Describe(reference, candidate.Proximity);
                if (hit == null) continue;

                best = hit;
                break;
            }

            return best;
        }

        /// <summary>Resolves a reference to a reportable hit, dropping anything without model geometry
        /// (annotation, and the room/space volumes themselves, which are not things you can walk into).</summary>
        private BotHit? Describe(Reference reference, double proximityFt)
        {
            Element? element = _doc.GetElement(reference.ElementId);
            string? linkName = null;

            if (reference.LinkedElementId != ElementId.InvalidElementId && element is RevitLinkInstance link)
            {
                linkName = SafeName(link);
                Document? linkDoc = link.GetLinkDocument();
                element = linkDoc?.GetElement(reference.LinkedElementId);
            }

            if (element == null) return null;

            Category? category = element.Category;
            if (category == null || category.CategoryType != CategoryType.Model) return null;

            return new BotHit(
                element.Id.Value,
                SafeName(element),
                category.Name,
                BotUnits.Round(proximityFt),
                linkName);
        }

        // Element.Name throws for a handful of element kinds; a scan must not die on one of them.
        private static string SafeName(Element element)
        {
            try { return element.Name; }
            catch { return $"<{element.GetType().Name}>"; }
        }
    }
}
