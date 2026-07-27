using System.IO.Compression;
using System.Text.Json;
using Google.FlatBuffers;
using Schema = AnalyseTool.Fragments.Schema;

namespace AnalyseTool.Fragments
{
    /// <summary>
    /// Serializes a <see cref="FragmentModel"/> into That Open's .frag format: a zlib-compressed
    /// FlatBuffers buffer. See SPEC.md for where the on-disk conventions come from — several of them
    /// are not what the schema comments suggest, and are derived from decoding a reference file.
    /// </summary>
    public static class FragmentWriter
    {
        /// <summary>Writes a .frag file. Compressed by default, which is what their loader expects.</summary>
        public static void Write(FragmentModel model, Stream output, bool compress = true)
        {
            byte[] payload = Serialize(model);
            if (!compress)
            {
                output.Write(payload, 0, payload.Length);
                return;
            }

            // zlib (RFC 1950), not gzip — the reference file starts with 78 9C and the viewer
            // inflates it with pako.
            using ZLibStream zlib = new(output, CompressionLevel.Optimal, leaveOpen: true);
            zlib.Write(payload, 0, payload.Length);
        }

        /// <summary>The raw FlatBuffers buffer, before compression. Useful for tests and debugging.</summary>
        public static byte[] Serialize(FragmentModel model)
        {
            if (model is null) throw new ArgumentNullException(nameof(model));
            Validate(model);

            FlatBufferBuilder builder = new(64 * 1024);

            // FlatBuffers is built bottom-up: every nested vector and table must be complete before
            // the table that references it is started. Hence the strict ordering below.

            // ---- geometry ---------------------------------------------------------------------
            Offset<Schema.Shell>[] shells = new Offset<Schema.Shell>[model.Shells.Count];
            for (int i = 0; i < model.Shells.Count; i++)
                shells[i] = WriteShell(builder, model.Shells[i]);

            VectorOffset shellsVector = Schema.Meshes.CreateShellsVector(builder, shells);
            VectorOffset circleExtrusionsVector =
                Schema.Meshes.CreateCircleExtrusionsVector(builder, Array.Empty<Offset<Schema.CircleExtrusion>>());

            // ---- flatten items into the format's parallel arrays ------------------------------
            Layout layout = Layout.Build(model);

            VectorOffset meshesItemsVector = Schema.Meshes.CreateMeshesItemsVector(builder, layout.MeshesItems);

            Schema.Meshes.StartSamplesVector(builder, layout.Samples.Count);
            for (int i = layout.Samples.Count - 1; i >= 0; i--)
            {
                SampleRow s = layout.Samples[i];
                Schema.Sample.CreateSample(builder, s.Item, s.Material, s.Representation, s.LocalTransform);
            }
            VectorOffset samplesVector = builder.EndVector();

            Schema.Meshes.StartRepresentationsVector(builder, model.Shells.Count);
            for (int i = model.Shells.Count - 1; i >= 0; i--)
            {
                Bounds bb = Bounds.Of(model.Shells[i]);
                Schema.Representation.CreateRepresentation(
                    builder, (uint)i,
                    bb.MinX, bb.MinY, bb.MinZ, bb.MaxX, bb.MaxY, bb.MaxZ,
                    Schema.RepresentationClass.SHELL);
            }
            VectorOffset representationsVector = builder.EndVector();

            Schema.Meshes.StartMaterialsVector(builder, model.Materials.Count);
            for (int i = model.Materials.Count - 1; i >= 0; i--)
            {
                FragMaterial m = model.Materials[i];
                Schema.Material.CreateMaterial(
                    builder, m.R, m.G, m.B, m.A,
                    m.DoubleSided ? Schema.RenderedFaces.TWO : Schema.RenderedFaces.ONE,
                    Schema.Stroke.DEFAULT);
            }
            VectorOffset materialsVector = builder.EndVector();

            VectorOffset localTransformsVector =
                WriteTransformVector(builder, layout.LocalTransforms, Schema.Meshes.StartLocalTransformsVector);
            VectorOffset globalTransformsVector =
                WriteTransformVector(builder, layout.GlobalTransforms, Schema.Meshes.StartGlobalTransformsVector);

            // The *_ids arrays are addressing handles drawn from the same counter as item localIds —
            // not 0..n-1 indices. See SPEC.md §5.
            IdAllocator ids = new(layout.NextFreeId);
            VectorOffset materialIdsVector =
                Schema.Meshes.CreateMaterialIdsVector(builder, ids.Take(model.Materials.Count));
            VectorOffset representationIdsVector =
                Schema.Meshes.CreateRepresentationIdsVector(builder, ids.Take(model.Shells.Count));
            VectorOffset sampleIdsVector =
                Schema.Meshes.CreateSampleIdsVector(builder, ids.Take(layout.Samples.Count));
            VectorOffset localTransformIdsVector =
                Schema.Meshes.CreateLocalTransformIdsVector(builder, ids.Take(layout.LocalTransforms.Count));
            VectorOffset globalTransformIdsVector =
                Schema.Meshes.CreateGlobalTransformIdsVector(builder, ids.Take(layout.GlobalTransforms.Count));

            Schema.Meshes.StartMeshes(builder);
            Schema.Meshes.AddCoordinates(builder, Transform(builder, model.Coordinates));
            Schema.Meshes.AddMeshesItems(builder, meshesItemsVector);
            Schema.Meshes.AddSamples(builder, samplesVector);
            Schema.Meshes.AddRepresentations(builder, representationsVector);
            Schema.Meshes.AddMaterials(builder, materialsVector);
            Schema.Meshes.AddCircleExtrusions(builder, circleExtrusionsVector);
            Schema.Meshes.AddShells(builder, shellsVector);
            Schema.Meshes.AddLocalTransforms(builder, localTransformsVector);
            Schema.Meshes.AddGlobalTransforms(builder, globalTransformsVector);
            Schema.Meshes.AddMaterialIds(builder, materialIdsVector);
            Schema.Meshes.AddRepresentationIds(builder, representationIdsVector);
            Schema.Meshes.AddSampleIds(builder, sampleIdsVector);
            Schema.Meshes.AddLocalTransformIds(builder, localTransformIdsVector);
            Schema.Meshes.AddGlobalTransformIds(builder, globalTransformIdsVector);
            Offset<Schema.Meshes> meshes = Schema.Meshes.EndMeshes(builder);

            // ---- item data --------------------------------------------------------------------
            StringOffset[] categories = new StringOffset[model.Items.Count];
            for (int i = 0; i < model.Items.Count; i++)
                categories[i] = builder.CreateString(model.Items[i].Category);
            VectorOffset categoriesVector = Schema.Model.CreateCategoriesVector(builder, categories);

            Offset<Schema.Attribute>[] attributes = new Offset<Schema.Attribute>[model.Items.Count];
            for (int i = 0; i < model.Items.Count; i++)
                attributes[i] = WriteAttributes(builder, model.Items[i]);
            VectorOffset attributesVector = Schema.Model.CreateAttributesVector(builder, attributes);

            List<StringOffset> guids = new();
            List<uint> guidOwners = new();
            foreach (FragItem item in model.Items)
            {
                if (string.IsNullOrEmpty(item.Guid)) continue;
                guids.Add(builder.CreateString(item.Guid));
                guidOwners.Add(item.LocalId); // raw localId, NOT an index — SPEC.md §2
            }
            VectorOffset guidsVector = Schema.Model.CreateGuidsVector(builder, guids.ToArray());
            VectorOffset guidsItemsVector = Schema.Model.CreateGuidsItemsVector(builder, guidOwners.ToArray());

            VectorOffset localIdsVector = Schema.Model.CreateLocalIdsVector(builder, layout.LocalIds);

            StringOffset guid = builder.CreateString(model.Guid);
            StringOffset metadata = builder.CreateString(model.MetadataJson ?? "{}");

            Schema.Model.StartModel(builder);
            Schema.Model.AddMetadata(builder, metadata);
            Schema.Model.AddGuids(builder, guidsVector);
            Schema.Model.AddGuidsItems(builder, guidsItemsVector);
            Schema.Model.AddMaxLocalId(builder, layout.NextFreeId + ids.Issued);
            Schema.Model.AddLocalIds(builder, localIdsVector);
            Schema.Model.AddCategories(builder, categoriesVector);
            Schema.Model.AddMeshes(builder, meshes);
            Schema.Model.AddAttributes(builder, attributesVector);
            Schema.Model.AddGuid(builder, guid);
            Offset<Schema.Model> root = Schema.Model.EndModel(builder);

            // Finished WITHOUT a file identifier, matching what their own importer emits: the
            // reference file has zeroes at bytes 4..8 even though the schema declares "0001".
            builder.Finish(root.Value);
            return builder.SizedByteArray();
        }

