namespace TaskbarMonitor.Core;

internal sealed class SingleInstance : IDisposable
{
    private const string Id = "TaskbarMonitor-4B8E61D2";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showSignal;
    private RegisteredWaitHandle? _wait;

    public SingleInstance()
    {
        _mutex = new Mutex(initiallyOwned: true, $@"Local\{Id}.Mutex", out bool createdNew);
        IsFirst = createdNew;
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{Id}.Show");
    }

    public bool IsFirst { get; }

    public void SignalFirstInstance() => _showSignal.Set();

    public void ListenForSignals(Action onSignal) =>
        _wait = ThreadPool.RegisterWaitForSingleObject(_showSignal, (_, _) => onSignal(), null, Timeout.Infinite, executeOnlyOnce: false);

    public void Dispose()
    {
        _wait?.Unregister(null);
        if (IsFirst)
            _mutex.ReleaseMutex();
        _mutex.Dispose();
        _showSignal.Dispose();
    }
}
