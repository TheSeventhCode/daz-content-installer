using System;
using System.IO;
using DazContentInstaller.Database;
using DazContentInstaller.Services;
using DazContentInstaller.ViewModels;
using DazContentInstaller.Views;
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
        services.AddSingleton<IAppInfoService>(appInfo);

        var appDataPath = AppDataPathResolver.ResolveAppDataPath(AppDomain.CurrentDomain.BaseDirectory, appInfo.IsDevelopmentEnvironment());
        Directory.CreateDirectory(appDataPath);

        var config = new InstallerConfig { AppDataPath = appDataPath };

        services.AddSingleton(config);
        services.Configure<InstallerConfig>(o => o.AppDataPath = appDataPath);

        services.AddDbContextFactory<ApplicationDbContext>(o => o.UseSqlite($"Data Source={config.DbPath}"));
        services.AddSingleton<SettingsService>();
        services.AddSingleton<IDirectoryService>(sp => new DirectoryService(sp.GetRequiredService<SettingsService>()));
        services.AddSingleton<IFileDialogService, FileDialogService>();
        services.AddSingleton<IDazArchiveScanner, DazArchiveScanner>();
        services.AddSingleton<IAssetLibraryStatisticsService, AssetLibraryStatisticsService>();
        services.AddSingleton<IInstallRecordStatisticsService, InstallRecordStatisticsService>();
        services.AddSingleton<IArchiveFileDetailsService, ArchiveFileDetailsService>();
        services.AddSingleton<DestinationPathLockRegistry>();
        services.AddSingleton<IArchiveOverrideService, ArchiveOverrideService>();
        services.AddTransient<IDazArchiveInstaller>(sp =>
            new DazArchiveInstaller(
                sp.GetRequiredService<IDbContextFactory<ApplicationDbContext>>(),
                sp.GetRequiredService<SettingsService>(),
                sp.GetRequiredService<InstallerConfig>(),
                sp.GetRequiredService<IDirectoryService>(),
                sp.GetRequiredService<IDazArchiveScanner>(),
                sp.GetRequiredService<DestinationPathLockRegistry>(),
                sp.GetRequiredService<IArchiveOverrideService>()));

        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<SettingsWindowViewModel>();
        services.AddTransient<SettingsWindow>();
        services.AddTransient<ArchiveOverridesWindowViewModel>();
        services.AddTransient<ArchiveOverridesWindow>();
        
        return services;
    }
    
    public static IServiceProvider GetServiceProvider()
    {
        return _serviceProvider ??= new ServiceCollection().CreateServiceCollection().BuildServiceProvider();
    }
}
