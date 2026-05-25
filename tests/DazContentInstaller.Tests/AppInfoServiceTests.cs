using DazContentInstaller.Services;
using Shouldly;

namespace DazContentInstaller.Tests;

public class AppInfoServiceTests
{
    [Fact]
    public void GetAppVersion_ReturnsInjectedVersion()
    {
        var service = new AppInfoService(() => "1.2.3");

        service.GetAppVersion().ShouldBe("1.2.3");
    }

    [Fact]
    public void IsDevelopmentEnvironment_ReturnsInjectedValue()
    {
        var service = new AppInfoService(isDevelopmentEnvironment: () => false);

        service.IsDevelopmentEnvironment().ShouldBeFalse();
    }
}