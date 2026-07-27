using AnalyseTool.Fragments;

// Writes a demo .frag without needing Revit, so the file can be round-tripped through the real
// viewer and through tools/inspect_frag.py on any machine.
//
//   dotnet run --project src/AnalyseTool.Fragments.Cli -- out/demo.frag

string path = args.Length > 0 ? args[0] : "demo.frag";
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

// Built in Revit's Z-up frame, then rotated once — the format stores Y-up (SPEC.md §6).
FragmentModel model = DemoModel.Build();
Axes.ZUpToYUp(model);

using (FileStream file = File.Create(path))
    FragmentWriter.Write(model, file);

long raw = FragmentWriter.Serialize(model).LongLength;
long compressed = new FileInfo(path).Length;

Console.WriteLine($"Wrote {path}");
Console.WriteLine($"  items      : {model.Items.Count}");
Console.WriteLine($"  shells     : {model.Shells.Count}");
Console.WriteLine($"  samples    : {model.Items.Sum(i => i.Samples.Count)}");
Console.WriteLine($"  raw        : {raw:N0} bytes");
Console.WriteLine($"  compressed : {compressed:N0} bytes");

internal static class DemoModel
{
    /// <summary>
    /// A tiny "building": one level, a row of columns that all share a single geometry, and a slab.
    /// The shared geometry is the point — it is what a Revit family symbol maps onto, and what an
    /// IFC round-trip would usually flatten into one mesh per instance.
    /// </summary>
    public static FragmentModel Build()
    {
        FragmentModel model = new()
        {
            MetadataJson = "{\"source\":\"AnalyseTool\",\"schema\":\"Revit\"}",
        };

        model.Materials.Add(new FragMaterial(190, 190, 195));            // concrete
        model.Materials.Add(new FragMaterial(120, 140, 200, 140, true)); // glass, double sided

        int columnShell = model.Shells.Count;
        model.Shells.Add(Box(0.4, 0.4, 3.0));
        int slabShell = model.Shells.Count;
        model.Shells.Add(Box(12.0, 8.0, 0.3));

        uint id = 1000;

        FragItem level = new(id++, "Levels") { Guid = Guid.NewGuid().ToString() };
        level.Attributes.Add(new FragAttribute("Name", "Level 1"));
        level.Attributes.Add(new FragAttribute("Elevation", "0", "Length"));
        model.Items.Add(level);

        FragItem slab = new(id++, "Floors")
        {
            Guid = Guid.NewGuid().ToString(),
            Placement = FragTransform.At(0, 0, -0.3),
        };
        slab.Attributes.Add(new FragAttribute("Name", "Generic 300mm"));
        slab.Samples.Add(new FragSample(slabShell, 0));
        model.Items.Add(slab);

        // Centred on the slab, which spans ±6 × ±4.
        for (int x = 0; x < 4; x++)
        {
            for (int y = 0; y < 3; y++)
            {
                FragItem column = new(id++, "Structural Columns")
                {
                    Guid = Guid.NewGuid().ToString(),
                    Placement = FragTransform.At(-4.5 + x * 3.0, -2.5 + y * 2.5, 0),
                };
                column.Attributes.Add(new FragAttribute("Name", $"Column {x}-{y}"));
                column.Attributes.Add(new FragAttribute("Mark", $"C{x}{y}"));
                // Every column reuses the same shell: one geometry, twelve placements.
                column.Samples.Add(new FragSample(columnShell, x == 0 ? 1 : 0));
                model.Items.Add(column);
            }
        }

        return model;
    }

    /// <summary>An axis-aligned box centred in X/Y and rising from z=0, as six quad profiles.</summary>
    private static FragShell Box(double sizeX, double sizeY, double sizeZ)
    {
        double hx = sizeX / 2, hy = sizeY / 2;

        FragShell shell = new();
        shell.Points.AddRange(new[]
        {
            new Vec3(-hx, -hy, 0),     new Vec3(hx, -hy, 0),     new Vec3(hx, hy, 0),     new Vec3(-hx, hy, 0),
            new Vec3(-hx, -hy, sizeZ), new Vec3(hx, -hy, sizeZ), new Vec3(hx, hy, sizeZ), new Vec3(-hx, hy, sizeZ),
        });
        shell.Profiles.AddRange(new[]
        {
            new FragProfile(new[] { 0, 3, 2, 1 }, 0),
            new FragProfile(new[] { 4, 5, 6, 7 }, 1),
            new FragProfile(new[] { 0, 1, 5, 4 }, 2),
            new FragProfile(new[] { 1, 2, 6, 5 }, 3),
            new FragProfile(new[] { 2, 3, 7, 6 }, 4),
            new FragProfile(new[] { 3, 0, 4, 7 }, 5),
        });
        return shell;
    }
}
