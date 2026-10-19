Bug-fix release. Recommended for everyone on v1.2.0.

## Fixed
- **No longer blocks sign-out and shutdown.** Closing for any reason other than the window's own close button (sign-out, shutdown, Task Manager) now really exits instead of hiding to the tray. Windows no longer lists SpatialSwitch as "preventing shutdown", and the restore format is put back on the way out.
- **UI thread is STA again.** In v1.2.0 the `[STAThread]` attribute had slipped from `Main` onto a helper method, so file dialogs ("Add .exe file…") and other COM-based UI could fail.
- **"Restore = Off" is never overwritten.** An empty restore format meant both "Off" and "not chosen yet", so after a crash or sign-out mid-game the active format could be adopted as the restore format and saved. First-start adoption now has its own flag. Older settings files count as already chosen.
- **`--settings` survives a reboot.** The folder is made absolute and written into the "Start with Windows" entry. Before, the autostarted instance used `%APPDATA%` and started with an empty watch list.
- **Self-healing start respects manual changes.** At start, the restore format is applied only if the device is still on the *active* format. A format you picked by hand while SpatialSwitch was not running is left alone.
- **Settings are crash-safe.** They are written to a temp file and swapped in. An unreadable `settings.xml` is kept as `settings.xml.bad` before defaults are saved over it.
- **No stale device readings.** The device export uses a per-process temp file that is deleted before and after every read.
- **Sign-in delay works with Fast Startup.** The 15 s settle time now counts from this session's sign-in, not only from boot (with Fast Startup, uptime counts from the last full restart).
- **Fewer false "did not take effect" warnings** when a format is given as a GUID, or the device is already on a format whose GUID is not known yet.
- Changing the output device while a watched program runs hands the override over to the new device instead of leaving the old one on the active format.
- The log is trimmed to its newer half at 1 MB instead of being deleted. The start breadcrumb in `%TEMP%` is capped at 64 KB. The list of installed formats is logged only when it changes.

## Requirements
Unchanged: Windows 10/11 with .NET Framework 4.8, [SoundVolumeView](https://www.nirsoft.net/utils/sound_volume_view.html) 2.48+ next to `SpatialSwitch.exe`. The log now warns about older builds, and `scripts\get-soundvolumeview.cmd` fails loudly if the download is older than 2.48.

## Install
Exit SpatialSwitch from the tray, replace `SpatialSwitch.exe`, and start it again. Settings in `%APPDATA%\SpatialSwitch` are picked up as before.
