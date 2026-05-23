using AnalyseTool.Common.Helper.Updater;
using AnalyseTool.Sdk;

namespace AnalyseTool.Features
{
    internal sealed class CheckUpdate : IRevitTask
    {
        public async Task<object?> ExecuteAsync(IRevitContext ctx, CancellationToken ct)
        {
            return await GitHubUpdateChecker.CheckForUpdateAsync(owner: "Nikola1Davydov", repo: "AnalyzeTool");
        }
    }
}
