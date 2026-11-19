namespace AnalyseTool.Extensions;

/// <summary>The error codes of the extension commands, beside the pipeline's own (<c>CommandErrorCodes</c>).</summary>
public static class ExtensionErrorCodes
{
    /// <summary>No extension has this id.</summary>
    public const string ExtensionNotFound = "extension_not_found";

    /// <summary>An extension with this id exists already.</summary>
    public const string ExtensionExists = "extension_exists";

    /// <summary>The extension is an installed package: the extension manager owns it.</summary>
    public const string ExtensionManaged = "extension_managed";

    /// <summary>A path leaves the extension folder, or names a file that does not exist.</summary>
    public const string FileNotFound = "file_not_found";

    /// <summary>Git cannot be used, or the folder has no history yet.</summary>
    public const string HistoryUnavailable = "history_unavailable";

    /// <summary>Another save of the same folder is running.</summary>
    public const string HistoryLocked = "history_locked";

    /// <summary>The sources did not compile.</summary>
    public const string CompileFailed = "compile_failed";

    /// <summary>The input is not valid: an id, a name, a path.</summary>
    public const string Invalid = "invalid";

    /// <summary>No model test has this id.</summary>
    public const string TestNotFound = "test_not_found";

    /// <summary>Revit has no open model to test.</summary>
    public const string NoDocument = "no_document";
}
