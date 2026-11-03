using System;
using System.IO;
using System.Text.Json;

namespace BluetoothBatteryMonitor.App.Services.Update;

/// <summary>What the app believed it was upgrading to when it handed over to the installer.</summary>
public sealed record PendingUpdate
{
    public string FromVersion { get; init; } = "";
    public string ToVersion { get; init; } = "";
    public string? InstallerPath { get; init; }
    public string StartedAt { get; init; } = "";
    public bool Notified { get; init; }
}

public enum PendingUpdateState
{
    None,
    Completed,
    Failed
}

/// <summary>
/// Records an update right before the installer is launched and reconciles it on the next start.
/// <para>
/// Without this the silent install is fire-and-forget: if Setup aborts (disk full, antivirus
/// block, unsigned-binary policy, a missed [Run] entry) the app simply does not come back, or
/// comes back as the old version, and the user has no way to tell that the upgrade failed.
/// </para>
/// </summary>
public static class UpdatePendingUpdate
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string FilePath => Path.Combine(UpdateInstaller.StateFolder, "update-pending.json");

    public static void Record(string fromVersion, string toVersion, string? installerPath)
    {
        try
        {
            Directory.CreateDirectory(UpdateInstaller.StateFolder);
            var pending = new PendingUpdate
            {
                FromVersion = fromVersion ?? "",
                ToVersion = toVersion ?? "",
                InstallerPath = installerPath,
                StartedAt = DateTime.Now.ToString("s")
            };
            File.WriteAllText(FilePath, JsonSerializer.Serialize(pending, JsonOptions));
        }
        catch
        {
            // A missing marker only costs us the failure report, never the update itself.
        }
    }

    public static PendingUpdate? Read()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            string json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<PendingUpdate>(json);
        }
        catch
        {
            // Unreadable or hand-edited marker: treat it as absent instead of failing startup.
            return null;
        }
    }

    public static void Clear()
    {
        try
        {
            if (File.Exists(FilePath)) File.Delete(FilePath);
        }
        catch { }
    }

    /// <summary>Marks the failure toast as shown so it does not repeat on every launch.</summary>
    public static void MarkNotified(PendingUpdate pending)
    {
        try
        {
            var updated = pending with { Notified = true };
            Directory.CreateDirectory(UpdateInstaller.StateFolder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(updated, JsonOptions));
        }
        catch { }
    }

    /// <summary>Pure verdict for a marker against the version that is actually running.</summary>
    internal static PendingUpdateState Evaluate(string currentVersion, PendingUpdate? pending)
    {
        if (pending == null) return PendingUpdateState.None;
        if (string.IsNullOrWhiteSpace(pending.ToVersion)) return PendingUpdateState.None;

        return UpdateCheckService.CompareVersions(currentVersion, pending.ToVersion) >= 0
            ? PendingUpdateState.Completed
            : PendingUpdateState.Failed;
    }

    /// <summary>
    /// Called once at startup. Clears the marker when the upgrade landed and drops the downloaded
    /// installer either way, so a failed attempt cannot be "reused" from a corrupt file.
    /// </summary>
    public static PendingUpdateState Reconcile(string currentVersion, out PendingUpdate? pending)
    {
        pending = Read();
        var state = Evaluate(currentVersion, pending);

        switch (state)
        {
            case PendingUpdateState.Completed:
                Clear();
                TryDelete(pending?.InstallerPath);
                UpdateInstaller.Log($"pending update to {pending?.ToVersion} completed (running {currentVersion})");
                break;

            case PendingUpdateState.Failed:
                TryDelete(pending?.InstallerPath);
                UpdateInstaller.Log(
                    $"pending update to {pending?.ToVersion} did not land (running {currentVersion}); installer dropped");
                break;
        }

        return state;
    }

    private static void TryDelete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // The installer may still hold the file; PruneStaleDownloads retries on a later start.
        }
    }
}
