namespace AnalyseTool.Fragments
{
    /// <summary>
    /// Converts Revit's Z-up frame into the Y-up frame the .frag format stores.
    /// <para>
    /// Their IFC importer performs this conversion while writing — the reference file's storeys stack
    /// along +Y (see SPEC.md §6) even though IFC, like Revit, is Z-up. Skipping it lands the whole
    /// model on its side in the viewer, which is why it lives here rather than being left to callers
    /// to remember.
    /// </para>
    /// </summary>
    public static class Axes
    {
        /// <summary>(x, y, z) → (x, z, −y). Right-handed in, right-handed out.</summary>
        public static Vec3 ZUpToYUp(Vec3 v) => new(v.X, v.Z, -v.Y);

        /// <summary>Rotates a placement into the Y-up frame, direction vectors included.</summary>
        public static FragTransform ZUpToYUp(FragTransform t) => new(
            ZUpToYUp(t.Position),
            ZUpToYUp(t.XDirection),
            ZUpToYUp(t.YDirection));

        /// <summary>The inverse: (x, y, z) → (x, −z, y). Useful when reading a .frag back into Revit terms.</summary>
        public static Vec3 YUpToZUp(Vec3 v) => new(v.X, -v.Z, v.Y);

        /// <summary>
        /// Rotates a whole model into the Y-up frame. Call once, after building everything in Revit's
        /// frame.
        /// <para>
        /// Only the OUTER placements move — item placements and the survey coordinates. Shell points
        /// and sample local transforms are expressed inside the item's own frame, and that frame is
        /// itself rotated by the placement's direction vectors, so rotating them too would apply the
        /// rotation twice and stand the model on its head. Formally: a point lands at
        /// <c>placement(local(p))</c>, and rotating the model means <c>R(placement(local(p)))</c> —
        /// which is <c>(R∘placement)(local(p))</c>, i.e. a change to the placement alone.
        /// </para>
        /// <para>
        /// This also covers geometry tessellated straight into world coordinates under an identity
        /// placement: the rotation then lives in that placement's rotated basis vectors.
        /// </para>
        /// </summary>
        public static void ZUpToYUp(FragmentModel model)
        {
            if (model is null) throw new ArgumentNullException(nameof(model));

            model.Coordinates = ZUpToYUp(model.Coordinates);

            foreach (FragItem item in model.Items)
                item.Placement = ZUpToYUp(item.Placement);
        }
    }
}
