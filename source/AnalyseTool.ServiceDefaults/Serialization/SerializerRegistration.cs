using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AnalyseTool.ServiceDefaults.Serialization;

/// <summary>
///     Provides extension methods for <see cref="IHostApplicationBuilder" /> to configure JSON serialization.
/// </summary>
[PublicAPI]
public static class SerializerRegistration
{
    /// <typeparam name="TBuilder">The host application builder type.</typeparam>
    /// <param name="builder">The host application builder.</param>
    extension<TBuilder>(TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        /// <summary>
        ///     Configures the JSON serializer options of the application.
        /// </summary>
        /// <returns>The <typeparamref name="TBuilder" /> for chaining.</returns>
        /// <remarks>
        ///     <para>The options reach a consumer through <see cref="Microsoft.Extensions.Options.IOptions{TOptions}" />.</para>
        ///     <para>
        ///         Types listed in <see cref="ApplicationSerializerContext" /> use source-generated metadata;
        ///         every other type falls back to reflection, so command payloads need no manual registration.
        ///     </para>
        /// </remarks>
        public TBuilder ConfigureJsonSerializer()
        {
            builder.Services.Configure<JsonSerializerOptions>(options =>
            {
                options.WriteIndented = builder.Environment.IsDevelopment();
                options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                options.PropertyNameCaseInsensitive = true;
                // The same in Debug and Release: a page that sees null during development must not get undefined in a release
                options.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
                options.Converters.Add(new JsonStringEnumConverter());
                options.TypeInfoResolverChain.Add(ApplicationSerializerContext.Default);
                options.TypeInfoResolverChain.Add(new DefaultJsonTypeInfoResolver());
            });

            return builder;
        }
    }
}
