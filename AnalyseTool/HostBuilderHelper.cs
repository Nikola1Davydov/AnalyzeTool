using AnalyseTool.RevitCommands.DataAccess;
using AnalyseTool.RevitCommands.ParameterControl.MVVM.MainTab;
using Microsoft.Extensions.DependencyInjection;

namespace AnalyseTool
{
    public static class HostBuilderHelper
    {
        private static IServiceProvider _serviceProvider;
        public static void StartHost()
        {
            ServiceCollection services = new ServiceCollection();

            services.AddSingleton<IDataElementRepository, DataElementRepository>();
            services.AddTransient<MainView>();

            _serviceProvider = services.BuildServiceProvider();
        }
        public static T GetService<T>() where T : class
        {
            return _serviceProvider.GetRequiredService<T>();
        }
    }
}
