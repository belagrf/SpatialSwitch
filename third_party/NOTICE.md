# Third-party components

## SoundVolumeView (NirSoft)

SpatialSwitch performs the actual spatial-format switch by invoking **SoundVolumeView.exe**
(`/SetSpatial`, `/scomma`) by Nir Sofer — https://www.nirsoft.net/utils/sound_volume_view.html

SoundVolumeView is freeware. Its license permits free redistribution only if all files of the package
are included without modification and nothing is charged for it. To keep this repository clean and to
always give you NirSoft's current build, the binary is **not** committed here; `scripts\get-soundvolumeview.cmd`
downloads the official x64 package straight from nirsoft.net into `bin\` (exe, help file and readme).

Version 2.48 or newer is required (the `/SetSpatial` command was updated for Windows 11 25H2 in 2.48).
Note that NirSoft binaries are not code-signed; SmartScreen may warn on first run.