        private static Offset<Schema.Shell> WriteShell(FlatBufferBuilder builder, FragShell shell)
        {
            // Point indices are ushort until a shell exceeds 65535 points, then the format switches
            // to the parallel big_* arrays with uint indices.
            bool big = shell.Points.Count > ushort.MaxValue;

            Offset<Schema.ShellProfile>[] profiles = Array.Empty<Offset<Schema.ShellProfile>>();
            Offset<Schema.BigShellProfile>[] bigProfiles = Array.Empty<Offset<Schema.BigShellProfile>>();
            Offset<Schema.ShellHole>[] holes = Array.Empty<Offset<Schema.ShellHole>>();
            Offset<Schema.BigShellHole>[] bigHoles = Array.Empty<Offset<Schema.BigShellHole>>();

            if (big)
            {
                bigProfiles = new Offset<Schema.BigShellProfile>[shell.Profiles.Count];
                for (int i = 0; i < shell.Profiles.Count; i++)
                {
                    VectorOffset indices = Schema.BigShellProfile.CreateIndicesVector(
                        builder, ToUInt(shell.Profiles[i].Indices));
                    bigProfiles[i] = Schema.BigShellProfile.CreateBigShellProfile(builder, indices);
                }

                bigHoles = new Offset<Schema.BigShellHole>[shell.Holes.Count];
                for (int i = 0; i < shell.Holes.Count; i++)
                {
                    VectorOffset indices = Schema.BigShellHole.CreateIndicesVector(
                        builder, ToUInt(shell.Holes[i].Indices));
                    bigHoles[i] = Schema.BigShellHole.CreateBigShellHole(builder, indices, shell.Holes[i].ProfileId);
                }
            }
            else
            {
                profiles = new Offset<Schema.ShellProfile>[shell.Profiles.Count];
                for (int i = 0; i < shell.Profiles.Count; i++)
                {
                    VectorOffset indices = Schema.ShellProfile.CreateIndicesVector(
                        builder, ToUShort(shell.Profiles[i].Indices));
                    profiles[i] = Schema.ShellProfile.CreateShellProfile(builder, indices);
                }

                holes = new Offset<Schema.ShellHole>[shell.Holes.Count];
                for (int i = 0; i < shell.Holes.Count; i++)
                {
                    VectorOffset indices = Schema.ShellHole.CreateIndicesVector(
                        builder, ToUShort(shell.Holes[i].Indices));
                    holes[i] = Schema.ShellHole.CreateShellHole(builder, indices, shell.Holes[i].ProfileId);
                }
            }

            VectorOffset profilesVector = Schema.Shell.CreateProfilesVector(builder, profiles);
            VectorOffset holesVector = Schema.Shell.CreateHolesVector(builder, holes);
            VectorOffset bigProfilesVector = Schema.Shell.CreateBigProfilesVector(builder, bigProfiles);
            VectorOffset bigHolesVector = Schema.Shell.CreateBigHolesVector(builder, bigHoles);

            ushort[] faceIds = new ushort[shell.Profiles.Count];
            for (int i = 0; i < shell.Profiles.Count; i++) faceIds[i] = shell.Profiles[i].FaceId;
            VectorOffset faceIdsVector = Schema.Shell.CreateProfilesFaceIdsVector(builder, faceIds);

            Schema.Shell.StartPointsVector(builder, shell.Points.Count);
            for (int i = shell.Points.Count - 1; i >= 0; i--)
            {
                Vec3 p = shell.Points[i];
                Schema.FloatVector.CreateFloatVector(builder, (float)p.X, (float)p.Y, (float)p.Z);
            }
            VectorOffset pointsVector = builder.EndVector();

            Schema.Shell.StartShell(builder);
            Schema.Shell.AddProfiles(builder, profilesVector);
            Schema.Shell.AddHoles(builder, holesVector);
            Schema.Shell.AddPoints(builder, pointsVector);
            Schema.Shell.AddBigProfiles(builder, bigProfilesVector);
            Schema.Shell.AddBigHoles(builder, bigHolesVector);
            Schema.Shell.AddType(builder, big ? Schema.ShellType.BIG : Schema.ShellType.NONE);
            Schema.Shell.AddProfilesFaceIds(builder, faceIdsVector);
            return Schema.Shell.EndShell(builder);
        }

