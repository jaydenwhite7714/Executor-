using System.Windows.Threading;

namespace DrivenTestEngine.App.Infrastructure;

/// <summary>
/// Lets engine callbacks (raised on worker threads) update view models safely.
/// </summary>
public static class UiDispatcher
{
    private static Dispatcher? _dispatcher;

    public static bool IsInitialized => _dispatcher is not null;

    public static void Initialize(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public static void Post(Action action)
    {
        var dispatcher = _dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.BeginInvoke(action, DispatcherPriority.Background);
    }
}
