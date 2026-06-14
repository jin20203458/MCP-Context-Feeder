using Microsoft.Extensions.DependencyInjection;
using System;
using System.Windows;
using Combined_Source_WPF.Services;
using Combined_Source_WPF.ViewModels;
using Combined_Source_WPF.Views;

namespace Combined_Source_WPF
{
    public partial class App : Application
    {
        public new static App Current => (App)Application.Current;
        public IServiceProvider Services { get; }

        public App()
        {
            Services = ConfigureServices();
        }

        private static IServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();

            // Services
            services.AddSingleton<IPresetService, PresetService>();
            services.AddSingleton<IFileInspector, FileInspector>();

            // ViewModels
            services.AddSingleton<MainViewModel>();

            // Views
            services.AddTransient<MainWindow>();

            return services.BuildServiceProvider();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var mainWindow = Services.GetRequiredService<MainWindow>();
            var viewModel = Services.GetRequiredService<MainViewModel>();
            
            mainWindow.DataContext = viewModel;
            mainWindow.Show();

            // ViewModel 초기화 로직 동작
            viewModel.Initialize();
        }
    }
}
