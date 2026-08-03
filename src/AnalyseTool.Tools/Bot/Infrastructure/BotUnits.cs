using Autodesk.Revit.DB;

namespace AnalyseTool.Tools.Bot
{
    /// <summary>
    /// The bot talks METRES to its callers (a human or an AI reasoning about "2.1 m headroom") and FEET
    /// to Revit. Every crossing goes through here so no raw 0.3048 ever appears in a feature.
    /// </summary>
    public static class BotUnits
    {
        public static double ToFeet(double metres) => UnitUtils.ConvertToInternalUnits(metres, UnitTypeId.Meters);

        public static double ToMetres(double feet) => UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Meters);

        /// <summary>Metres, rounded for the wire — millimetre precision is more than any of this is worth.</summary>
        public static double Round(double feet) => Math.Round(ToMetres(feet), 3);

        public static BotPoint ToPoint(XYZ p) =>
            new(Math.Round(ToMetres(p.X), 3), Math.Round(ToMetres(p.Y), 3), Math.Round(ToMetres(p.Z), 3));

        public static XYZ ToXyz(double xM, double yM, double zM) =>
            new(ToFeet(xM), ToFeet(yM), ToFeet(zM));
    }
}
