using System;
using System.Threading;

namespace BluetoothBatteryMonitor.App.Helpers;

/// <summary>
/// Named-mutex single-instance gate.
/// <para>
/// A one-shot <see cref="Mutex"/> probe loses the race right after an in-app update: the
/// installer relaunches this process while the previous instance is still shutting down, so
/// the new process finds the name taken, treats itself as secondary and exits — the user ends
/// up with no tray icon at all. Retrying for a few seconds fixes that without changing what
/// happens when a genuinely running instance is already there.
/// </para>
/// </summary>
public static class SingleInstanceLock
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);
    public const int DefaultPollMilliseconds = 200;

    /// <summary>
    /// Becomes the primary instance, or returns <c>null</c> when another process still owns the
    /// name after <paramref name="timeout"/>. The returned mutex must stay alive (and undisposed)
    /// for the lifetime of the process.
    /// </summary>
    public static Mutex? Acquire(string name, TimeSpan? timeout = null, int pollMilliseconds = DefaultPollMilliseconds)
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);

        while (true)
        {
            Mutex? mutex = null;
            try
            {
                mutex = new Mutex(false, name);
                // WaitOne(0) covers the "name exists but nobody owns it" case that an
                // initially-owned constructor would misreport as an existing instance.
                if (mutex.WaitOne(0))
                {
                    return mutex;
                }
            }
            catch (AbandonedMutexException)
            {
                // The previous owner died while holding the mutex; we own it now.
                return mutex;
            }
            catch
            {
                // Unusable name (restricted session, invalid characters): behave like the old
                // code path and let the process start instead of refusing to run.
                mutex?.Dispose();
                return new Mutex(true);
            }

            mutex?.Dispose();

            if (DateTime.UtcNow >= deadline)
            {
                return null;
            }

            Thread.Sleep(pollMilliseconds);
        }
    }
}
