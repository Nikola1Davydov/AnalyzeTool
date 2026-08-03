using AnalyseTool.Sdk;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using System.ComponentModel;

namespace AnalyseTool.Tools.Bot
{
    /// <summary>
    /// Stands the bot at a point and reports what it sees: the room it is in, the floor under it, the
    /// ceiling over it and a fan of horizontal sight lines.
    ///
    /// <para>This is the first-person view of the model that schedules and filters cannot give — how far
    /// the walls are, what is overhead, whether the point is even inside a room. It is the context an AI
    /// agent needs before it can reason about a place rather than about a list of elements.</para>
    /// </summary>
    [RevitCommand(
        Description = "Puts a probe at a point (project coordinates, METRES) and reports what surrounds it: " +
                      "the room it stands in, the distance down to the floor and up to the ceiling, and a fan " +
                      "of horizontal rays with the first element each one meets. Use it to understand a " +
                      "location from the inside — clear widths, what is overhead, whether the point is " +
                      "enclosed at all. Reads only — it builds a throwaway 3D view for the rays and rolls it back.",
        ReadOnly = true,
        InputType = typeof(BotLookAround.Request))]
    internal sealed class BotLookAround : IRevitTask
    {
        private const int MinRays = 4;
        private const int MaxRays = 64;

        public Task<object?> ExecuteAsync(IRevitContext revitContext, CancellationToken cancellationToken)
        {
            Request request = revitContext.Payload.As<Request>() ?? new Request();
            int rayCount = Math.Clamp(request.Rays ?? 16, MinRays, MaxRays);
            double eyeHeightM = request.EyeHeightM ?? 1.6;

            return revitContext.RunInRevitAsync<object?>(app =>
            {
                Document doc = app.ActiveUIDocument.Document;

                XYZ stand = BotUnits.ToXyz(request.X, request.Y, request.Z);
                XYZ eye = BotUnits.ToXyz(request.X, request.Y, request.Z + eyeHeightM);
                // Just off the floor: a point exactly on the slab is ambiguous for room containment.
                XYZ ankle = BotUnits.ToXyz(request.X, request.Y, request.Z + 0.05);

                Room? room = doc.GetRoomAtPoint(ankle) ?? doc.GetRoomAtPoint(eye);

                using BotScanScope scope = BotScanScope.Create(doc);
                BotRayService rays = new(doc, scope.View, request.IncludeLinks ?? true);

                List<BotSightLine> horizon = new(rayCount);
                for (int i = 0; i < rayCount; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    double bearing = i * 360d / rayCount;
                    double radians = bearing * Math.PI / 180d;
                    XYZ direction = new(Math.Cos(radians), Math.Sin(radians), 0);
                    horizon.Add(new BotSightLine($"{bearing:0}°", Math.Round(bearing, 1), rays.Nearest(eye, direction)));
                }

                BotHit? ceiling = rays.Nearest(eye, XYZ.BasisZ);
                BotHit? floor = rays.Nearest(eye, XYZ.BasisZ.Negate());

                List<BotSightLine> reached = horizon.Where(s => s.Hit != null).ToList();

                return new
                {
                    ok = true,
                    stand = BotUnits.ToPoint(stand),
                    eye = BotUnits.ToPoint(eye),
                    eyeHeightM,
                    room = room == null
                        ? null
                        : new { id = room.Id.Value, name = SafeName(room), number = room.Number, level = room.Level?.Name },
                    inRoom = room != null,
                    ceiling,
                    floor,
                    // Clear height at the point: down to the floor plus up to whatever is over the eye.
                    clearHeightM = ceiling == null || floor == null
                        ? (double?)null
                        : Math.Round(ceiling.DistanceM + floor.DistanceM, 3),
                    nearestObstacleM = reached.Count == 0 ? (double?)null : reached.Min(s => s.Hit!.DistanceM),
                    openDirections = horizon.Count - reached.Count,
                    horizon,
                };
            });
        }

        private static string SafeName(Element element)
        {
            try { return element.Name; }
            catch { return $"<{element.GetType().Name}>"; }
        }

        internal sealed class Request
        {
            [Description("Standing point X, in metres (project coordinates).")]
            public double X { get; set; }

            [Description("Standing point Y, in metres.")]
            public double Y { get; set; }

            [Description("Floor level Z at that point, in metres. The rays are cast from eyeHeightM above it.")]
            public double Z { get; set; }

            [Description("Eye height above the standing point, in metres. Default 1.6.")]
            public double? EyeHeightM { get; set; }

            [Description("How many horizontal rays to fan out, 4..64. Default 16. Bearings are measured " +
                         "counter-clockwise from the project +X axis, not from true north.")]
            public int? Rays { get; set; }

            [Description("Also hit elements inside linked models. Default true.")]
            public bool? IncludeLinks { get; set; }
        }
    }
}
