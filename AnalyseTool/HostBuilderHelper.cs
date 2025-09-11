using AnalyseTool.ParameterControl.Models;
using AnalyseTool.ParameterControl.ViewModels;
using AnalyseTool.ParameterControl.Views;
using Microsoft.Extensions.DependencyInjection;

namespace AnalyseTool
{
    public static class HostBuilderHelper
    {
        private static IServiceProvider _serviceProvider;
        public static void StartHost()
        {
            ServiceCollection services = new ServiceCollection();

            services.AddTransient<IAnalyseToolModel, AnalyseToolModel>();
            services.AddTransient<AnalyseToolViewModel>();
            services.AddTransient<AnalyseToolView>();

            _serviceProvider = services.BuildServiceProvider();
        }

        public static T GetService<T>() where T : class
        {
            return _serviceProvider.GetRequiredService<T>();
        }
    }
}
