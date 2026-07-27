using System.IO.Compression;
using Google.FlatBuffers;
using Xunit;
using Schema = AnalyseTool.Fragments.Schema;

namespace AnalyseTool.Fragments.Tests
{
    /// <summary>
    /// Locks in the on-disk conventions documented in SPEC.md — the ones the schema comments get
    /// wrong are exactly the ones worth a test, because a silent regression there produces a file
    /// that loads but renders or selects incorrectly.
    /// </summary>
    public class FragmentWriterTests
    {
        /// <summary>
        /// A cube of 1 m, as 6 quad profiles over 8 shared points. Quads rather than triangles so the
        /// tests also cover the polygonal-profile path the format is built around.
        /// </summary>
        private static FragShell Cube()
        {
            FragShell shell = new();
            shell.Points.AddRange(new[]
            {
                new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(1, 1, 0), new Vec3(0, 1, 0),
                new Vec3(0, 0, 1), new Vec3(1, 0, 1), new Vec3(1, 1, 1), new Vec3(0, 1, 1),
            });
            shell.Profiles.AddRange(new[]
            {
                new FragProfile(new[] { 0, 3, 2, 1 }, 0), // bottom
                new FragProfile(new[] { 4, 5, 6, 7 }, 1), // top
                new FragProfile(new[] { 0, 1, 5, 4 }, 2),
                new FragProfile(new[] { 1, 2, 6, 5 }, 3),
                new FragProfile(new[] { 2, 3, 7, 6 }, 4),
                new FragProfile(new[] { 3, 0, 4, 7 }, 5),
            });
            return shell;
        }

        /// <summary>
        /// Two walls sharing one cube geometry, plus a level that carries no geometry at all — the
        /// smallest model that exercises instancing AND the geometry/non-geometry item split.
        /// </summary>
        private static FragmentModel TwoWallsAndALevel()
        {
            FragmentModel model = new()
            {
                Guid = "11111111-2222-3333-4444-555555555555",
                MetadataJson = "{\"source\":\"AnalyseTool\"}",
                Coordinates = FragTransform.At(1000, 2000, 30),
            };
            model.Materials.Add(new FragMaterial(200, 100, 50));
            model.Materials.Add(new FragMaterial(10, 20, 30, 128, DoubleSided: true));
            model.Shells.Add(Cube());

            FragItem level = new(100, "Levels") { Guid = "level-guid" };
            level.Attributes.Add(new FragAttribute("Name", "Level 1"));
            model.Items.Add(level);

            FragItem wall1 = new(200, "Walls") { Guid = "wall-1-guid", Placement = FragTransform.At(5, 0, 0) };
            wall1.Attributes.Add(new FragAttribute("Name", "Basic Wall"));
            wall1.Attributes.Add(new FragAttribute("Height", "3000", "Length"));
            wall1.Samples.Add(new FragSample(0, 0));
            model.Items.Add(wall1);

            // No guid on purpose: guids/guids_items must stay shorter than local_ids.
            FragItem wall2 = new(300, "Walls") { Placement = FragTransform.At(9, 0, 0) };
            wall2.Samples.Add(new FragSample(0, 1));
            wall2.Samples.Add(new FragSample(0, 0) { LocalTransform = FragTransform.At(0, 0, 3) });
            model.Items.Add(wall2);

            return model;
        }

        private static Schema.Model Read(byte[] buffer) =>
            Schema.Model.GetRootAsModel(new ByteBuffer(buffer));

        [Fact]
        public void Items_are_written_as_arrays_parallel_to_local_ids()
        {
            Schema.Model m = Read(FragmentWriter.Serialize(TwoWallsAndALevel()));

            Assert.Equal(3, m.LocalIdsLength);
            Assert.Equal(new uint[] { 100, 200, 300 }, Enumerable.Range(0, 3).Select(m.LocalIds).ToArray());

            // categories is per item and repeated verbatim, not a deduplicated pool.
            Assert.Equal(3, m.CategoriesLength);
            Assert.Equal("Levels", m.Categories(0));
            Assert.Equal("Walls", m.Categories(1));
            Assert.Equal("Walls", m.Categories(2));

            Assert.Equal(3, m.AttributesLength);
            Schema.Attribute wallAttributes = m.Attributes(1)!.Value;
            Assert.Equal(2, wallAttributes.DataLength);
            Assert.Equal("[\"Name\",\"Basic Wall\",\"String\"]", wallAttributes.Data(0));
            Assert.Equal("[\"Height\",\"3000\",\"Length\"]", wallAttributes.Data(1));
        }

        [Fact]
        public void Guids_items_holds_raw_local_ids_not_indices()
        {
            Schema.Model m = Read(FragmentWriter.Serialize(TwoWallsAndALevel()));

            // Only two of the three items have a guid.
            Assert.Equal(2, m.GuidsLength);
            Assert.Equal(2, m.GuidsItemsLength);
            Assert.Equal("level-guid", m.Guids(0));
            Assert.Equal("wall-1-guid", m.Guids(1));

            // The localId values themselves — 100 and 200, not the indices 0 and 1.
            Assert.Equal(100u, m.GuidsItems(0));
            Assert.Equal(200u, m.GuidsItems(1));
        }

