using System;
using System.IO;
using DazContentInstaller.Database;
using DazContentInstaller.Services;
using DazContentInstaller.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DazContentInstaller;

public static class ServiceCollectionExtensions
{
    private static IServiceProvider? _serviceProvider;

    private static IServiceCollection CreateServiceCollection(this IServiceCollection services)
    {
        var appInfo = new AppInfoService();
        services.AddSingleton(appInfo);
        
        var appDataPath = AppDataPathResolver.ResolveAppDataPath(AppDomain.CurrentDomain.BaseDirectory, appInfo.IsDevelopmentEnvironment());
        Directory.CreateDirectory(appDataPath);

        var config = new InstallerConfig { AppDataPath = appDataPath };

        services.AddSingleton<IAppInfoService, AppInfoService>();

        services.Configure<InstallerConfig>(o => o.AppDataPath = appDataPath);

        services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite($"Data Source={config.DbPath}"),
            ServiceLifetime.Singleton);
        services.AddSingleton<SettingsService>();

        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<SettingsWindowViewModel>();
        
        return services;
    }
    
    public static IServiceProvider GetServiceProvider()
    {
        return _serviceProvider ??= new ServiceCollection().CreateServiceCollection().BuildServiceProvider();
    }
}