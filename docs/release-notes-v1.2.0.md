Robustness release. No behaviour change for a healthy install; several ways a start could silently do nothing are gone.

## Fixed / hardened
- **Self-healing start.** If SpatialSwitch was not running when the last watched program exited (sign-out, crash, kill) and the device is still on the "active" format, the restore format is applied at the next start instead of staying wrong until the next game.
- **Sign-in start.** When started less than three minutes after boot it waits 15 s before the first device query and retries (up to a minute) until the configured device is reported, so an early autostart cannot adopt the wrong device or format while the audio service is still coming up.
- **Settings folder resolution.** The folder is resolved once with a fallback chain (shell folder → `%APPDATA%` → `%USERPROFILE%\AppData\Roaming` → next to the executable) and is never a relative path. New `--settings <dir>` option for portable use or virtualised environments.
- **Nothing fails silently.** The log records which settings file was loaded and how many programs are watched; if the log itself cannot be written, the folder next to the executable is used; a one-line start breadcrumb goes to `%TEMP%\spatialswitch-start.txt` before anything else.
- Polling is exception-safe (a failing poll is logged, the next one still runs).

## Requirements
Unchanged: Windows 10/11 with .NET Framework 4.8, [SoundVolumeView](https://www.nirsoft.net/utils/sound_volume_view.html) 2.48+ next to `SpatialSwitch.exe`.

## Install
Replace `SpatialSwitch.exe`; settings in `%APPDATA%\SpatialSwitch` are picked up as before.
