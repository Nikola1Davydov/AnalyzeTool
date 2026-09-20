using System;
using System.IO;
using WixSharp;
using Microsoft.Deployment.WindowsInstaller;

namespace Installer;

/// <summary>
/// The installer property that joins a seat at install time (design §7, "installer property"):
/// <c>msiexec /i AnalyseTool-…-SingleUser.msi POLICYURL=https://… [POLICYKEY=base64] /qn</c>.
/// A per-user install writes the membership record the plugin reads (<c>%LOCALAPPDATA%\AnalyseTool\org.json</c>);
/// a per-machine install (elevated, LOCALAPPDATA is SYSTEM's) writes the machine pointer
/// (<c>%ProgramData%\AnalyseTool\policy.json</c>, not enforced — add "enforced" by GPO if leaving must be impossible).
/// Without POLICYURL the action does nothing.
/// </summary>
public static class PolicyActions
{
    [CustomAction]
    public static ActionResult WriteMembership(Session session)
    {
        try
        {
            string url = session.Property("POLICYURL");
            if (string.IsNullOrWhiteSpace(url)) return ActionResult.Success;
            string key = session.Property("POLICYKEY");
            bool perMachine = session.Property("ALLUSERS") == "1";
            string keyJson = string.IsNullOrWhiteSpace(key) ? "null" : Quote(key.Trim());
            string urlJson = Quote(url.Trim());

            string folder = Path.Combine(
                Environment.GetFolderPath(perMachine ? Environment.SpecialFolder.CommonApplicationData : Environment.SpecialFolder.LocalApplicationData),
                SharedData.ToolData.PLUGIN_NAME);
            Directory.CreateDirectory(folder);

            if (perMachine)
            {
                string pointer = Path.Combine(folder, "policy.json");
                if (!System.IO.File.Exists(pointer)) // never overwrite what GPO put there
                    System.IO.File.WriteAllText(pointer,
                        "{ \"version\": 1, \"policyUrl\": " + urlJson + ", \"enforced\": false, \"signingKey\": " + keyJson + " }");
                session.Log("AnalyseTool: wrote machine pointer " + pointer);
            }
            else
            {
                string membership = Path.Combine(folder, "org.json");
                System.IO.File.WriteAllText(membership,
                    "{ \"policyUrl\": " + urlJson + ", \"enforced\": false, \"signingKey\": " + keyJson + " }");
                session.Log("AnalyseTool: wrote membership " + membership);
            }
            return ActionResult.Success;
        }
        catch (Exception ex)
        {
            session.Log("AnalyseTool: POLICYURL could not be applied: " + ex.Message);
            return ActionResult.Success; // the plugin still installs; the user can join from Settings
        }
    }

    /// <summary>A JSON string literal (quotes, backslashes and control characters escaped).</summary>
    private static string Quote(string value)
    {
        var sb = new System.Text.StringBuilder("\"");
        foreach (char c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4")); else sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }
}