        private static Offset<Schema.Attribute> WriteAttributes(FlatBufferBuilder builder, FragItem item)
        {
            StringOffset[] data = new StringOffset[item.Attributes.Count];
            for (int i = 0; i < item.Attributes.Count; i++)
            {
                FragAttribute a = item.Attributes[i];
                // Each attribute is stored as a JSON array string: ["Name","Value","Type"].
                string json = JsonSerializer.Serialize(new[] { a.Name, a.Value, a.Type });
                data[i] = builder.CreateString(json);
            }

            VectorOffset dataVector = Schema.Attribute.CreateDataVector(builder, data);
            return Schema.Attribute.CreateAttribute(builder, dataVector);
        }

        private static VectorOffset WriteTransformVector(
            FlatBufferBuilder builder, List<FragTransform> transforms, Action<FlatBufferBuilder, int> start)
        {
            start(builder, transforms.Count);
            for (int i = transforms.Count - 1; i >= 0; i--)
                Transform(builder, transforms[i]);
            return builder.EndVector();
        }

        private static Offset<Schema.Transform> Transform(FlatBufferBuilder builder, FragTransform t) =>
            Schema.Transform.CreateTransform(
                builder,
                t.Position.X, t.Position.Y, t.Position.Z,
                (float)t.XDirection.X, (float)t.XDirection.Y, (float)t.XDirection.Z,
                (float)t.YDirection.X, (float)t.YDirection.Y, (float)t.YDirection.Z);

