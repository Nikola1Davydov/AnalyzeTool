using AnalyseTool.ParameterControl.Models;
using AnalyseTool.ParameterControl.ViewModels;
using AnalyseTool.ParameterControl.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.IO;
using System.Reflection;

namespace AnalyseTool
{
    public static class HostBuilderHelper
    {
        private static IHost _host;
        public static void StartHost()
        {
            HostApplicationBuilder builder = new HostApplicationBuilder(new HostApplicationBuilderSettings
            {
                ContentRootPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                DisableDefaults = true,
            });

            builder.Services.AddTransient<IAnalyseToolModel, AnalyseToolModel>();
            builder.Services.AddTransient<AnalyseToolViewModel>();
            builder.Services.AddTransient<AnalyseToolView>();

            _host = builder.Build();
            _host.Start();
        }

        public static void StopHost()
        {
            _host.StopAsync().GetAwaiter().GetResult();
            _host.Dispose();
        }
        public static T GetService<T>() where T : class
        {
            return _host.Services.GetRequiredService<T>();
        }
    }
}
