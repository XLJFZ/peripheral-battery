using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace BluetoothBatteryMonitor.App.Services.Update;

/// <summary>
/// Headless end-to-end run of the update pipeline: check → download → verify → launch.
/// Used to prove a release can actually upgrade an installed copy, without driving the UI.
/// Writes its report before the installer closes this process, so the file survives the upgrade.
/// </summary>
public static class UpdateSelfTest
{
    public static async Task Run(string path)
    {
        var stages = new List<object>();
        var report = new Dictionary<string, object?>
        {
            ["startedAt"] = DateTime.Now.ToString("s"),
            ["currentVersion"] = UpdateCheckService.GetCurrentVersion(),
            ["installedCopy"] = UpdateInstaller.IsInstalledCopy(),
            ["logPath"] = UpdateInstaller.LogPath,
            ["stages"] = stages
        };

        void Save()
        {
            try
            {
                File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                report["reportError"] = ex.Message;
            }
        }

        var check = await new UpdateCheckService().CheckForUpdatesAsync();
        report["latestVersion"] = check.LatestVersion;
        report["hasUpdate"] = check.HasUpdate;
        report["downloadUrl"] = check.DownloadUrl;
        report["checksumUrl"] = check.ChecksumUrl;
        report["checkError"] = check.ErrorMessage;
        Save();

        if (!check.HasUpdate)
        {
            report["success"] = true;
            report["note"] = "already up to date";
            Save();
            return;
        }

        var progress = new StageCollector(stages);

        var result = await UpdateInstaller.DownloadAndLaunchAsync(check, progress);
        report["success"] = result.Success;
        report["errorCode"] = result.ErrorCode;
        report["installerPath"] = result.InstallerPath;
        report["finishedAt"] = DateTime.Now.ToString("s");
        Save();
    }

    /// <summary>Records one entry per stage. Synchronous on purpose: Progress&lt;T&gt; would post
    /// callbacks to the dispatcher and the report could be written before they run.</summary>
    private sealed class StageCollector : IProgress<UpdateProgress>
    {
        private readonly List<object> _stages;
        private string _lastStage = "";

        public StageCollector(List<object> stages) => _stages = stages;

        public void Report(UpdateProgress value)
        {
            string stage = value.Stage.ToString();
            if (stage == _lastStage) return;
            _lastStage = stage;
            _stages.Add(new { stage, percent = value.Percent });
        }
    }
}
