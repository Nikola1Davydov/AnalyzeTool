namespace AnalyseTool.Tools.Bot
{
    /// <summary>Tuning for a headroom sweep. All lengths in METRES (see <see cref="BotUnits"/>).</summary>
    public sealed class HeadroomOptions
    {
        /// <summary>Clearance below which a sample point counts as a defect. 2.1 m is the usual minimum
        /// clear height for a circulation route.</summary>
        public double MinClearanceM { get; set; } = 2.1;

        /// <summary>Spacing of the sample grid inside a room. Finer finds smaller obstructions and costs
        /// proportionally more rays.</summary>
        public double GridStepM { get; set; } = 1.0;

        /// <summary>Per-room ray budget. A room whose grid would exceed it is sampled on a coarser grid
        /// rather than silently cut off, and the grid actually used is reported back per room.</summary>
        public int MaxPointsPerRoom { get; set; } = 400;

        /// <summary>Whether obstructions inside linked models (typically the MEP link) count. They almost
        /// always do — that is where the ducts are.</summary>
        public bool IncludeLinks { get; set; } = true;

        /// <summary>Restrict to these level names (exact, case-insensitive). Null/empty = every level.</summary>
        public List<string>? LevelNames { get; set; }

        /// <summary>Restrict to these room ids. Null/empty = every placed room.</summary>
        public List<long>? RoomIds { get; set; }
    }
}
