using JetBrains.Annotations;
using Nuke.Common;
using Nuke.Common.ProjectModel;
using System.Collections.Generic;
using System.Configuration;
using System.IO.Enumeration;
using System.Linq;

[PublicAPI]
sealed partial class Build : NukeBuild
{
    /// <summary>
    ///     Pipeline entry point.
    /// </summary>
    public static int Main() => Execute<Build>(build => build.PublishGitHub);

    /// <summary>
    ///     Extract solution configuration names from the solution file.
    /// </summary>
    List<string> GlobBuildConfigurations()
    {
        var configurations = Solution.Configurations
            .Select(pair => pair.Key)
            .Select(config => config.Remove(config.LastIndexOf('|')))
            .Where(config => Configurations.Any(wildcard => FileSystemName.MatchesSimpleExpression(wildcard, config)))
            .ToList();

        Assert.NotEmpty(configurations, $"No solution configurations have been found. Pattern: {string.Join(" | ", Configurations)}");
        return configurations;
    }
}