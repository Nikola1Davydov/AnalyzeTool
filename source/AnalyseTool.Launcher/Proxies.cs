namespace AnalyseTool.Launcher.Proxies;

// No proxies: every ribbon button of AnalyseTool — its own and the extensions' — is declared in a plugin.json and built with
// AdWindows by the add-in (Infrastructure/Ribbon/AppRibbon.cs), so Revit never creates a command class of ours.
//
// A button that must be a Revit IExternalCommand (a keyboard shortcut of its own, for example) would need one line here,
// named like the add-in class it forwards to:
//
// [Transaction(TransactionMode.Manual)] public sealed class MyCommand : ProxyCommand;
