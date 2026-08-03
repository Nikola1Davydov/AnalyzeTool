using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace AnalyseTool.Tools.Bot
{
    /// <summary>
    /// The bot's first real job: stand on a grid of points across every room and look UP.
    ///
    /// <para>This is deliberately not clash detection. Nothing here intersects — a duct 1.9 m over a
    /// corridor floor collides with nothing at all, which is exactly why coordination keeps shipping it
    /// and why it keeps surfacing on site. The only way to see it is to ask the question from where a
    /// person stands.</para>
    /// </summary>
    public sealed class HeadroomService
    {
        // The ray starts slightly above the room base so it does not immediately report the floor finish
        // it starts on.
        private const double OriginOffsetM = 0.05;

        // Anything found below this is the floor build-up, a threshold or a skirting — not something you
        // walk into face-first. Keeps slab self-hits out of the report.
        private const double IgnoreBelowM = 0.3;

        /// <summary>Rooms to sweep, decided in one cheap round-trip so the caller can chunk the expensive
        /// part and keep Revit responsive.</summary>
        public List<long> PlanRooms(Document doc, HeadroomOptions options)
        {
            IEnumerable<Room> rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .OfType<Room>()
                .Where(r => r.Area > 1e-6); // unplaced / not enclosed rooms have no area and no volume to walk

            if (options.RoomIds is { Count: > 0 })
            {
                HashSet<long> wanted = new(options.RoomIds);
                rooms = rooms.Where(r => wanted.Contains(r.Id.Value));
            }

            if (options.LevelNames is { Count: > 0 })
            {
                HashSet<string> levels = new(options.LevelNames, StringComparer.OrdinalIgnoreCase);
                rooms = rooms.Where(r => r.Level != null && levels.Contains(r.Level.Name));
            }

            return rooms.Select(r => r.Id.Value).ToList();
        }

        /// <summary>Sweeps one batch of rooms. Creates (and rolls back) its own scan view, so batches are
        /// independent and the model is untouched between them.</summary>
        public List<RoomHeadroom> ScanRooms(
            Document doc,
            IReadOnlyList<long> roomIds,
            HeadroomOptions options,
            CancellationToken cancellationToken)
        {
            List<RoomHeadroom> results = new(roomIds.Count);
            if (roomIds.Count == 0) return results;

            using BotScanScope scope = BotScanScope.Create(doc);
            BotRayService rays = new(doc, scope.View, options.IncludeLinks);

            foreach (long id in roomIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (doc.GetElement(new ElementId(id)) is not Room room) continue;
                results.Add(ScanRoom(room, rays, options, cancellationToken));
            }

            return results;
        }

        private static RoomHeadroom ScanRoom(
            Room room,
            BotRayService rays,
            HeadroomOptions options,
            CancellationToken cancellationToken)
        {
            string name = SafeName(room);
            string? number = room.Number;
            string? level = room.Level?.Name;

            BoundingBoxXYZ? box = room.get_BoundingBox(null);
            if (box == null)
                return new RoomHeadroom(room.Id.Value, name, number, level, 0, 0, options.GridStepM, null, null, null,
                    "No geometry — the room is not bounded.");

            double step = ChooseStep(box, options);
            double originOffset = BotUnits.ToFeet(OriginOffsetM);
            double originZ = box.Min.Z + originOffset;
            double ignoreBelow = BotUnits.ToFeet(IgnoreBelowM);
            double limit = BotUnits.ToFeet(options.MinClearanceM);

            int sampled = 0, low = 0;
            double? worstClearance = null;
            XYZ? worstPoint = null;
            BotHit? worstHit = null;

            // Half-step inset: sampling the boundary itself is noise (you stand IN the room, not in its wall).
            for (double x = box.Min.X + step / 2; x < box.Max.X; x += step)
            {
                cancellationToken.ThrowIfCancellationRequested();

                for (double y = box.Min.Y + step / 2; y < box.Max.Y; y += step)
                {
                    XYZ origin = new(x, y, originZ);
                    if (!room.IsPointInRoom(origin)) continue;

                    sampled++;

                    BotHit? hit = rays.Nearest(origin, XYZ.BasisZ);
                    if (hit == null) continue; // nothing overhead at all — open to the sky, not a defect

                    // Clearance is measured from the floor, the ray from just above it.
                    double clearanceFt = BotUnits.ToFeet(hit.DistanceM) + originOffset;
                    if (clearanceFt < ignoreBelow) continue;
                    if (clearanceFt >= limit) continue;

                    low++;
                    if (worstClearance == null || clearanceFt < worstClearance)
                    {
                        worstClearance = clearanceFt;
                        worstPoint = origin;
                        worstHit = hit;
                    }
                }
            }

            return new RoomHeadroom(
                room.Id.Value,
                name,
                number,
                level,
                sampled,
                low,
                Math.Round(BotUnits.ToMetres(step), 3),
                worstClearance == null ? null : BotUnits.Round(worstClearance.Value),
                worstPoint == null ? null : BotUnits.ToPoint(worstPoint),
                worstHit,
                sampled == 0 ? "No sample point fell inside the room — try a finer grid step." : null);
        }

        /// <summary>Keeps a hall from eating the whole ray budget: coarsen the grid until the room fits it.
        /// Reported back per room, so a coarse sweep never looks like a fine one.</summary>
        private static double ChooseStep(BoundingBoxXYZ box, HeadroomOptions options)
        {
            double step = BotUnits.ToFeet(options.GridStepM);
            double area = (box.Max.X - box.Min.X) * (box.Max.Y - box.Min.Y);
            if (step <= 0 || area <= 0 || options.MaxPointsPerRoom <= 0) return Math.Max(step, 0.1);

            double estimated = area / (step * step);
            if (estimated <= options.MaxPointsPerRoom) return step;

            return step * Math.Sqrt(estimated / options.MaxPointsPerRoom);
        }

        private static string SafeName(Element element)
        {
            try { return element.Name; }
            catch { return $"<{element.GetType().Name}>"; }
        }
    }
}
