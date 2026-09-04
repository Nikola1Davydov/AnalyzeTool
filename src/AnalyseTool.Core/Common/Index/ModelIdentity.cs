using Autodesk.Revit.DB;
using System.IO;

namespace AnalyseTool.Core.Common.Index
{
    /// <summary>
    /// Which model an index belongs to. The key is the MODEL, never the file path: for a workshared
    /// model the central's GUID, so every team member's local copy maps to the same index; otherwise
    /// <c>Document.CreationGUID</c>, which survives Save As (measured 2026-09-02, DocumentIdentityTests) —
    /// a copy therefore starts from its original's index and the first reconcile brings it in line.
    /// </summary>
    internal static class ModelIdentity
    {
        /// <summary>Revit-thread only (reads the document).</summary>
        public static string KeyOf(Document doc)
        {
            Guid guid = doc.CreationGUID;
            if (doc.IsWorkshared)
            {
                try { guid = doc.WorksharingCentralGUID; }
                catch (Autodesk.Revit.Exceptions.ApplicationException) { /* detached or no central: fall back to CreationGUID */ }
            }
            return guid.ToString("N");
        }

        /// <summary>Documents the index does not cover: families (their own world, not a model) and
        /// links (read through the host model, if ever).</summary>
        public static bool IsIndexable(Document doc) => !doc.IsFamilyDocument && !doc.IsLinked;

        /// <summary>%LOCALAPPDATA%\&lt;plugin&gt;\models\&lt;key&gt;\index.db</summary>
        public static string IndexPath(string key) => Path.Combine(SqliteRuntime.ModelsRoot, key, "index.db");
    }
}