        private static ushort[] ToUShort(IReadOnlyList<int> indices)
        {
            ushort[] result = new ushort[indices.Count];
            for (int i = 0; i < indices.Count; i++) result[i] = checked((ushort)indices[i]);
            return result;
        }

        private static uint[] ToUInt(IReadOnlyList<int> indices)
        {
            uint[] result = new uint[indices.Count];
            for (int i = 0; i < indices.Count; i++) result[i] = checked((uint)indices[i]);
            return result;
        }

        private static void Validate(FragmentModel model)
        {
            if (model.Items.Count == 0)
                throw new InvalidOperationException("A fragments model needs at least one item.");

            HashSet<uint> seen = new();
            foreach (FragItem item in model.Items)
            {
                if (!seen.Add(item.LocalId))
                    throw new InvalidOperationException($"Duplicate localId {item.LocalId}.");

                foreach (FragSample sample in item.Samples)
                {
                    if ((uint)sample.ShellIndex >= (uint)model.Shells.Count)
                        throw new InvalidOperationException(
                            $"Item {item.LocalId} references shell {sample.ShellIndex}, but the model has " +
                            $"{model.Shells.Count} shells.");
                    if ((uint)sample.MaterialIndex >= (uint)model.Materials.Count)
                        throw new InvalidOperationException(
                            $"Item {item.LocalId} references material {sample.MaterialIndex}, but the model has " +
                            $"{model.Materials.Count} materials.");
                }
            }

            for (int s = 0; s < model.Shells.Count; s++)
            {
                FragShell shell = model.Shells[s];
                foreach (FragProfile profile in shell.Profiles)
                {
                    if (profile.Indices.Count < 3)
                        throw new InvalidOperationException($"Shell {s} has a profile with fewer than 3 points.");
                    foreach (int index in profile.Indices)
                        if ((uint)index >= (uint)shell.Points.Count)
                            throw new InvalidOperationException(
                                $"Shell {s} has a profile index {index} outside its {shell.Points.Count} points.");
                }

                foreach (FragHole hole in shell.Holes)
                    if (hole.ProfileId >= shell.Profiles.Count)
                        throw new InvalidOperationException(
                            $"Shell {s} has a hole pointing at profile {hole.ProfileId}, which does not exist.");
            }
        }

