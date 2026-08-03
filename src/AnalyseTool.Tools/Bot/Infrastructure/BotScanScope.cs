using Autodesk.Revit.DB;

namespace AnalyseTool.Tools.Bot
{
    /// <summary>
    /// The 3D view every raycast needs, without inheriting the user's view settings.
    ///
    /// <para><see cref="ReferenceIntersector"/> only finds what is VISIBLE in the view it is given, so
    /// reusing whatever 3D view happens to be open would silently under-report: a hidden category, a
    /// view filter or an active section box turns a real obstruction into "clear". A checking tool that
    /// misses things is worse than none, so the scan gets a throwaway view of its own, with every model
    /// category on and no crop.</para>
    ///
    /// <para>Creating a view is a model change, so it runs inside a <see cref="TransactionGroup"/> that is
    /// ALWAYS rolled back on dispose: nothing is left behind and no undo entry is created — which is what
    /// lets these commands still call themselves read-only.</para>
    /// </summary>
    public sealed class BotScanScope : IDisposable
    {
        private readonly TransactionGroup _group;
        private bool _closed;

        private BotScanScope(View3D view, TransactionGroup group)
        {
            View = view;
            _group = group;
        }

        /// <summary>The scratch view to hand to <see cref="ReferenceIntersector"/>. Invalid after dispose.</summary>
        public View3D View { get; }

        public static BotScanScope Create(Document doc)
        {
            if (doc.IsFamilyDocument)
                throw new InvalidOperationException("The bot works on a project document; a family document has no rooms or 3D views to walk.");

            ViewFamilyType? viewType = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(v => v.ViewFamily == ViewFamily.ThreeDimensional);

            if (viewType == null)
                throw new InvalidOperationException("The document has no 3D view type, so no scan view can be created.");

            TransactionGroup group = new(doc, "AnalyseTool bot scan");
            group.Start();
            try
            {
                View3D view;
                using (Transaction transaction = new(doc, "Bot scan view"))
                {
                    transaction.Start();
                    view = View3D.CreateIsometric(doc, viewType.Id);
                    view.DetailLevel = ViewDetailLevel.Fine;
                    transaction.Commit();
                }

                return new BotScanScope(view, group);
            }
            catch
            {
                group.RollBack();
                group.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            if (_closed) return;
            _closed = true;
            _group.RollBack();
            _group.Dispose();
        }
    }
}
