extern alias MonitorApp;
using System.Linq;
using Xunit;
using LocalizationService = MonitorApp::BluetoothBatteryMonitor.App.Services.Localization.LocalizationService;

public class LocalizationCoverageTests
{
    // One representative key per UI section, spanning every screen and surface.
    private static readonly string[] ProbeKeys =
    {
        "App_Title", "Flyout_Header", "Device_Connected", "Codec_LDAC",
        "Tray_Settings", "Tray_TooltipTitle", "Settings_GroupAppearance",
        "Settings_AutoHideLabel", "Settings_AutoHideOff", "Settings_SaveAndClose",
        "Settings_DiscardAndClose", "Settings_Language", "Settings_RestApiPort",
        "Settings_DeviceThresholdsDesc", "GamePolling_Title", "TrayColor_Apply",
        "Update_InstallNow", "Update_Err_NoChecksum", "Update_PendingFailed",
        "About_OverviewText", "Toast_CriticalBody", "Settings_Seconds"
    };

    public static readonly string[] AllLanguages = { "en", "zh", "zht", "tr", "fr", "de", "es", "it" };

    [Fact]
    public void EightLanguagesAreOffered()
    {
        var codes = LocalizationService.SupportedLanguages.Select(l => l.Code).ToList();
        Assert.Equal(8, codes.Count);
        Assert.Equal(AllLanguages.OrderBy(c => c).ToArray(), codes.OrderBy(c => c).ToArray());
    }

    [Fact]
    public void EveryLanguageResolvesEveryProbeKey()
    {
        foreach (var lang in AllLanguages)
        {
            LocalizationService.ApplyLanguage(lang);
            foreach (var key in ProbeKeys)
            {
                var value = LocalizationService.GetString(key);
                Assert.True(!string.IsNullOrWhiteSpace(value) && value != key,
                    $"{lang}/{key} fell back to the key: {value}");
            }
        }
    }

    [Fact]
    public void PlaceholdersSurviveTranslation()
    {
        foreach (var lang in AllLanguages)
        {
            LocalizationService.ApplyLanguage(lang);
            Assert.Contains("{0}", LocalizationService.GetString("Settings_Seconds"));
            Assert.Contains("{0}", LocalizationService.GetString("Update_Available"));
            Assert.Contains("{0}", LocalizationService.GetString("About_Version"));
            Assert.Contains("{0}", LocalizationService.GetString("Toast_CriticalTitle"));
            Assert.Contains("{1}", LocalizationService.GetString("Toast_CriticalBody"));
            Assert.Contains("{1}", LocalizationService.GetString("Update_PendingFailed"));
        }
    }

    [Fact]
    public void TraditionalChineseDiffersFromSimplified()
    {
        LocalizationService.ApplyLanguage("zh");
        var simplified = LocalizationService.GetString("Flyout_Header");
        LocalizationService.ApplyLanguage("zht");
        var traditional = LocalizationService.GetString("Flyout_Header");
        Assert.NotEqual(simplified, traditional);
    }

    [Fact]
    public void AboutContentDoesNotFallBackToEnglish()
    {
        // Regression: zh used to ship without the About_Overview/Feature keys, so the
        // dialog silently showed the English seed values inside a Chinese UI.
        LocalizationService.ApplyLanguage("en");
        var englishText = LocalizationService.GetString("About_OverviewText");
        var englishHeading = LocalizationService.GetString("About_Overview");
        foreach (var lang in AllLanguages.Where(l => l != "en"))
        {
            LocalizationService.ApplyLanguage(lang);
            Assert.NotEqual(englishText, LocalizationService.GetString("About_OverviewText"));
            Assert.NotEqual(englishHeading, LocalizationService.GetString("About_Overview"));
        }
        LocalizationService.ApplyLanguage("en");
    }

    [Fact]
    public void AboutFeatureListMentionsRazerInEveryLanguage()
    {
        foreach (var lang in AllLanguages)
        {
            LocalizationService.ApplyLanguage(lang);
            Assert.Contains("Razer", LocalizationService.GetString("About_Feature1"));
        }
        LocalizationService.ApplyLanguage("en");
    }
}
