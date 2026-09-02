using AnalyseTool.Tools.Shared;
using Autodesk.Revit.DB;
using ParameterUtils = Autodesk.Revit.DB.ParameterUtils;

namespace AnalyseTool.Tools.Actions
{
    /// <summary>
    /// The core of SetDataToParameters as a function of a <see cref="Document"/>: one transaction over
    /// the given items, per-item failures reported rather than thrown.
    ///
    /// Pulled out of the command so it can be tested inside Revit without a <c>UIApplication</c> — the
    /// in-Revit test host has no UI session, and a command's <c>RunInRevitAsync(app =&gt; …)</c> needs
    /// one. The command is now the thin shell: payload in, this service, result out.
    /// </summary>
    public sealed class ParameterWriteService
    {
        /// <summary>One write: which element, which parameter (BuiltInParameter value or ParameterElement
        /// id), what value.</summary>
        public sealed record Item(long ElementId, long ParameterId, string Value);

        public enum Mode
        {
            Overwrite,
            OnlyIfEmpty,
            SkipIfEqual,
        }

        public SetDataResult Write(Document doc, IReadOnlyList<Item?> items, Mode mode, string transactionName = "Set data to parameters")
        {
            using Transaction transaction = new Transaction(doc, transactionName);
            transaction.Start();
            // Without this a Revit warning raises a MODAL dialog on the Revit thread and the whole
            // platform waits for a click that, in a batch, nobody is there to give.
            CollectingFailuresPreprocessor failures = CollectingFailuresPreprocessor.Apply(transaction);

            int written = 0, skipped = 0;
            List<WriteProblem> problems = new();
            foreach (Item? item in items)
            {
                if (item == null) { skipped++; continue; }
                try
                {
                    if (WriteOne(doc, item, mode)) written++;
                    else skipped++;
                }
                catch (Exception ex)
                {
                    // Per item, not per batch: a value that cannot be converted for ONE parameter (a
                    // string into an ElementId, say) used to throw out of the loop, and the transaction
                    // never committed — 499 good writes gone for one bad one, with nothing saying which.
                    skipped++;
                    problems.Add(new WriteProblem(item.ElementId, item.ParameterId, ex.Message));
                }
            }

            transaction.Commit();

            // Counted and reported rather than silently dropped: an unattended caller has no other way
            // to learn that 40 of its 500 writes never landed.
            return new SetDataResult(true, written, skipped, null, failures.Warnings,
                problems.Count == 0 ? null : problems);
        }

        /// <summary>True when the value was actually written; false when the element or parameter was
        /// not found, the parameter is read-only, or the mode filtered the write out.</summary>
        private static bool WriteOne(Document doc, Item item, Mode mode)
        {
            Element? element = doc.GetElement(new ElementId(item.ElementId));
            if (element == null) return false;

            Parameter? parameter = Resolve(doc, element, item.ParameterId);
            if (parameter == null || parameter.IsReadOnly) return false;

            switch (mode)
            {
                case Mode.OnlyIfEmpty when parameter.HasValue:
                    return false;
                case Mode.SkipIfEqual when parameter.GetParameterValue() == item.Value:
                    return false;
            }
            parameter.SetParameterValue(item.Value);
            return true;
        }

        /// <summary>A BuiltInParameter value (negative) is looked up directly; anything else is an
        /// ElementId — first among the element's own parameters, then as a ParameterElement definition.</summary>
        private static Parameter? Resolve(Document doc, Element element, long parameterId)
        {
            ElementId id = new(parameterId);
            if (ParameterUtils.IsBuiltInParameter(id))
                return element.get_Parameter((BuiltInParameter)parameterId);

            foreach (Parameter p in element.Parameters)
                if (p?.Id != null && p.Id.Value == parameterId) return p;

            Definition? definition = (doc.GetElement(id) as ParameterElement)?.GetDefinition();
            return definition != null ? element.get_Parameter(definition) : null;
        }
    }
}