        [Fact]
        public void Meshes_items_holds_indices_into_local_ids_and_skips_items_without_geometry()
        {
            Schema.Model m = Read(FragmentWriter.Serialize(TwoWallsAndALevel()));
            Schema.Meshes meshes = m.Meshes!.Value;

            // The level has no geometry, so only the two walls appear — as indices 1 and 2.
            Assert.Equal(2, meshes.MeshesItemsLength);
            Assert.Equal(1u, meshes.MeshesItems(0));
            Assert.Equal(2u, meshes.MeshesItems(1));

            // One global transform per geometric item, positionally aligned with meshes_items.
            Assert.Equal(2, meshes.GlobalTransformsLength);
            Assert.Equal(5.0, meshes.GlobalTransforms(0)!.Value.Position.X);
            Assert.Equal(9.0, meshes.GlobalTransforms(1)!.Value.Position.X);
        }

        [Fact]
        public void Samples_address_the_geometric_item_by_its_meshes_items_position()
        {
            Schema.Model m = Read(FragmentWriter.Serialize(TwoWallsAndALevel()));
            Schema.Meshes meshes = m.Meshes!.Value;

            Assert.Equal(3, meshes.SamplesLength);

            Schema.Sample first = meshes.Samples(0)!.Value;
            Assert.Equal(0u, first.Item);          // wall1 == geometric item 0
            Assert.Equal(0u, first.Material);
            Assert.Equal(0u, first.Representation);

            Assert.Equal(1u, meshes.Samples(1)!.Value.Item); // both of wall2's samples
            Assert.Equal(1u, meshes.Samples(2)!.Value.Item);
            Assert.Equal(1u, meshes.Samples(1)!.Value.Material);

            // All three samples share the single cube — that is the instancing win.
            Assert.Equal(1, meshes.ShellsLength);
            Assert.Equal(1, meshes.RepresentationsLength);
            Assert.Equal(Schema.RepresentationClass.SHELL, meshes.Representations(0)!.Value.RepresentationClass);
            Assert.Equal(0u, meshes.Representations(0)!.Value.Id);
        }

        [Fact]
        public void Identical_local_transforms_are_stored_once()
        {
            Schema.Model m = Read(FragmentWriter.Serialize(TwoWallsAndALevel()));
            Schema.Meshes meshes = m.Meshes!.Value;

            // Two identity samples and one lifted by 3 m → two distinct local transforms.
            Assert.Equal(2, meshes.LocalTransformsLength);
            Assert.Equal(0u, meshes.Samples(0)!.Value.LocalTransform);
            Assert.Equal(0u, meshes.Samples(1)!.Value.LocalTransform);
            Assert.Equal(1u, meshes.Samples(2)!.Value.LocalTransform);
            Assert.Equal(3.0, meshes.LocalTransforms(1)!.Value.Position.Z);
        }

        [Fact]
        public void Shell_keeps_polygonal_profiles_face_ids_and_a_computed_bounding_box()
        {
            Schema.Model m = Read(FragmentWriter.Serialize(TwoWallsAndALevel()));
            Schema.Meshes meshes = m.Meshes!.Value;
            Schema.Shell shell = meshes.Shells(0)!.Value;

            Assert.Equal(Schema.ShellType.NONE, shell.Type);
            Assert.Equal(8, shell.PointsLength);
            Assert.Equal(6, shell.ProfilesLength);
            Assert.Equal(0, shell.BigProfilesLength);

            Schema.ShellProfile bottom = shell.Profiles(0)!.Value;
            Assert.Equal(4, bottom.IndicesLength);
            Assert.Equal(new ushort[] { 0, 3, 2, 1 }, Enumerable.Range(0, 4).Select(i => bottom.Indices(i)).ToArray());

            Assert.Equal(6, shell.ProfilesFaceIdsLength);
            Assert.Equal(new ushort[] { 0, 1, 2, 3, 4, 5 },
                Enumerable.Range(0, 6).Select(i => shell.ProfilesFaceIds(i)).ToArray());

            Schema.BoundingBox bbox = meshes.Representations(0)!.Value.Bbox;
            Assert.Equal(0f, bbox.Min.X);
            Assert.Equal(1f, bbox.Max.Z);
        }

        [Fact]
        public void Materials_carry_colour_and_double_sidedness()
        {
            Schema.Model m = Read(FragmentWriter.Serialize(TwoWallsAndALevel()));
            Schema.Meshes meshes = m.Meshes!.Value;

            Assert.Equal(2, meshes.MaterialsLength);
            Schema.Material opaque = meshes.Materials(0)!.Value;
            Assert.Equal(200, opaque.R);
            Assert.Equal(255, opaque.A);
            Assert.Equal(Schema.RenderedFaces.ONE, opaque.RenderedFaces);

            Schema.Material glass = meshes.Materials(1)!.Value;
            Assert.Equal(128, glass.A);
            Assert.Equal(Schema.RenderedFaces.TWO, glass.RenderedFaces);
        }

