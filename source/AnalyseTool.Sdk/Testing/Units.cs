using Autodesk.Revit.DB;

namespace AnalyseTool.Sdk.Testing
{
    /// <summary>
    ///     Lengths, areas, volumes and angles in the units people think in, turned into Revit's internal units (feet, square
    ///     feet, cubic feet, radians) by Revit itself (<see cref="UnitUtils.ConvertToInternalUnits"/>) — what <c>InRange</c> and
    ///     the Revit API compare against.
    /// </summary>
    public static class Units
    {
        /// <summary>Millimetres to feet: <c>900.Mm()</c>.</summary>
        public static double Mm(this double value) => UnitUtils.ConvertToInternalUnits(value, UnitTypeId.Millimeters);

        /// <inheritdoc cref="Mm(double)"/>
        public static double Mm(this int value) => Mm((double)value);

        /// <summary>Metres to feet: <c>0.9.M()</c>.</summary>
        public static double M(this double value) => UnitUtils.ConvertToInternalUnits(value, UnitTypeId.Meters);

        /// <inheritdoc cref="M(double)"/>
        public static double M(this int value) => M((double)value);

        /// <summary>Square metres to square feet: <c>InRange(1.M2(), 2000.M2())</c>.</summary>
        public static double M2(this double value) => UnitUtils.ConvertToInternalUnits(value, UnitTypeId.SquareMeters);

        /// <inheritdoc cref="M2(double)"/>
        public static double M2(this int value) => M2((double)value);

        /// <summary>Cubic metres to cubic feet: <c>0.5.M3()</c>.</summary>
        public static double M3(this double value) => UnitUtils.ConvertToInternalUnits(value, UnitTypeId.CubicMeters);

        /// <inheritdoc cref="M3(double)"/>
        public static double M3(this int value) => M3((double)value);

        /// <summary>Degrees to radians: <c>InRange(0, 45.Deg())</c> for a slope or a rotation.</summary>
        public static double Deg(this double value) => UnitUtils.ConvertToInternalUnits(value, UnitTypeId.Degrees);

        /// <inheritdoc cref="Deg(double)"/>
        public static double Deg(this int value) => Deg((double)value);
    }
}
