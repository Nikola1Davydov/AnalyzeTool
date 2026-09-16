using AnalyseTool.Core.Common;
using AnalyseTool.Core.Common.Bootstrap;
using AnalyseTool.Core.Common.Extensions;
using AnalyseTool.Core.Common.Extensions.Scripting;
using AnalyseTool.Core.Common.Policy;
using AnalyseTool.Sdk;

namespace AnalyseTool.Core.Features.Policy
{
    /// <summary>
    /// What the organization policy does to this seat, for the Settings "Organization" panel and the
    /// CLI: whether a file was found, what it could not read, which settings it locks and what each
    /// managed setting resolves to and from where. Read-only; nothing here touches the Revit model.
    /// </summary>
    [RevitCommand(
        Description = "Reports the organization policy applied to this installation: whether a policy " +
                      "file exists, its problems, the locked settings and the effective value of each " +
                      "managed setting with its origin.",
        ReadOnly = true,
        HiddenFromMcp = true)] // plugin self-management, not for the AI
    internal sealed class GetPolicyStatus : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            PolicyState policy = PolicyStore.Current;
            PolicyDocument doc = policy.Document;

            return Task.FromResult<object?>(new
            {
                present = policy.IsPresent,
                origin = policy.Origin.ToString().ToLowerInvariant(), // absent | loaded | invalid
                path = policy.Path,
                problems = policy.Problems,
                organization = doc.Organization is null ? null : new
                {
                    name = doc.Organization.Name,
                    contact = doc.Organization.Contact,
                },
                minimumVersion = doc.MinimumVersion,
                pluginVersion = SharedData.ToolData.PLUGIN_VERSION,
                // Pointer form: recognized, followed from phase 3a on.
                policyUrl = doc.PolicyUrl,
                locked = doc.Locked,
                settings = new
                {
                    codeExecution = new
                    {
                        enabled = CodeExecutionSettings.Enabled,
                        origin = CodeExecutionSettings.Origin,
                        locked = CodeExecutionSettings.IsManaged,
                    },
                    extensionRoots = new
                    {
                        fromPolicy = ExtensionSources.PolicyRoots(),
                        locked = ExtensionSources.RootsLocked,
                    },
                    allowedFeeds = doc.Extensions?.AllowedFeeds,
                    allowInstallFromRepository = PolicyFeedRules.InstallFromRepositoryAllowed,
                    mcpEnabled = new
                    {
                        value = doc.Mcp?.Enabled,
                        locked = policy.IsLocked(PolicySettings.McpEnabled),
                    },
                },
                machineExtensionsRoot = PathProvider.MachineExtensionsDistRoot,
            });
        }
    }

    /// <summary>Re-reads the policy file. For a Settings "Reload" button and for the CLI; an edited
    /// file otherwise applies on the next Revit start.</summary>
    [RevitCommand(
        Description = "Re-reads the organization policy file and reloads extensions so new roots apply.",
        HiddenFromMcp = true)]
    internal sealed class ReloadPolicy : IRevitTask
    {
        public Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            PolicyStore.Reload();
            CoreServices.ReloadExtensions();
            PolicyState policy = PolicyStore.Current;
            return Task.FromResult<object?>(new
            {
                present = policy.IsPresent,
                origin = policy.Origin.ToString().ToLowerInvariant(),
                problems = policy.Problems,
            });
        }
    }
}