        [Fact]
        public void Mesh_entity_ids_come_from_the_shared_counter_after_the_items()
        {
            FragmentModel model = TwoWallsAndALevel();
            Schema.Model m = Read(FragmentWriter.Serialize(model));
            Schema.Meshes meshes = m.Meshes!.Value;

            // Highest item localId is 300, so mesh entity ids start at 301 and never collide.
            Assert.Equal(301u, meshes.MaterialIds(0));
            Assert.Equal(302u, meshes.MaterialIds(1));
            Assert.Equal(303u, meshes.RepresentationIds(0));

            Assert.Equal(meshes.MaterialsLength, meshes.MaterialIdsLength);
            Assert.Equal(meshes.SamplesLength, meshes.SampleIdsLength);
            Assert.Equal(meshes.GlobalTransformsLength, meshes.GlobalTransformIdsLength);

            // max_local_id is the next FREE id, past everything handed out.
            Assert.Equal(301u + 2 + 1 + 3 + 2 + 2, m.MaxLocalId);
        }

        [Fact]
        public void Model_level_fields_survive_the_round_trip()
        {
            Schema.Model m = Read(FragmentWriter.Serialize(TwoWallsAndALevel()));

            Assert.Equal("11111111-2222-3333-4444-555555555555", m.Guid);
            Assert.Equal("{\"source\":\"AnalyseTool\"}", m.Metadata);

            // The survey offset lives here, so geometry can stay small and float32-friendly.
            Schema.Transform coordinates = m.Meshes!.Value.Coordinates!.Value;
            Assert.Equal(1000.0, coordinates.Position.X);
            Assert.Equal(2000.0, coordinates.Position.Y);
            Assert.Equal(1f, coordinates.XDirection.X);
        }

        [Fact]
        public void Write_emits_a_zlib_stream_that_inflates_to_the_raw_buffer()
        {
            FragmentModel model = TwoWallsAndALevel();
            byte[] raw = FragmentWriter.Serialize(model);

            using MemoryStream file = new();
            FragmentWriter.Write(model, file);
            byte[] compressed = file.ToArray();

            // 78 9C is the zlib header their loader (pako) expects — gzip's 1F 8B would not load.
            Assert.Equal(0x78, compressed[0]);
            Assert.Equal(0x9C, compressed[1]);
            Assert.True(compressed.Length < raw.Length);

            using MemoryStream input = new(compressed);
            using ZLibStream inflate = new(input, CompressionMode.Decompress);
            using MemoryStream output = new();
            inflate.CopyTo(output);
            Assert.Equal(raw, output.ToArray());
        }

        [Fact]
        public void Shells_past_65535_points_switch_to_the_big_arrays()
        {
            FragmentModel model = new();
            model.Materials.Add(FragMaterial.Grey);

            FragShell shell = new();
            for (int i = 0; i <= ushort.MaxValue + 1; i++) shell.Points.Add(new Vec3(i, 0, 0));
            shell.Profiles.Add(new FragProfile(new[] { 0, 1, ushort.MaxValue + 1 }));
            model.Shells.Add(shell);

            FragItem item = new(1, "Generic");
            item.Samples.Add(new FragSample(0, 0));
            model.Items.Add(item);

            Schema.Shell written = Read(FragmentWriter.Serialize(model)).Meshes!.Value.Shells(0)!.Value;

            Assert.Equal(Schema.ShellType.BIG, written.Type);
            Assert.Equal(0, written.ProfilesLength);
            Assert.Equal(1, written.BigProfilesLength);
            // 65536 does not fit a ushort — this is exactly why the big path exists.
            Assert.Equal(65536u, written.BigProfiles(0)!.Value.Indices(2));
        }

        [Theory]
        [InlineData("duplicate")]
        [InlineData("dangling-shell")]
        [InlineData("dangling-material")]
        [InlineData("degenerate-profile")]
        [InlineData("out-of-range-index")]
        public void Broken_models_are_rejected_before_they_reach_disk(string flaw)
        {
            FragmentModel model = new();
            model.Materials.Add(FragMaterial.Grey);
            model.Shells.Add(Cube());

            FragItem item = new(1, "Walls");
            item.Samples.Add(new FragSample(0, 0));
            model.Items.Add(item);

            switch (flaw)
            {
                case "duplicate":
                    model.Items.Add(new FragItem(1, "Walls"));
                    break;
                case "dangling-shell":
                    model.Items[0].Samples.Add(new FragSample(7, 0));
                    break;
                case "dangling-material":
                    model.Items[0].Samples.Add(new FragSample(0, 7));
                    break;
                case "degenerate-profile":
                    model.Shells[0].Profiles.Add(new FragProfile(new[] { 0, 1 }));
                    break;
                case "out-of-range-index":
                    model.Shells[0].Profiles.Add(new FragProfile(new[] { 0, 1, 99 }));
                    break;
            }

            Assert.Throws<InvalidOperationException>(() => FragmentWriter.Serialize(model));
        }
    }
}
