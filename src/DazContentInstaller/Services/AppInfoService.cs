using System;

namespace DazContentInstaller.Services;

public interface IAppInfoService
{
    string? GetAppVersion();
    bool IsDevelopmentEnvironment();
}

public class AppInfoService(
    Func<string?>? getAppVersion = null,
    Func<bool>? isDevelopmentEnvironment = null) : IAppInfoService
{
    public string? GetAppVersion() =>
        getAppVersion?.Invoke() ??
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString();

    public bool IsDevelopmentEnvironment() =>
        isDevelopmentEnvironment?.Invoke() ?? IsDevelopmentEnvironmentDefault();

    private static bool IsDevelopmentEnvironmentDefault()
    {
#if DEBUG
        return true;
#else
        return false;
#endif
    }
}