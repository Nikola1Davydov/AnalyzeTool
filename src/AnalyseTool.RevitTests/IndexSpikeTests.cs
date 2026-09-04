using AnalyseTool.Core.Common.Index;
using Autodesk.Revit.DB;

namespace AnalyseTool.RevitTests;

/// <summary>
/// The Revit half of the phase-0 spike: the reader that turns elements into index rows, run against
/// the seeded model — one level, four walls — and written through the spike store into a real file
/// under the temp folder. The command (ModelIndexSpike) adds only the chunking and the timings; what
/// is checked here is that the rows say what the model says.
/// </summary>
public sealed class IndexSpikeTests : SeededModel
{
    [Test]
    public async Task The_seeded_walls_and_level_land_in_the_index_with_their_parameters()
    {
        string path = Path.Combine(Path.GetTempPath(), "analysetool-spike-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            using IndexSpikeStore store = IndexSpikeStore.Create(path);
            await Assert.That(store.JournalMode).IsEqualTo("wal");

            IReadOnlyList<ElementId> ids = ElementRowReader.CollectIds(Document);
            ElementRowReader reader = new(Document, withParameters: true);
            List<ElementRead> batch = ids
                .Select(id => Document.GetElement(id))
                .Where(element => element is not null)
                .Select(element => reader.Read(element!))
                .ToList();
            store.Write(batch);

            long walls = store.Scalar<long>("SELECT COUNT(*) FROM v_elements WHERE built_in_category = 'OST_Walls' AND is_type = 0");
            await Assert.That(walls).IsEqualTo(4);

            long wallTypes = store.Scalar<long>("SELECT COUNT(*) FROM v_elements WHERE built_in_category = 'OST_Walls' AND is_type = 1");
            await Assert.That(wallTypes).IsGreaterThanOrEqualTo(1);

            long levels = store.Scalar<long>("SELECT COUNT(*) FROM v_elements WHERE built_in_category = 'OST_Levels'");
            await Assert.That(levels).IsEqualTo(1);

            // Every wall sits on the seeded level and knows its type — the joins the views are for.
            long onLevel = store.Scalar<long>(
                $"SELECT COUNT(*) FROM v_elements WHERE built_in_category = 'OST_Walls' AND is_type = 0 AND level_id = {Level.Id.Value} AND type_element_id IS NOT NULL");
            await Assert.That(onLevel).IsEqualTo(4);

            // A wall's height is a length in the document's display unit (metric template: millimetres).
            long heights = store.Scalar<long>(
                "SELECT COUNT(*) FROM v_parameters WHERE built_in_parameter = 'WALL_USER_HEIGHT_PARAM' AND value_num > 0 AND unit = 'millimeters'");
            await Assert.That(heights).IsEqualTo(4);

            // The walls were drawn from (0,0) to (10,6) feet: their midpoints and boxes are in millimetres too.
            double? maxX = store.Scalar<double?>("SELECT MAX(bbox_max_x) FROM v_elements WHERE built_in_category = 'OST_Walls' AND is_type = 0");
            await Assert.That(maxX ?? 0).IsGreaterThan(3000);
        }
        finally
        {
            foreach (string file in new[] { path, path + "-wal", path + "-shm" })
                if (File.Exists(file)) File.Delete(file);
        }
    }

    [Test]
    public async Task The_version_sweep_covers_every_collected_element()
    {
        IReadOnlyList<ElementId> ids = ElementRowReader.CollectIds(Document);
        List<(long Id, Guid Version)> sweep = ElementRowReader.SweepVersions(Document, ids);

        await Assert.That(sweep.Count).IsEqualTo(ids.Count);
        await Assert.That(sweep.Select(s => s.Id).Distinct().Count()).IsEqualTo(ids.Count);
    }
}
