# ESP32 App Store

A Windows app (VB.NET, .NET 9) that installs ready-made firmware on ESP32 boards: pick an app, pick the
COM port, press **Install**. No compiling. The binaries are hosted as GitHub release assets, the list
of apps is `catalog.json` in this repository, and flashing is done by Espressif's `esptool`.

## Using it

1. Install the [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0) (once).
2. Run `ESP32AppStore.exe`, plug the board in, choose its port (Espressif boards are listed first) and
   press **Install**.
3. If the board does not answer, hold BOOT / G0 while plugging it in (download mode) and press Retry.

What it does on Install:

- downloads the app's files into `%LOCALAPPDATA%\ESP32AppStore\cache` and checks each one's SHA-256;
- downloads `esptool` v4.13.0 from Espressif's GitHub releases the first time (or uses an `esptool.exe`
  placed next to the program);
- for TinyUSB firmwares (`"reset": "1200bps"` in the catalog) opens the port at 1200 baud first, so the
  board jumps into its bootloader;
- writes the files with `esptool write_flash` at their offsets, optionally after erasing the whole chip.

The **...** menu opens a local `catalog.json` (to test a catalog before publishing) or goes back to the
online one.

Command line (used for testing): `ESP32AppStore.exe --install <catalog> <app-id> <COMx> [--erase]`, the
log goes to `%LOCALAPPDATA%\ESP32AppStore\install.log`.

## Catalog format

```json
{
  "version": 1,
  "updated": "2026-11-10",
  "apps": [{
    "id": "cxl-remote", "name": "CXL Remote", "version": "2.2.1",
    "device": "Cardputer XL", "chip": "esp32s3", "category": "Tools",
    "description": "...", "notes": "...", "flash_size": "8MB",
    "reset": "default", "baud": 921600, "released": "2026-11-09",
    "files": [
      {"name": "bootloader.bin", "offset": "0x0",     "size": 20112,   "sha256": "...", "url": "https://..."},
      {"name": "partitions.bin", "offset": "0x8000",  "size": 3072,    "sha256": "...", "url": "https://..."},
      {"name": "boot_app0.bin",  "offset": "0xe000",  "size": 8192,    "sha256": "...", "url": "https://..."},
      {"name": "app.bin",        "offset": "0x10000", "size": 1221824, "sha256": "...", "url": "https://..."}
    ]
  }]
}
```

## Publishing (maintainer)

`apps.json` lists the Arduino projects: sketch folder, FQBN, build properties, build folder and where
the version string lives.

```
python tools/publish.py collect [ids]   # copy built bins to dist/, write catalog.json + catalog.local.json
python tools/publish.py build   [ids]   # compile with arduino-cli first
python tools/publish.py release [ids]   # GitHub release <id>-v<version> with the bins, push catalog.json
```

The script warns when the version in the source is not inside the built binary (a stale build).
