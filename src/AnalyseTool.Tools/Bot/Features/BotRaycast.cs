using AnalyseTool.Sdk;
using Autodesk.Revit.DB;
using System.ComponentModel;

namespace AnalyseTool.Tools.Bot
{
    /// <summary>
    /// The primitive the rest of the slice is built from, exposed on its own because an AI agent needs it
    /// raw: "what is the first thing between this point and that one?" — a line-of-sight / obstruction
    /// question no schedule or filter can answer.
    /// </summary>
    [RevitCommand(
        Description = "Casts a single ray through the model from a point and returns the first element it " +
                      "meets (id, name, category, distance in metres). Give either a direction vector or a " +
                      "target point; with a target it also reports whether the line of sight is blocked " +
                      "(the hit is nearer than the target). All coordinates are project coordinates in " +
                      "METRES. Reads only — it builds a throwaway 3D view for the raycast and rolls it back.",
        ReadOnly = true,
        InputType = typeof(BotRaycast.Request))]
    internal sealed class BotRaycast : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext revitContext, CancellationToken cancellationToken)
        {
            Request request = revitContext.Payload.As<Request>() ?? new Request();

            return revitContext.RunInRevitAsync<object?>(app =>
            {
                Document doc = app.ActiveUIDocument.Document;
                XYZ origin = BotUnits.ToXyz(request.X, request.Y, request.Z);

                XYZ direction;
                double? targetDistanceM = null;

                if (request.TargetX.HasValue && request.TargetY.HasValue && request.TargetZ.HasValue)
                {
                    XYZ target = BotUnits.ToXyz(request.TargetX.Value, request.TargetY.Value, request.TargetZ.Value);
                    direction = target - origin;
                    if (direction.GetLength() < 1e-9)
                        throw new InvalidOperationException("Target and origin are the same point — no ray to cast.");
                    targetDistanceM = BotUnits.Round(direction.GetLength());
                }
                else
                {
                    direction = new XYZ(request.DirX ?? 0, request.DirY ?? 0, request.DirZ ?? 1);
                    if (direction.GetLength() < 1e-9)
                        throw new InvalidOperationException("Direction is a zero-length vector. Pass dirX/dirY/dirZ or a target point.");
                }

                using BotScanScope scope = BotScanScope.Create(doc);
                BotRayService rays = new(doc, scope.View, request.IncludeLinks ?? true);
                BotHit? hit = rays.Nearest(origin, direction);

                return new
                {
                    ok = true,
                    origin = BotUnits.ToPoint(origin),
                    hit,
                    targetDistanceM,
                    blocked = targetDistanceM.HasValue && hit != null && hit.DistanceM < targetDistanceM.Value - 1e-3,
                };
            });
        }

        internal sealed class Request
        {
            [Description("Ray origin X, in metres (project coordinates).")]
            public double X { get; set; }

            [Description("Ray origin Y, in metres.")]
            public double Y { get; set; }

            [Description("Ray origin Z, in metres.")]
            public double Z { get; set; }

            [Description("Direction X. Ignored when a target point is given. Default direction is straight up.")]
            public double? DirX { get; set; }

            [Description("Direction Y. Ignored when a target point is given.")]
            public double? DirY { get; set; }

            [Description("Direction Z. Ignored when a target point is given. Defaults to 1 (up).")]
            public double? DirZ { get; set; }

            [Description("Optional target X in metres — aims the ray at a point instead of a direction.")]
            public double? TargetX { get; set; }

            [Description("Optional target Y in metres.")]
            public double? TargetY { get; set; }

            [Description("Optional target Z in metres.")]
            public double? TargetZ { get; set; }

            [Description("Also hit elements inside linked models. Default true.")]
            public bool? IncludeLinks { get; set; }
        }
    }
}
