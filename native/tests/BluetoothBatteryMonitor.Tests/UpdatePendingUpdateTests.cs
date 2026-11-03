extern alias MonitorApp;
using System;
using System.IO;
using System.Threading;
using Xunit;
using PendingUpdate = MonitorApp::BluetoothBatteryMonitor.App.Services.Update.PendingUpdate;
using PendingUpdateState = MonitorApp::BluetoothBatteryMonitor.App.Services.Update.PendingUpdateState;
using UpdatePendingUpdate = MonitorApp::BluetoothBatteryMonitor.App.Services.Update.UpdatePendingUpdate;
using SingleInstanceLock = MonitorApp::BluetoothBatteryMonitor.App.Helpers.SingleInstanceLock;

public class UpdatePendingUpdateTests
{
    private static PendingUpdate Marker(string to, string from = "0.3.8") =>
        new() { FromVersion = from, ToVersion = to };

    [Fact]
    public void NoMarkerMeansNothingToReport()
    {
        Assert.Equal(PendingUpdateState.None, UpdatePendingUpdate.Evaluate("0.3.8", null));
    }

    [Fact]
    public void RunningTheTargetVersionCountsAsCompleted()
    {
        Assert.Equal(PendingUpdateState.Completed,
            UpdatePendingUpdate.Evaluate("0.3.9", Marker("0.3.9")));
    }

    [Fact]
    public void ANewerVersionThanTheTargetAlsoCountsAsCompleted()
    {
        // The user may have updated twice before opening the app again.
        Assert.Equal(PendingUpdateState.Completed,
            UpdatePendingUpdate.Evaluate("0.4.0", Marker("0.3.9")));
    }

    [Fact]
    public void StillBeingOnThePreviousVersionCountsAsFailed()
    {
        Assert.Equal(PendingUpdateState.Failed,
            UpdatePendingUpdate.Evaluate("0.3.8", Marker("0.3.9")));
    }

    [Fact]
    public void AMarkerWithoutATargetIsIgnored()
    {
        Assert.Equal(PendingUpdateState.None,
            UpdatePendingUpdate.Evaluate("0.3.8", new PendingUpdate { FromVersion = "0.3.8" }));
    }

    [Fact]
    public void TheMarkerRoundTripsThroughJson()
    {
        // Serialization shape is what survives a silent install restart, so pin it down.
        string path = Path.Combine(Path.GetTempPath(), "pb-pending-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var original = new PendingUpdate
            {
                FromVersion = "0.3.8",
                ToVersion = "0.3.9",
                InstallerPath = @"C:\x\setup.exe",
                StartedAt = "2026-11-03T16:40:00",
                Notified = true
            };

            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(original));
            var restored = System.Text.Json.JsonSerializer.Deserialize<PendingUpdate>(File.ReadAllText(path));

            Assert.NotNull(restored);
            Assert.Equal(original.FromVersion, restored!.FromVersion);
            Assert.Equal(original.ToVersion, restored.ToVersion);
            Assert.Equal(original.InstallerPath, restored.InstallerPath);
            Assert.True(restored.Notified);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}

public class SingleInstanceLockTests
{
    private static string NewName() => @"Local\PBTest_" + Guid.NewGuid().ToString("N");

    [Fact]
    public void ReturnsNullWhileAnotherThreadStillHoldsTheName()
    {
        string name = NewName();
        using var ready = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();

        var holder = new Thread(() =>
        {
            using var mutex = new Mutex(true, name, out _);
            ready.Set();
            release.Wait(TimeSpan.FromSeconds(5));
            mutex.ReleaseMutex();
        }) { IsBackground = true };
        holder.Start();
        Assert.True(ready.Wait(TimeSpan.FromSeconds(5)));

        try
        {
            Assert.Null(SingleInstanceLock.Acquire(name, TimeSpan.FromMilliseconds(300), 50));
        }
        finally
        {
            release.Set();
            holder.Join(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public void WaitsForThePreviousInstanceToFinishShuttingDown()
    {
        // This is the post-update case: the old process is closing, the relaunched one must win.
        string name = NewName();
        using var ready = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();

        var holder = new Thread(() =>
        {
            using var mutex = new Mutex(true, name, out _);
            ready.Set();
            release.Wait(TimeSpan.FromSeconds(5));
            mutex.ReleaseMutex();
        }) { IsBackground = true };
        holder.Start();
        Assert.True(ready.Wait(TimeSpan.FromSeconds(5)));

        var timer = new Timer(_ => release.Set(), null, 250, Timeout.Infinite);

        Mutex? acquired = null;
        try
        {
            acquired = SingleInstanceLock.Acquire(name, TimeSpan.FromSeconds(5), 50);
            Assert.NotNull(acquired);
        }
        finally
        {
            timer.Dispose();
            release.Set();
            holder.Join(TimeSpan.FromSeconds(5));
            if (acquired != null)
            {
                acquired.ReleaseMutex();
                acquired.Dispose();
            }
        }
    }

    [Fact]
    public void AFreshNameIsAcquiredImmediately()
    {
        var mutex = SingleInstanceLock.Acquire(NewName(), TimeSpan.FromMilliseconds(200), 50);
        Assert.NotNull(mutex);
        mutex!.ReleaseMutex();
        mutex.Dispose();
    }
}
