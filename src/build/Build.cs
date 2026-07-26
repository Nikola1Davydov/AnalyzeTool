using JetBrains.Annotations;
using Nuke.Common;
using System.Collections.Generic;
using System.IO.Enumeration;
using System.Linq;

[PublicAPI]
sealed partial class Build : NukeBuild
{
    /// <summary>
    ///     Pipeline entry point. The default target is deliberately the read-only guardrail run:
    ///     bare <c>build.cmd</c> must not do anything outward-facing. It used to default to
    ///     <c>PublishGitHub</c>, so a run with no arguments went straight at creating a GitHub
    ///     release — and <c>CleanFailedRelease</c> deletes the tag from origin when that fails.
    ///     Both workflows name their target explicitly (ci.yml -> Ci, Publish Release.yml ->
    ///     PublishGitHub), so nothing relies on the default; publishing is now an explicit ask.
    /// </summary>
    public static int Main() => Execute<Build>(build => build.Ci);

    /// <summary>
    ///     Extract solution configuration names from the solution file.
    /// </summary>
    List<string> GlobBuildConfigurations()
    {
        List<string> configurations = Solution.Configurations
            .Select(pair => pair.Key)
            .Select(config => config.Remove(config.LastIndexOf('|')))
            .Where(config => Configurations.Any(wildcard => FileSystemName.MatchesSimpleExpression(wildcard, config)))
            // The solution declares every configuration for three platforms (Any CPU, x64, x86), so
            // stripping the platform yields "Release R25" three times. Without this the callers build
            // each configuration three times over — and BuildClientApp runs `npm run build` nine.
            .Distinct()
            .ToList();

        Assert.NotEmpty(configurations, $"No solution configurations have been found. Pattern: {string.Join(" | ", Configurations)}");
        return configurations;
    }
}