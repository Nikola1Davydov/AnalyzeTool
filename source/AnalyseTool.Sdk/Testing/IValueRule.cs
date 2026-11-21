using Autodesk.Revit.DB;

namespace AnalyseTool.Sdk.Testing
{
    /// <summary>
    /// A rule on one value of every element: the conditions are checked in order, the first that fails is the message. A
    /// missing parameter fails every condition.
    /// </summary>
    /// <typeparam name="T">The element class.</typeparam>
    public interface IValueRule<T> : IElementTest<T> where T : Element
    {
        /// <summary>The value exists and is not blank.</summary>
        IValueRule<T> NotEmpty();

        /// <summary>The value equals the text, ignoring case.</summary>
        IValueRule<T> Equal(string expected);

        /// <summary>The value matches the regular expression.</summary>
        IValueRule<T> Matches(string pattern);

        /// <summary>The value is one of these, ignoring case.</summary>
        IValueRule<T> OneOf(params string[] values);

        /// <summary>
        ///     The value is a number between the two, inclusive, in Revit's internal units: lengths in feet — write
        ///     <c>InRange(100.Mm(), 20000.Mm())</c> — areas in square feet (<c>1.M2()</c>), volumes in cubic feet (<c>0.5.M3()</c>), angles in radians (<c>45.Deg()</c>). The same suite checks the same
        ///     in a project in millimetres and one in metres; failures show the value in the units of the project.
        /// </summary>
        IValueRule<T> InRange(double min, double max);

        /// <summary>No two elements of the set share the value (blank values are left to <see cref="NotEmpty"/>).</summary>
        IValueRule<T> Unique();

        /// <summary>A repair that writes this value into the parameter (as typed in Revit, units included).</summary>
        IValueRule<T> FixValue(string value);
    }
}
