using System;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace DazContentInstaller;

internal static class UiThread
{
    public static Task RunAsync(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return Dispatcher.UIThread.InvokeAsync(action).GetTask();
    }

    public static Task RunAsync(Func<Task> action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            return action();

        return Dispatcher.UIThread.InvokeAsync(action);
    }
}