        /// <summary>Hands out the unique ids the mesh entities are addressed by.</summary>
        private sealed class IdAllocator(uint next)
        {
            private uint _next = next;

            public uint Issued { get; private set; }

            public uint[] Take(int count)
            {
                uint[] result = new uint[count];
                for (int i = 0; i < count; i++) result[i] = _next++;
                Issued += (uint)count;
                return result;
            }
        }

        private readonly struct Bounds(float minX, float minY, float minZ, float maxX, float maxY, float maxZ)
        {
            public float MinX { get; } = minX;
            public float MinY { get; } = minY;
            public float MinZ { get; } = minZ;
            public float MaxX { get; } = maxX;
            public float MaxY { get; } = maxY;
            public float MaxZ { get; } = maxZ;

            public static Bounds Of(FragShell shell)
            {
                if (shell.Points.Count == 0) return new Bounds(0, 0, 0, 0, 0, 0);

                double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
                foreach (Vec3 p in shell.Points)
                {
                    if (p.X < minX) minX = p.X;
                    if (p.Y < minY) minY = p.Y;
                    if (p.Z < minZ) minZ = p.Z;
                    if (p.X > maxX) maxX = p.X;
                    if (p.Y > maxY) maxY = p.Y;
                    if (p.Z > maxZ) maxZ = p.Z;
                }

                return new Bounds((float)minX, (float)minY, (float)minZ, (float)maxX, (float)maxY, (float)maxZ);
            }
        }

        private readonly record struct SampleRow(uint Item, uint Material, uint Representation, uint LocalTransform);

        /// <summary>
        /// Turns the item tree into the format's flat, cross-indexed arrays: which items carry
        /// geometry, where each sample points, and which local transforms are worth storing once.
        /// </summary>
        private sealed class Layout
        {
            public uint[] LocalIds { get; private set; } = Array.Empty<uint>();

            /// <summary>Indices into <see cref="LocalIds"/> for items that have geometry, ascending.</summary>
            public uint[] MeshesItems { get; private set; } = Array.Empty<uint>();

            public List<FragTransform> GlobalTransforms { get; } = new();

            public List<FragTransform> LocalTransforms { get; } = new();

            public List<SampleRow> Samples { get; } = new();

            /// <summary>First id not taken by an item — where the mesh entity ids start.</summary>
            public uint NextFreeId { get; private set; }

            public static Layout Build(FragmentModel model)
            {
                Layout layout = new();

                uint[] localIds = new uint[model.Items.Count];
                uint maxLocalId = 0;
                for (int i = 0; i < model.Items.Count; i++)
                {
                    localIds[i] = model.Items[i].LocalId;
                    if (localIds[i] > maxLocalId) maxLocalId = localIds[i];
                }

                List<uint> meshesItems = new();
                Dictionary<FragTransform, uint> localTransformIndex = new();

                for (int i = 0; i < model.Items.Count; i++)
                {
                    FragItem item = model.Items[i];
                    if (item.Samples.Count == 0) continue;

                    // sample.item addresses the geometric item by its position in meshes_items,
                    // which is itself an index into local_ids. Two hops — SPEC.md §3.
                    uint geometricIndex = (uint)meshesItems.Count;
                    meshesItems.Add((uint)i);
                    layout.GlobalTransforms.Add(item.Placement);

                    foreach (FragSample sample in item.Samples)
                    {
                        if (!localTransformIndex.TryGetValue(sample.LocalTransform, out uint transformIndex))
                        {
                            transformIndex = (uint)layout.LocalTransforms.Count;
                            layout.LocalTransforms.Add(sample.LocalTransform);
                            localTransformIndex[sample.LocalTransform] = transformIndex;
                        }

                        layout.Samples.Add(new SampleRow(
                            geometricIndex, (uint)sample.MaterialIndex, (uint)sample.ShellIndex, transformIndex));
                    }
                }

                layout.LocalIds = localIds;
                layout.MeshesItems = meshesItems.ToArray();
                layout.NextFreeId = maxLocalId + 1;
                return layout;
            }
        }
    }
}
