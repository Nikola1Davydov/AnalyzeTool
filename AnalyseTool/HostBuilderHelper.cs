using AnalyseTool.RevitCommands.ParameterControl.DataAccess;
using AnalyseTool.RevitCommands.ParameterControl.MVVM.MainTab;
using AnalyseTool.RevitCommands.ParameterControl.MVVM.ParameterAnalyseTab;
using Microsoft.Extensions.DependencyInjection;

namespace AnalyseTool
{
    public static class HostBuilderHelper
    {
        private static IServiceProvider _serviceProvider;
        public static void StartHost()
        {
            ServiceCollection services = new ServiceCollection();

            services.AddTransient<IDataElementRepository, DataElementRepository>();
            services.AddTransient<DataElementManagment>();

            services.AddTransient<MainViewModel>();
            services.AddTransient<MainView>();
            services.AddTransient<ParameterAnalyseViewModel>();


            _serviceProvider = services.BuildServiceProvider();

        }
        public static T GetService<T>() where T : class
        {
            return _serviceProvider.GetRequiredService<T>();
        }
    }
}
