First public release.

**SpatialSwitch** is a tiny Windows tray app that switches Windows Spatial Sound automatically: while any program you select is running, your output device uses one spatial format (for example *Off* for a competitive shooter with its own HRTF); when the last one exits, it switches back (for example *Dolby Atmos for Headphones* for films and YouTube).

## Highlights
- Works with any spatial format Windows knows (Dolby Atmos, Windows Sonic, DTS Headphone:X, … or Off)
- Format lists show only what is installed on your PC (enumerated from installed Store packages)
- Every switch is verified by reading the device back; failures are logged instead of hidden
- First start adopts your current format as the "restore" format
- Tray icon, single instance, optional start with Windows — ~8 MB RAM while hidden, no services, no admin rights

## Requirements
- Windows 10/11 with .NET Framework 4.8 (built in)
- [SoundVolumeView](https://www.nirsoft.net/utils/sound_volume_view.html) 2.48 or newer by NirSoft (freeware, not bundled) placed next to `SpatialSwitch.exe` — run `scripts\get-soundvolumeview.cmd` from the repository, or download it yourself

## Install
Download `SpatialSwitch.exe` from this release, put it in a folder together with `SoundVolumeView.exe`, run it. No installer; settings live in `%APPDATA%\SpatialSwitch`.
