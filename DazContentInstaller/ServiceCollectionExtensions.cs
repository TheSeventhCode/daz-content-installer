using System;
using System.IO;
using DazContentInstaller.Database;
using DazContentInstaller.Options;
using DazContentInstaller.Services;
using DazContentInstaller.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DazContentInstaller;

public static class ServiceCollectionExtensions
{
    private static IServiceProvider? _serviceProvider;
    public static IServiceCollection CreateServiceCollection()
    {
        var services = new ServiceCollection();
        var appInfo = new AppInfoService();
        services.AddSingleton(appInfo);

        var appDataPath = appInfo.IsDevelopmentEnvironment()
            ? AppDomain.CurrentDomain.BaseDirectory
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DazContentInstaller");

        var config = new InstallerConfig { AppDataPath = appDataPath };

        services.Configure<InstallerConfig>(o => o.AppDataPath = appDataPath);

        services.AddDbContextFactory<ApplicationDbContext>(o =>
            o.UseSqlite($"Data Source={config.DbPath}"));

        services.AddSingleton<SettingsService>();
        services.AddSingleton<IAppInfoService, AppInfoService>();
        
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<SettingsWindowViewModel>();

        return services;
    }

    public static IServiceProvider GetServiceProvider()
    {
        return _serviceProvider ??= CreateServiceCollection().BuildServiceProvider();
    }
}