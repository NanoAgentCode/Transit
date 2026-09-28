namespace RepoTransit;

public sealed class SingleInstanceGate : IDisposable
{
    private Mutex? _mutex;
    private readonly EventWaitHandle _activation;

    private SingleInstanceGate(Mutex mutex, string name)
    {
        _mutex = mutex;
        _activation = new EventWaitHandle(false, EventResetMode.AutoReset, name + ".Activate");
    }

    public static SingleInstanceGate? TryAcquire(string name)
    {
        var mutex = new Mutex(true, name, out var createdNew);
        if (createdNew)
        {
            try { return new SingleInstanceGate(mutex, name); }
            catch { mutex.ReleaseMutex(); mutex.Dispose(); throw; }
        }

        mutex.Dispose();
        return null;
    }

    public static bool SignalExisting(string name)
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var activation = EventWaitHandle.OpenExisting(name + ".Activate");
            return activation.Set();
        }
        catch (WaitHandleCannotBeOpenedException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public IDisposable OnActivation(Action action)
    {
        var registration = ThreadPool.RegisterWaitForSingleObject(_activation, (_, _) => action(), null, -1, false);
        return new ActivationSubscription(registration);
    }

    public void Dispose()
    {
        var mutex = Interlocked.Exchange(ref _mutex, null);
        if (mutex is null)
            return;

        _activation.Dispose();
        mutex.ReleaseMutex();
        mutex.Dispose();
    }

    private sealed class ActivationSubscription(RegisteredWaitHandle registration) : IDisposable
    {
        public void Dispose() => registration.Unregister(null);
    }
}
