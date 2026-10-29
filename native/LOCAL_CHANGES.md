# Peripheral Battery changes

- v0.2.2: Disable Apple AirPods / Beats Beacon and Google Fast Pair advertisement discovery in the tray engine and diagnostic probe. Nearby broadcasts do not establish ownership; Windows-connected, GATT, PnP, and HID providers remain available.

The original native WPF UI and Bluetooth providers are retained under the MIT license in LICENSE.

- Added the existing Razer/MCHOSE HID backend as a separate local provider.
- Added configurable bold ring, number, and ring-with-number tray icons.
- Automatic colors use >30% / 10–30% / <10%, with user-defined colors and a fixed-color option.
- Added Simplified Chinese localization and tray previews.
- Default startup registration, automatic audio switching, and local REST API are disabled until explicitly enabled in Settings.
- Fork-specific settings, process identity, startup entry, and update URL avoid conflict with the upstream app.

Bluetooth provider support depends on the device protocol and driver; inherited model coverage is not a claim that every device was tested in this fork.

- Added Ultimate 2 receiver battery reading and physical controller identity deduplication; unknown XInput telemetry does not create an extra card.
