using Xunit;

namespace AnalyseTool.Fragments.Tests
{
    public class AxesTests
    {
        [Fact]
        public void Revit_up_becomes_frag_up()
        {
            // The one that matters: straight up in Revit must land on +Y, or the model shows up
            // rotated 90° in the viewer.
            Assert.Equal(new Vec3(0, 1, 0), Axes.ZUpToYUp(new Vec3(0, 0, 1)));
        }

        [Theory]
        [InlineData(1, 0, 0, 1, 0, 0)]     // east stays east
        [InlineData(0, 1, 0, 0, 0, -1)]    // north folds onto -Z
        [InlineData(0, 0, 1, 0, 1, 0)]     // up folds onto +Y
        [InlineData(3, 5, 7, 3, 7, -5)]
        public void Points_map_x_z_minus_y(
            double x, double y, double z, double ex, double ey, double ez)
        {
            Assert.Equal(new Vec3(ex, ey, ez), Axes.ZUpToYUp(new Vec3(x, y, z)));
        }

        [Fact]
        public void Conversion_is_reversible()
        {
            Vec3 original = new(1.5, -2.5, 3.5);
            Assert.Equal(original, Axes.YUpToZUp(Axes.ZUpToYUp(original)));
        }

        [Fact]
        public void Converted_frame_stays_right_handed()
        {
            FragTransform converted = Axes.ZUpToYUp(FragTransform.Identity);

            // Z' must equal X' × Y' for the implied third axis to point the right way.
            Vec3 x = converted.XDirection, y = converted.YDirection;
            Vec3 cross = new(
                x.Y * y.Z - x.Z * y.Y,
                x.Z * y.X - x.X * y.Z,
                x.X * y.Y - x.Y * y.X);

            Vec3 expectedZ = Axes.ZUpToYUp(new Vec3(0, 0, 1));
            Assert.Equal(expectedZ, cross);
        }

        [Fact]
        public void Converting_a_model_rotates_placements_but_leaves_local_geometry_alone()
        {
            FragmentModel model = BuildColumnModel();

            Axes.ZUpToYUp(model);

            Assert.Equal(new Vec3(4, 6, -5), model.Items[0].Placement.Position);
            Assert.Equal(new Vec3(10, 30, -20), model.Coordinates.Position);

            // Untouched: both live inside the item's frame, which the placement already rotated.
            // Rotating them as well would apply the rotation twice.
            Assert.Equal(new Vec3(0, 0, 2), model.Shells[0].Points[2]);
            Assert.Equal(new Vec3(0, 0, 3), model.Items[0].Samples[0].LocalTransform.Position);
        }

        [Fact]
        public void A_column_that_points_up_in_revit_still_points_up_after_conversion()
        {
            // The invariant that actually matters: the composed world position must be the Revit
            // world position, rotated exactly once.
            FragmentModel model = BuildColumnModel();
            Vec3 before = WorldPositionOfShellPoint(model, 2);

            Axes.ZUpToYUp(model);
            Vec3 after = WorldPositionOfShellPoint(model, 2);

            Assert.Equal(Axes.ZUpToYUp(before), after);

            // Concretely: the column sits at z=6, its local transform lifts it 3 more, and the point
            // is 2 above that — 11 m up in Revit, so +11 on Y once converted.
            Assert.Equal(11.0, before.Z);
            Assert.Equal(11.0, after.Y);
        }

        private static FragmentModel BuildColumnModel()
        {
            FragmentModel model = new() { Coordinates = FragTransform.At(10, 20, 30) };
            model.Materials.Add(FragMaterial.Grey);

            FragShell shell = new();
            shell.Points.AddRange(new[] { new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(0, 0, 2) });
            shell.Profiles.Add(new FragProfile(new[] { 0, 1, 2 }));
            model.Shells.Add(shell);

            FragItem column = new(1, "Structural Columns") { Placement = FragTransform.At(4, 5, 6) };
            column.Samples.Add(new FragSample(0, 0) { LocalTransform = FragTransform.At(0, 0, 3) });
            model.Items.Add(column);

            return model;
        }

        /// <summary>Composes placement(local(point)) the way the viewer does.</summary>
        private static Vec3 WorldPositionOfShellPoint(FragmentModel model, int pointIndex)
        {
            FragItem item = model.Items[0];
            Vec3 point = model.Shells[0].Points[pointIndex];
            return Apply(item.Placement, Apply(item.Samples[0].LocalTransform, point));
        }

        private static Vec3 Apply(FragTransform t, Vec3 p)
        {
            Vec3 x = t.XDirection, y = t.YDirection;
            Vec3 z = new(
                x.Y * y.Z - x.Z * y.Y,
                x.Z * y.X - x.X * y.Z,
                x.X * y.Y - x.Y * y.X);

            return new Vec3(
                t.Position.X + p.X * x.X + p.Y * y.X + p.Z * z.X,
                t.Position.Y + p.X * x.Y + p.Y * y.Y + p.Z * z.Y,
                t.Position.Z + p.X * x.Z + p.Y * y.Z + p.Z * z.Z);
        }
    }
}
