namespace RepoTransit;

public sealed class SingleInstanceGate : IDisposable
{
    private Mutex? _mutex;

    private SingleInstanceGate(Mutex mutex) => _mutex = mutex;

    public static SingleInstanceGate? TryAcquire(string name)
    {
        var mutex = new Mutex(true, name, out var createdNew);
        if (createdNew)
            return new SingleInstanceGate(mutex);

        mutex.Dispose();
        return null;
    }

    public void Dispose()
    {
        var mutex = Interlocked.Exchange(ref _mutex, null);
        if (mutex is null)
            return;

        mutex.ReleaseMutex();
        mutex.Dispose();
    }
}
