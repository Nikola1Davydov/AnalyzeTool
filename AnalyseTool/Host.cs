using AnalyseTool.DoorManager.View;
using AnalyseTool.ParameterControl.ViewModels;
using AnalyseTool.ParameterControl.Views;
using AnalyseTool.Resources.wpf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.IO;
using System.Reflection;

namespace AnalyseTool
{
    public static class Host
    {
        private static IHost _host;
        /// <summary>
        ///     Starts the host and configures the application's services
        /// </summary>
        public static void Start()
        {
            var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings
            {
                ContentRootPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly()!.Location),
                DisableDefaults = true
            });

            //Logging

            //Services
            builder.Services.AddTransient<AnalyseToolViewModel>();
            builder.Services.AddTransient<SubViewAnalyseTool>();
            builder.Services.AddTransient<DoorManagerView>();

            _host = builder.Build();
            _host.Start();
        }
        /// <summary>
        ///     Stops the host and handle <see cref="IHostedService"/> services
        /// </summary>
        public static void Stop()
        {
            _host.StopAsync().GetAwaiter().GetResult();
        }
        /// <summary>
        ///     Get service of type <typeparamref name="T"/>
        /// </summary>
        /// <typeparam name="T">The type of service object to get</typeparam>
        /// <exception cref="System.InvalidOperationException">There is no service of type <typeparamref name="T"/></exception>
        public static T GetService<T>() where T : class
        {
            return _host.Services.GetRequiredService<T>();
        }
    }
}
