using Autodesk.Revit.Attributes;

namespace AnalyseTool.Launcher.Proxies;

// One line per ribbon button and per availability class.
// The proxy name must match the add-in class name; the add-in checks this at startup.

[Transaction(TransactionMode.Manual)] public sealed class AnalyseToolCommand : ProxyCommand;
[Transaction(TransactionMode.Manual)] public sealed class DockCommand : ProxyCommand;
[Transaction(TransactionMode.Manual)] public sealed class NewButtonCommand : ProxyCommand;
[Transaction(TransactionMode.Manual)] public sealed class ExtensionsCommand : ProxyCommand;
[Transaction(TransactionMode.Manual)] public sealed class SettingsCommand : ProxyCommand;
[Transaction(TransactionMode.Manual)] public sealed class AboutCommand : ProxyCommand;
[Transaction(TransactionMode.Manual)] public sealed class ReportBugCommand : ProxyCommand;
[Transaction(TransactionMode.Manual)] public sealed class StatusCommand : ProxyCommand;
[Transaction(TransactionMode.Manual)] public sealed class ActiveDocumentCommand : ProxyCommand;
[Transaction(TransactionMode.Manual)] public sealed class UserSettingsCommand : ProxyCommand;
[Transaction(TransactionMode.Manual)] public sealed class DevPageCommand : ProxyCommand;

public sealed class DocumentAvailability : ProxyAvailability;
public sealed class ProjectAvailability : ProxyAvailability;
