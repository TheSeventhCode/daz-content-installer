namespace DazContentInstaller.Options;

public interface IAppInfoService
{
    string? GetAppVersion();
    bool IsDevelopmentEnvironment();
}

public class AppInfoService : IAppInfoService
{
    public string? GetAppVersion()
    {
        return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString();
    }

    public bool IsDevelopmentEnvironment()
    {
#if DEBUG
        return true;
#else
        return false;
#endif
    }
}