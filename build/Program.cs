using Build.Modules;
using Build.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModularPipelines;
using ModularPipelines.Extensions;

var builder = Pipeline.CreateBuilder();

builder.Configuration.AddJsonFile("appsettings.json");
builder.Configuration.AddUserSecrets<Program>();
builder.Configuration.AddEnvironmentVariables();

builder.Services.AddOptions<BuildOptions>().Bind(builder.Configuration.GetSection("Build")).ValidateDataAnnotations();
builder.Services.AddOptions<ProductOptions>().Bind(builder.Configuration.GetSection("Product")).ValidateDataAnnotations();
builder.Services.AddOptions<VendorOptions>().Bind(builder.Configuration.GetSection("Vendor")).ValidateDataAnnotations();

builder.Services.AddModule<CheckBoundariesModule>();
builder.Services.AddModule<CompileClientAppModule>();
builder.Services.AddModule<CompileProjectModule>();

if (args.Contains("pack"))
{
    builder.Services.AddModule<CleanProjectModule>();
    builder.Services.AddModule<CreateBundleModule>();
    builder.Services.AddModule<CreateInstallerModule>();
}

AddRepositoryModules(builder.Services, args);

await builder.Build().RunAsync();

internal partial class Program
{
    /// <summary>
    ///     Adds the modules of this repository only — our tests — implemented in build/RepositoryModules.cs.
    /// </summary>
    /// <remarks>
    ///     Projects made from the template get neither that file nor the tests: a partial method without
    ///     an implementation compiles away together with its call.
    /// </remarks>
    static partial void AddRepositoryModules(IServiceCollection services, string[] args);
}
