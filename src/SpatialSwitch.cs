// SpatialSwitch — lightweight tray watcher that switches Windows Spatial Sound (Dolby Atmos, Windows Sonic,
// DTS Headphone:X, … or Off) automatically while selected programs run, and switches back afterwards.
//
// Build:   scripts\build.cmd   (uses the C# compiler that ships with Windows — no SDK required)
// Runtime: .NET Framework 4.8 (part of Windows 10/11). The switch itself is performed by NirSoft's
//          SoundVolumeView.exe (/SetSpatial), which must sit next to SpatialSwitch.exe (see scripts\get-soundvolumeview.cmd).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Serialization;
using Microsoft.Win32;

[assembly: AssemblyTitle("SpatialSwitch")]
[assembly: AssemblyDescription("Switch Windows Spatial Sound automatically while selected programs run")]
[assembly: AssemblyProduct("SpatialSwitch")]
[assembly: AssemblyVersion("1.2.1.0")]
[assembly: AssemblyFileVersion("1.2.1.0")]

namespace SpatialSwitch
{
    public class FormatGuid { public string Guid; public string Name; }

    public class Settings
    {
        public List<string> WatchedProcesses = new List<string>();
        /// <summary>Command-line friendly ID of the output device (SoundVolumeView format). Empty = default render device.</summary>
        public string DeviceId = "";
        /// <summary>Format applied while any watched program runs. Empty = spatial sound Off (plain stereo).</summary>
        public string ActiveFormat = "";
        /// <summary>Format restored when no watched program runs. Empty = spatial sound Off (plain stereo).</summary>
        public string RestoreFormat = "";
        /// <summary>True until a restore format has been adopted from the device or chosen in the window. Kept apart from
        /// RestoreFormat because "" there means Off, a deliberate choice that must never be overwritten. Absent in older
        /// settings files, which therefore count as chosen.</summary>
        public bool RestoreFormatUnset;
        public bool WatchingEnabled = true;
        public bool StartWithWindows = true;
        public bool RestoreOnExit = true;
        public int PollMs = 1000;
        /// <summary>GUID→name pairs learned from successful switches, so the status line can show names for formats we've seen.</summary>
        public List<FormatGuid> LearnedFormats = new List<FormatGuid>();

        static string dir;
        /// <summary>Directory given on the command line (--settings &lt;dir&gt;), if any.</summary>
        public static string DirOverride;
        /// <summary>True when %APPDATA% could not be resolved and the folder next to the executable is used instead.</summary>
        public static bool DirUnresolved;
        /// <summary>True when Load() found and read settings.xml (false = built-in defaults are in use).</summary>
        public static bool Loaded;

        /// <summary>
        /// Settings directory, resolved once per process. Right after sign-in (the Run key starts us early) the
        /// roaming AppData folder can come back empty for a moment; an empty answer would silently make every path
        /// relative, the settings file would "not exist" and the app would run with an empty watch list and no log.
        /// So: shell folder, then the environment, then the folder next to the executable — never a relative path.
        /// </summary>
        public static string Dir
        {
            get
            {
                if (dir != null) return dir;
                string d = DirOverride;
                if (string.IsNullOrEmpty(d)) { try { d = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify); } catch { d = null; } }
                if (string.IsNullOrEmpty(d)) d = Environment.GetEnvironmentVariable("APPDATA");
                if (string.IsNullOrEmpty(d)) { var up = Environment.GetEnvironmentVariable("USERPROFILE"); if (!string.IsNullOrEmpty(up)) d = Path.Combine(up, "AppData", "Roaming"); }
                if (string.IsNullOrEmpty(d) || !Path.IsPathRooted(d)) { d = AppDomain.CurrentDomain.BaseDirectory; DirUnresolved = true; }
                if (string.IsNullOrEmpty(DirOverride)) d = Path.Combine(d, "SpatialSwitch");
                dir = d;
                return dir;
            }
        }
        public static string FilePath { get { return Path.Combine(Dir, "settings.xml"); } }

        public static Settings Load()
        {
            Loaded = false;
            try
            {
                if (File.Exists(FilePath))
                    using (var fs = File.OpenRead(FilePath))
                    {
                        var loaded = (Settings)new XmlSerializer(typeof(Settings)).Deserialize(fs);
                        Loaded = true;
                        return loaded;
                    }
            }
            catch (Exception ex)
            {
                Program.Breadcrumb("settings load failed: " + ex.GetType().Name + ": " + ex.Message); Log.Write("settings load failed: " + ex.Message);
                // the next Save() would replace the unreadable file with defaults - keep a copy so the watch list can be recovered
                try { File.Copy(FilePath, FilePath + ".bad", true); Log.Write("unreadable settings file kept as " + FilePath + ".bad"); } catch { }
            }
            return new Settings { RestoreFormatUnset = true };
        }

        /// <summary>Writes a temp file and swaps it in, so a crash or power loss mid-write never leaves a truncated settings file.</summary>
        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                string tmp = FilePath + ".tmp";
                using (var fs = File.Create(tmp))
                    new XmlSerializer(typeof(Settings)).Serialize(fs, this);
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null, true);
                else File.Move(tmp, FilePath);
            }
            catch (Exception ex) { Log.Write("settings save failed: " + ex.Message); }
        }
    }

    public static class Log
    {
        static readonly object gate = new object();
        public static string Path { get { return System.IO.Path.Combine(Settings.Dir, "spatialswitch.log"); } }
        public static void Write(string msg)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + Environment.NewLine;
            lock (gate)
            {
                // settings folder first; if that is not writable (unresolved profile, permissions), the folder next to the
                // executable — a log that silently goes nowhere is how a broken start stays invisible
                foreach (var p in new[] { Path, System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "spatialswitch.log") })
                {
                    try
                    {
                        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(p));
                        Trim(p, 1024 * 1024);
                        File.AppendAllText(p, line);
                        return;
                    }
                    catch (Exception ex) { Program.Breadcrumb("log write to " + p + " failed: " + ex.GetType().Name + ": " + ex.Message); }
                }
            }
        }

        /// <summary>Once a file exceeds maxBytes, keeps only its newer half (cut at a line break).</summary>
        public static void Trim(string path, long maxBytes)
        {
            var fi = new FileInfo(path);
            if (!fi.Exists || fi.Length <= maxBytes) return;
            string text = File.ReadAllText(path);
            int cut = text.IndexOf('\n', text.Length / 2);
            File.WriteAllText(path, cut < 0 ? "" : text.Substring(cut + 1));
        }
    }

    public class AudioDevice
    {
        public string Id; public string Name; public string Adapter; public bool IsDefault;
        public override string ToString() { return Name + "  —  " + Adapter + (IsDefault ? "  (default)" : ""); }
    }

    /// <summary>
    /// Spatial formats. Windows learns about them from Store packages (Dolby Access, DTS Sound Unbound, …) that
    /// register "windows.mediaCodec" audio encoders taking PCM input; Windows Sonic is built in. SoundVolumeView
    /// accepts either a format's display name or its GUID.
    /// </summary>
    public static class Formats
    {
        public const string Off = "";
        public const string OffLabel = "Off (plain stereo)";
        public const string WindowsSonic = "Windows Sonic for Headphones";
        const string PcmFloatSubtype = "00000003-0000-0010-8000-00AA00389B71";

        static readonly Dictionary<string, string> guidNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "{1459AC38-3875-49BF-BB59-0FE80F4D395D}", "Dolby Atmos for Headphones" },
            { "{B53D940C-B846-4831-9F76-D102B9B725A0}", WindowsSonic },
        };

        public static void Learn(string guid, string name) { if (!string.IsNullOrEmpty(guid) && !string.IsNullOrEmpty(name)) guidNames[guid] = name; }

        /// <summary>Turns a GUID reported by the system into a name if known; unknown GUIDs are kept verbatim (SoundVolumeView accepts GUIDs).</summary>
        public static string FromGuid(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return Off;
            string n; return guidNames.TryGetValue(guid, out n) ? n : guid;
        }
        /// <summary>True if both denote the same format; each side may be a name, a GUID or "" (Off). A name and a GUID
        /// only match once that GUID's name is known (built in, or learned from an earlier switch).</summary>
        public static bool Same(string a, string b) { return string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase); }
        static string Normalize(string f) { f = (f ?? "").Trim(); return f.StartsWith("{") ? FromGuid(f) : f; }
        /// <summary>True if a GUID is known for this format name, i.e. a device reading can confirm or rule it out.</summary>
        public static bool IsKnownName(string name) { return guidNames.Values.Any(v => string.Equals(v, name, StringComparison.OrdinalIgnoreCase)); }
        public static string Label(string format) { return string.IsNullOrEmpty(format) ? OffLabel : format; }
        public static string FromLabel(string label) { var t = (label ?? "").Replace("\"", "").Trim(); return t == OffLabel ? Off : t; }

        /// <summary>
        /// Formats installed on this machine: Windows Sonic plus every PCM-input audio encoder that installed packages
        /// register — the same set the Settings app offers (a format may still not apply to a given device type;
        /// the switch is verified by reading the device back, see MainForm.Apply).
        /// </summary>
        public static List<string> Installed()
        {
            var list = new List<string> { WindowsSonic };
            try
            {
                using (var root = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages"))
                {
                    if (root != null)
                        foreach (var sub in root.GetSubKeyNames())
                        {
                            try
                            {
                                using (var k = root.OpenSubKey(sub))
                                {
                                    var folder = k == null ? null : k.GetValue("PackageRootFolder") as string;
                                    if (string.IsNullOrEmpty(folder)) continue;
                                    var manifest = Path.Combine(folder, "AppxManifest.xml");
                                    if (!File.Exists(manifest)) continue;
                                    foreach (var n in SpatialEncodersIn(manifest))
                                        if (!list.Contains(n, StringComparer.OrdinalIgnoreCase)) list.Add(n);
                                }
                            }
                            catch { }
                        }
                }
            }
            catch (Exception ex) { Log.Write("format enumeration: " + ex.Message); }
            return list;
        }

        static IEnumerable<string> SpatialEncodersIn(string manifestPath)
        {
            var found = new List<string>();
            string text;
            try { text = File.ReadAllText(manifestPath); } catch { return found; }
            if (text.IndexOf("audioEncoder", StringComparison.OrdinalIgnoreCase) < 0) return found;   // cheap pre-filter
            try
            {
                var doc = new XmlDocument(); doc.LoadXml(text);
                foreach (XmlNode codec in doc.SelectNodes("//*[local-name()='MediaCodec' and @Category='audioEncoder']"))
                {
                    var name = codec.Attributes["DisplayName"] == null ? null : codec.Attributes["DisplayName"].Value;
                    if (string.IsNullOrEmpty(name)) continue;
                    bool takesPcm = false;
                    foreach (XmlNode it in codec.SelectNodes(".//*[local-name()='InputType']"))
                        if (it.Attributes["SubType"] != null && string.Equals(it.Attributes["SubType"].Value, PcmFloatSubtype, StringComparison.OrdinalIgnoreCase)) takesPcm = true;
                    if (takesPcm) found.Add(name);
                }
            }
            catch (Exception ex) { Log.Write("manifest parse (" + manifestPath + "): " + ex.Message); }
            return found;
        }
    }

    /// <summary>Thin wrapper around NirSoft SoundVolumeView.exe.</summary>
    public static class Svv
    {
        public static string ExePath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SoundVolumeView.exe"); } }
        public static bool Available { get { return File.Exists(ExePath); } }
        /// <summary>/SetSpatial was updated for Windows 11 25H2 in 2.48; older builds may silently do nothing there.</summary>
        public static readonly Version MinVersion = new Version(2, 48);

        /// <summary>File version of SoundVolumeView.exe, or null if it is missing or unreadable. Read from the version
        /// string: NirSoft stores "2.53" as the numeric parts 2.5.3.0.</summary>
        public static Version FileVersion()
        {
            try
            {
                Version v;
                return Version.TryParse((FileVersionInfo.GetVersionInfo(ExePath).FileVersion ?? "").Trim(), out v) ? v : null;
            }
            catch { return null; }
        }

        static int Run(string args)
        {
            var psi = new ProcessStartInfo(ExePath, args) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            try
            {
                // can throw: exe deleted or blocked, or Windows refusing new processes while the session ends
                using (var p = Process.Start(psi))
                {
                    if (!p.WaitForExit(20000)) { try { p.Kill(); } catch { } return -1; }
                    return p.ExitCode;
                }
            }
            catch (Exception ex) { Log.Write("SoundVolumeView " + args + " failed to run: " + ex.Message); return -1; }
        }

        /// <summary>Asks SoundVolumeView to set the format. Note: it returns 0 even for a format that is not installed
        /// (nothing changes then), so callers must read the device back — see MainForm.Apply.</summary>
        public static bool SetSpatial(string deviceId, string format)
        {
            int rc = Run("/SetSpatial \"" + deviceId + "\" \"" + (format ?? "").Replace("\"", "") + "\"");
            Log.Write("SetSpatial [" + deviceId + "] -> " + Formats.Label(format) + "  rc=" + rc);
            return rc == 0;
        }

        static List<Dictionary<string, string>> Export()
        {
            var rows = new List<Dictionary<string, string>>();
            string tmp = Path.Combine(Path.GetTempPath(), "spatialswitch-devices-" + Process.GetCurrentProcess().Id + ".csv");
            // never read a previous export: a run that "succeeds" without writing would otherwise report stale device state
            try { File.Delete(tmp); } catch { return rows; }
            string[] lines;
            try
            {
                if (Run("/scomma \"" + tmp + "\"") != 0 || !File.Exists(tmp)) return rows;
                using (var sr = new StreamReader(tmp, Encoding.Default, true)) lines = sr.ReadToEnd().Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            }
            finally { try { File.Delete(tmp); } catch { } }
            if (lines.Length < 2) return rows;
            var header = ParseCsvLine(lines[0]);
            for (int i = 1; i < lines.Length; i++)
            {
                var f = ParseCsvLine(lines[i]);
                var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int c = 0; c < header.Count && c < f.Count; c++) d[header[c]] = f[c];
                rows.Add(d);
            }
            return rows;
        }

        static List<string> ParseCsvLine(string line)
        {
            var result = new List<string>(); var sb = new StringBuilder(); bool inQ = false;
            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (inQ)
                {
                    if (ch == '"') { if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; } else inQ = false; }
                    else sb.Append(ch);
                }
                else if (ch == '"') inQ = true;
                else if (ch == ',') { result.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(ch);
            }
            result.Add(sb.ToString());
            return result;
        }

        static string Get(Dictionary<string, string> d, string key) { string v; return d.TryGetValue(key, out v) ? v : ""; }

        public static List<AudioDevice> RenderDevices()
        {
            var list = new List<AudioDevice>();
            foreach (var r in Export())
            {
                if (Get(r, "Type") != "Device" || Get(r, "Direction") != "Render") continue;
                list.Add(new AudioDevice { Id = Get(r, "Command-Line Friendly ID"), Name = Get(r, "Name"), Adapter = Get(r, "Device Name"), IsDefault = Get(r, "Default").IndexOf("Render", StringComparison.OrdinalIgnoreCase) >= 0 });
            }
            return list;
        }

        /// <summary>Current spatial format GUID of a device ("" = off, null = device not found).</summary>
        public static string SpatialGuid(string deviceId)
        {
            foreach (var r in Export())
                if (string.Equals(Get(r, "Command-Line Friendly ID"), deviceId, StringComparison.OrdinalIgnoreCase))
                    return Get(r, "Spatial Guid");
            return null;
        }
    }

    public class PickRunningForm : Form
    {
        public string Chosen;
        public PickRunningForm()
        {
            Text = "Add a running program"; Width = 420; Height = 460; StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            var lst = new ListBox { Left = 12, Top = 12, Width = 380, Height = 360, Sorted = true };
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in Process.GetProcesses()) { try { names.Add(p.ProcessName + ".exe"); } catch { } finally { p.Dispose(); } }
            foreach (var n in names) lst.Items.Add(n);
            var ok = new Button { Text = "Add", Left = 236, Top = 382, Width = 75, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", Left = 317, Top = 382, Width = 75, DialogResult = DialogResult.Cancel };
            ok.Click += (s, e) => { Chosen = lst.SelectedItem as string; };
            lst.DoubleClick += (s, e) => { Chosen = lst.SelectedItem as string; DialogResult = DialogResult.OK; Close(); };
            Controls.AddRange(new Control[] { lst, ok, cancel }); AcceptButton = ok; CancelButton = cancel;
        }
    }

    public class MainForm : Form
    {
        readonly Settings s;
        readonly NotifyIcon tray;
        readonly System.Windows.Forms.Timer timer;
        readonly ListBox lstApps; readonly ComboBox cboDevice, cboActive, cboRestore; readonly CheckBox chkEnabled, chkAutostart, chkRestoreExit; readonly Label lblStatus;
        readonly ToolStripMenuItem miEnabled;
        bool overrideActive;      // true while WE have applied the "active" format because a watched program runs
        bool lastApplyFailed;     // the last switch did not take effect (format probably not installed for this device)
        bool reallyExit;
        string lastKnown = "(unknown)";

        public MainForm(bool startHidden)
        {
            s = Settings.Load();
            Log.Write(Settings.Loaded
                ? "settings loaded from " + Settings.FilePath + ": " + s.WatchedProcesses.Count + " watched program(s), device '" + s.DeviceId + "', active '" + Formats.Label(s.ActiveFormat) + "', restore '" + Formats.Label(s.RestoreFormat) + "'"
                : "no usable settings file at " + Settings.FilePath + " -> defaults (nothing is watched until programs are added)");
            foreach (var fg in s.LearnedFormats) Formats.Learn(fg.Guid, fg.Name);
            Text = "SpatialSwitch"; Width = 560; Height = 540; FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false; StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var lbl1 = new Label { Text = "Watched programs — while any of them runs, the output device is switched to the \"active\" format below; when the last one exits, the \"restore\" format is applied:", Left = 12, Top = 12, Width = 520, Height = 34 };
            lstApps = new ListBox { Left = 12, Top = 50, Width = 380, Height = 150 };
            foreach (var w in s.WatchedProcesses) lstApps.Items.Add(w);
            var btnAddExe = new Button { Text = "Add .exe file…", Left = 404, Top = 50, Width = 128 };
            var btnAddRun = new Button { Text = "Add running…", Left = 404, Top = 82, Width = 128 };
            var btnRemove = new Button { Text = "Remove", Left = 404, Top = 114, Width = 128 };
            btnAddExe.Click += (o, e) => { using (var d = new OpenFileDialog { Filter = "Programs (*.exe)|*.exe", Title = "Pick the program's executable" }) if (d.ShowDialog(this) == DialogResult.OK) AddWatched(Path.GetFileName(d.FileName)); };
            btnAddRun.Click += (o, e) => { using (var d = new PickRunningForm()) if (d.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(d.Chosen)) AddWatched(d.Chosen); };
            btnRemove.Click += (o, e) => { if (lstApps.SelectedItem != null) { s.WatchedProcesses.Remove((string)lstApps.SelectedItem); lstApps.Items.Remove(lstApps.SelectedItem); s.Save(); } };

            var lbl2 = new Label { Text = "Output device:", Left = 12, Top = 212, Width = 200 };
            cboDevice = new ComboBox { Left = 12, Top = 232, Width = 520, DropDownStyle = ComboBoxStyle.DropDownList };

            var lbl3 = new Label { Text = "Active format (while a watched program runs) — list shows formats installed on this PC:", Left = 12, Top = 266, Width = 520 };
            cboActive = new ComboBox { Left = 12, Top = 286, Width = 520, DropDownStyle = ComboBoxStyle.DropDown };
            cboActive.Text = Formats.Label(s.ActiveFormat);

            var lbl4 = new Label { Text = "Restore format (when no watched program runs):", Left = 12, Top = 318, Width = 400 };
            cboRestore = new ComboBox { Left = 12, Top = 338, Width = 400, DropDownStyle = ComboBoxStyle.DropDown };
            cboRestore.Text = Formats.Label(s.RestoreFormat);
            var btnUseCurrent = new Button { Text = "Use current", Left = 420, Top = 337, Width = 112 };
            btnUseCurrent.Click += (o, e) => { var cur = CurrentFormat(); if (cur != null) cboRestore.Text = Formats.Label(cur); };

            chkEnabled = new CheckBox { Text = "Watching enabled", Left = 12, Top = 372, Width = 200, Checked = s.WatchingEnabled };
            chkAutostart = new CheckBox { Text = "Start with Windows (hidden in tray)", Left = 12, Top = 396, Width = 300, Checked = s.StartWithWindows };
            chkRestoreExit = new CheckBox { Text = "Apply restore format when SpatialSwitch exits", Left = 12, Top = 420, Width = 340, Checked = s.RestoreOnExit };

            var btnRestoreNow = new Button { Text = "Apply restore now", Left = 12, Top = 452, Width = 140 };
            var btnActiveNow = new Button { Text = "Apply active now", Left = 158, Top = 452, Width = 140 };
            var btnRefresh = new Button { Text = "Refresh", Left = 304, Top = 452, Width = 110 };
            var btnSave = new Button { Text = "Save", Left = 457, Top = 452, Width = 75 };
            lblStatus = new Label { Left = 12, Top = 486, Width = 520, Height = 20, Text = "…" };
            btnRestoreNow.Click += (o, e) => { SaveFromUi(); Apply(s.RestoreFormat); overrideActive = false; UpdateStatusText(AnyWatchedRunning()); };
            btnActiveNow.Click += (o, e) => { SaveFromUi(); Apply(s.ActiveFormat); UpdateStatusText(AnyWatchedRunning()); };
            btnRefresh.Click += (o, e) => { PopulateFormats(); RefreshStatus(true); };
            btnSave.Click += (o, e) => { SaveFromUi(); RefreshStatus(false); };
            Controls.AddRange(new Control[] { lbl1, lstApps, btnAddExe, btnAddRun, btnRemove, lbl2, cboDevice, lbl3, cboActive, lbl4, cboRestore, btnUseCurrent, chkEnabled, chkAutostart, chkRestoreExit, btnRestoreNow, btnActiveNow, btnRefresh, btnSave, lblStatus });

            // tray
            var menu = new ContextMenuStrip();
            menu.Items.Add("Open", null, (o, e) => ShowWindow());
            miEnabled = new ToolStripMenuItem("Watching enabled") { Checked = s.WatchingEnabled, CheckOnClick = true };
            miEnabled.CheckedChanged += (o, e) => { s.WatchingEnabled = miEnabled.Checked; chkEnabled.Checked = s.WatchingEnabled; s.Save(); };
            menu.Items.Add(miEnabled);
            menu.Items.Add("Apply restore format now", null, (o, e) => { Apply(s.RestoreFormat); overrideActive = false; UpdateStatusText(AnyWatchedRunning()); });
            menu.Items.Add("Apply active format now", null, (o, e) => { Apply(s.ActiveFormat); UpdateStatusText(AnyWatchedRunning()); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (o, e) => { reallyExit = true; Close(); });
            tray = new NotifyIcon { Icon = Icon, Visible = true, Text = "SpatialSwitch", ContextMenuStrip = menu };
            tray.DoubleClick += (o, e) => ShowWindow();

            timer = new System.Windows.Forms.Timer { Interval = Math.Max(500, s.PollMs) };
            timer.Tick += (o, e) => Poll();

            var showEvt = new EventWaitHandle(false, EventResetMode.AutoReset, "SpatialSwitch_Show");
            ThreadPool.RegisterWaitForSingleObject(showEvt, (st, to) => { try { BeginInvoke(new Action(ShowWindow)); } catch { } }, null, -1, false);

            Load += (o, e) =>
            {
                // Started by the Run key right after sign-in? Give the audio service and the profile a moment before the
                // first device query; otherwise the configured device may not be reported yet.
                int sinceSignIn = SecondsSinceSignIn();
                if (startHidden && sinceSignIn >= 0 && sinceSignIn < 180)
                {
                    Log.Write("started " + sinceSignIn + " s after boot/sign-in - waiting 15 s before the first device query");
                    Later(15000, InitAfterStartup);
                }
                else InitAfterStartup();
                ApplyAutostart();
                if (startHidden) { Hide(); ShowInTaskbar = false; TrimMemory(); }
            };
            FormClosing += (o, e) =>
            {
                // only the window's own close button hides to the tray; sign-out, shutdown and Task Manager really close -
                // cancelling those would make Windows list SpatialSwitch as "preventing shutdown"
                if (!reallyExit && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); ShowInTaskbar = false; tray.ShowBalloonTip(1500, "SpatialSwitch", "Still watching in the tray. Use Exit in the tray menu to quit.", ToolTipIcon.Info); TrimMemory(); return; }
                timer.Stop();
                if (overrideActive && s.RestoreOnExit && Svv.Available)
                {
                    if (e.CloseReason == CloseReason.WindowsShutDown)
                    {
                        // Windows waits only a few seconds per app at sign-out: switch once, skip the read-back verification
                        Svv.SetSpatial(s.DeviceId, s.RestoreFormat); overrideActive = false;
                        Log.Write("session end: sent restore format " + Formats.Label(s.RestoreFormat));
                    }
                    else { Apply(s.RestoreFormat); Log.Write("exit: applied restore format " + Formats.Label(s.RestoreFormat)); }
                }
                Log.Write("closing (" + e.CloseReason + ")");
                tray.Visible = false;
            };
        }

        int startupRetries;

        /// <summary>Runs an action once on the UI thread after a delay.</summary>
        void Later(int ms, Action action)
        {
            var t = new System.Windows.Forms.Timer { Interval = ms };
            t.Tick += (o, e) => { t.Stop(); t.Dispose(); action(); };
            t.Start();
        }

        /// <summary>Device/format discovery, first-start adoption, self-heal, then regular polling.</summary>
        void InitAfterStartup()
        {
            PopulateDevices();
            PopulateFormats();
            if (!Svv.Available)
            {
                lblStatus.Text = "SoundVolumeView.exe is missing next to SpatialSwitch.exe — run scripts\\get-soundvolumeview.cmd. Switching disabled.";
                Log.Write(lblStatus.Text); timer.Start(); return;
            }
            if (startupRetries == 0)
            {
                var v = Svv.FileVersion();
                if (v != null && v < Svv.MinVersion) Log.Write("WARNING: SoundVolumeView " + v + " is older than " + Svv.MinVersion + " - /SetSpatial may silently do nothing on Windows 11 25H2; run scripts\\get-soundvolumeview.cmd");
                else Log.Write("SoundVolumeView " + (v == null ? "(version unknown)" : v.ToString()));
            }
            // configured device not reported yet (audio service still coming up)? retry for up to a minute
            if (!string.IsNullOrEmpty(s.DeviceId) && Svv.SpatialGuid(s.DeviceId) == null && startupRetries++ < 30)
            {
                Log.Write("device '" + s.DeviceId + "' not reported yet - retrying in 2 s"); Later(2000, InitAfterStartup); return;
            }
            // first start: adopt whatever format is currently in use (Off included) as the restore format
            if (s.RestoreFormatUnset && !string.IsNullOrEmpty(s.DeviceId))
            {
                var cur = CurrentFormat();
                if (cur != null) { s.RestoreFormat = cur; s.RestoreFormatUnset = false; cboRestore.Text = Formats.Label(cur); s.Save(); Log.Write("adopted current format as restore format: " + Formats.Label(cur)); }
            }
            RefreshStatus(true);
            // self-heal: if we were not running when the last watched program exited (crash, kill, power loss), the device
            // is still on the "active" format - put the restore format back before regular polling starts. Only from the
            // active format: any other format was chosen by hand while we were not running and is left alone.
            if (s.WatchingEnabled && !string.IsNullOrEmpty(s.DeviceId) && !s.RestoreFormatUnset && !Formats.Same(s.ActiveFormat, s.RestoreFormat) && !AnyWatchedRunning())
            {
                var cur = CurrentFormat();
                if (cur != null && Formats.Same(cur, s.ActiveFormat))
                {
                    Log.Write("start: no watched program runs but the device is still on the active format " + Formats.Label(cur) + " -> applying restore format " + Formats.Label(s.RestoreFormat));
                    Apply(s.RestoreFormat); RefreshStatus(false);
                }
            }
            Poll();
            timer.Start();
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern bool SetProcessWorkingSetSize(IntPtr h, IntPtr min, IntPtr max);
        /// <summary>Release startup/JIT pages while hidden so the tray footprint stays small.</summary>
        static void TrimMemory() { try { GC.Collect(); GC.WaitForPendingFinalizers(); SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, (IntPtr)(-1), (IntPtr)(-1)); } catch { } }

        /// <summary>Seconds since the later of boot and this session's sign-in. Uptime alone is not enough: with Fast Startup
        /// (the default) "Shut down" resumes the kernel, so uptime counts from the last full restart, and a sign-out/sign-in
        /// keeps it running too. The session's oldest Explorer stands in for the sign-in time. -1 if neither is known.</summary>
        static int SecondsSinceSignIn()
        {
            int uptime = Environment.TickCount >= 0 ? Environment.TickCount / 1000 : -1;
            int shell = -1;
            try
            {
                int session = Process.GetCurrentProcess().SessionId;
                foreach (var p in Process.GetProcessesByName("explorer"))
                {
                    try { if (p.SessionId == session) shell = Math.Max(shell, (int)(DateTime.Now - p.StartTime).TotalSeconds); }
                    catch { }
                    finally { p.Dispose(); }
                }
            }
            catch { }
            if (uptime < 0) return shell;
            if (shell < 0) return uptime;
            return Math.Min(uptime, shell);
        }

        void ShowWindow() { Show(); ShowInTaskbar = true; WindowState = FormWindowState.Normal; Activate(); PopulateFormats(); RefreshStatus(true); }

        /// <summary>Format currently set on the configured device, as a name or GUID; null if unavailable.</summary>
        string CurrentFormat()
        {
            if (!Svv.Available || string.IsNullOrEmpty(s.DeviceId)) return null;
            var guid = Svv.SpatialGuid(s.DeviceId);
            return guid == null ? null : Formats.FromGuid(guid);
        }

        /// <summary>Switches the device and verifies by reading it back (SoundVolumeView reports success even for
        /// formats that are not installed). Learns GUID→name on success. Returns true if the device now reports the requested format.</summary>
        bool Apply(string format)
        {
            if (!Svv.Available || string.IsNullOrEmpty(s.DeviceId)) return false;
            string before = Svv.SpatialGuid(s.DeviceId);
            bool sent = Svv.SetSpatial(s.DeviceId, format);
            string after = Svv.SpatialGuid(s.DeviceId);
            bool ok, changed = after != null && after != before;
            if (!sent || after == null) ok = false;
            else if (string.IsNullOrEmpty(format)) ok = after == "";
            else if (after == "") ok = false;
            else if (Formats.Same(after, format) || changed) ok = true;
            // unchanged and the device's GUID has no name yet: if the requested name has no known GUID either, the device was
            // most likely already on it - nothing proves a failure, so don't warn (but don't learn the pair from a guess)
            else ok = !format.StartsWith("{") && !Formats.IsKnownName(format) && Formats.FromGuid(after).StartsWith("{");
            if (ok && changed && !string.IsNullOrEmpty(format) && !format.StartsWith("{"))
            {
                Formats.Learn(after, format);
                if (!s.LearnedFormats.Any(x => string.Equals(x.Guid, after, StringComparison.OrdinalIgnoreCase))) { s.LearnedFormats.Add(new FormatGuid { Guid = after, Name = format }); s.Save(); }
            }
            lastKnown = after == null ? "device not found" : Formats.Label(Formats.FromGuid(after));
            lastApplyFailed = !ok;
            if (!ok) Log.Write("WARNING: '" + Formats.Label(format) + "' did not take effect — device still reports " + lastKnown + ". Is that format installed and valid for this device?");
            return ok;
        }

        void AddWatched(string exeName)
        {
            if (string.IsNullOrEmpty(exeName)) return;
            if (!s.WatchedProcesses.Any(w => string.Equals(w, exeName, StringComparison.OrdinalIgnoreCase))) { s.WatchedProcesses.Add(exeName); lstApps.Items.Add(exeName); s.Save(); }
        }

        void PopulateDevices()
        {
            cboDevice.Items.Clear();
            if (!Svv.Available) return;
            AudioDevice sel = null;
            foreach (var d in Svv.RenderDevices())
            {
                cboDevice.Items.Add(d);
                if (!string.IsNullOrEmpty(s.DeviceId) && string.Equals(d.Id, s.DeviceId, StringComparison.OrdinalIgnoreCase)) sel = d;
                else if (string.IsNullOrEmpty(s.DeviceId) && d.IsDefault) sel = d;
            }
            if (sel != null) cboDevice.SelectedItem = sel; else if (cboDevice.Items.Count > 0) cboDevice.SelectedIndex = 0;
            var chosen = cboDevice.SelectedItem as AudioDevice;
            if (chosen != null && string.IsNullOrEmpty(s.DeviceId)) { s.DeviceId = chosen.Id; s.Save(); }
        }

        /// <summary>Fills both format lists with Off + the formats installed on this machine (keeps the typed text).</summary>
        void PopulateFormats()
        {
            var installed = Formats.Installed();
            foreach (var cbo in new[] { cboActive, cboRestore })
            {
                string keep = cbo.Text;
                cbo.Items.Clear(); cbo.Items.Add(Formats.OffLabel);
                foreach (var f in installed) cbo.Items.Add(f);
                cbo.Text = keep;
            }
            string list = string.Join(", ", installed);
            if (list != lastFormatList) { Log.Write("installed spatial formats: " + list); lastFormatList = list; }
        }
        string lastFormatList;

        void SaveFromUi()
        {
            string oldDevice = s.DeviceId;
            var d = cboDevice.SelectedItem as AudioDevice; if (d != null) s.DeviceId = d.Id;
            s.ActiveFormat = Formats.FromLabel(cboActive.Text);
            s.RestoreFormat = Formats.FromLabel(cboRestore.Text); s.RestoreFormatUnset = false;
            s.WatchingEnabled = chkEnabled.Checked; miEnabled.Checked = s.WatchingEnabled;
            s.StartWithWindows = chkAutostart.Checked; s.RestoreOnExit = chkRestoreExit.Checked;
            s.Save(); ApplyAutostart();
            // device changed while a watched program runs: hand the override over instead of stranding the old device on it
            if (overrideActive && Svv.Available && !string.IsNullOrEmpty(oldDevice) && !string.Equals(oldDevice, s.DeviceId, StringComparison.OrdinalIgnoreCase))
            {
                Log.Write("device changed during override -> restoring '" + oldDevice + "', applying active format to '" + s.DeviceId + "'");
                Svv.SetSpatial(oldDevice, s.RestoreFormat);
                Apply(s.ActiveFormat);
            }
        }

        void ApplyAutostart()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    string cmd = "\"" + Application.ExecutablePath + "\" --tray";
                    if (!string.IsNullOrEmpty(Settings.DirOverride))
                    {
                        // a trailing backslash would escape the closing quote ("C:\x\" -> C:\x")
                        string dirArg = Settings.DirOverride.EndsWith("\\") ? Settings.DirOverride + "." : Settings.DirOverride;
                        cmd += " --settings \"" + dirArg + "\"";
                    }
                    if (s.StartWithWindows) k.SetValue("SpatialSwitch", cmd);
                    else k.DeleteValue("SpatialSwitch", false);
                }
            }
            catch (Exception ex) { Log.Write("autostart: " + ex.Message); }
        }

        bool AnyWatchedRunning()
        {
            if (s.WatchedProcesses.Count == 0) return false;
            var want = new HashSet<string>(s.WatchedProcesses.Select(w => w.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? w.Substring(0, w.Length - 4) : w), StringComparer.OrdinalIgnoreCase);
            foreach (var p in Process.GetProcesses()) { try { if (want.Contains(p.ProcessName)) return true; } catch { } finally { p.Dispose(); } }
            return false;
        }

        void Poll()
        {
            try { PollCore(); }
            catch (Exception ex) { Log.Write("poll error: " + ex.Message); }
        }

        void PollCore()
        {
            if (!s.WatchingEnabled || !Svv.Available || string.IsNullOrEmpty(s.DeviceId)) return;
            bool running = AnyWatchedRunning();
            if (running && !overrideActive)
            {
                Log.Write("watched program detected -> applying active format " + Formats.Label(s.ActiveFormat));
                Apply(s.ActiveFormat);
                overrideActive = true;      // edge-triggered: one attempt per transition, never a retry storm
                UpdateStatusText(true);
            }
            else if (!running && overrideActive)
            {
                Log.Write("watched programs gone -> applying restore format " + Formats.Label(s.RestoreFormat));
                Apply(s.RestoreFormat);
                overrideActive = false;
                UpdateStatusText(false);
            }
        }

        void RefreshStatus(bool queryDevice)
        {
            if (queryDevice) { var cur = CurrentFormat(); lastKnown = cur == null ? "device not found" : Formats.Label(cur); }
            UpdateStatusText(AnyWatchedRunning());
        }

        void UpdateStatusText(bool running)
        {
            lblStatus.Text = "Now: " + lastKnown + (lastApplyFailed ? "  (last switch did not take effect — see log)" : "") + "   |   watched running: " + (running ? "yes" : "no") + "   |   override: " + (overrideActive ? "active" : "none");
            string t = "SpatialSwitch — " + lastKnown; tray.Text = t.Length > 63 ? "SpatialSwitch" : t;
        }
    }

    public static class Program
    {
        /// <summary>Breadcrumb in %TEMP% written before anything else, so a start that never reaches the log is still visible.</summary>
        public static void Breadcrumb(string what)
        {
            try { string p = Path.Combine(Path.GetTempPath(), "spatialswitch-start.txt"); Log.Trim(p, 64 * 1024); File.AppendAllText(p, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  pid " + Process.GetCurrentProcess().Id + "  " + what + Environment.NewLine); } catch { }
        }

        [STAThread]   // keep directly on Main: WinForms dialogs and COM need the UI thread to be STA
        static void Main(string[] args)
        {
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i].Equals("--settings", StringComparison.OrdinalIgnoreCase))
                {
                    // absolute, so the autostart entry and a later working directory resolve to the same folder
                    try { Settings.DirOverride = Path.GetFullPath(args[i + 1]); } catch { Settings.DirOverride = args[i + 1]; }
                }
            Breadcrumb("main: args='" + string.Join(" ", args) + "' dir=" + Settings.Dir + " settingsFile=" + (File.Exists(Settings.FilePath) ? "found" : "MISSING") + " cwd=" + Environment.CurrentDirectory);
            bool created;
            using (var mx = new Mutex(true, "SpatialSwitch_SingleInstance", out created))
            {
                Breadcrumb("single-instance mutex created=" + created);
                if (!created)
                {
                    // second launch (e.g. a shortcut) just asks the running instance to show its window
                    try { EventWaitHandle.OpenExisting("SpatialSwitch_Show").Set(); } catch { }
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Log.Write("start v" + Assembly.GetExecutingAssembly().GetName().Version + " " + string.Join(" ", args));
                Log.Write("settings dir: " + Settings.Dir + (Settings.DirUnresolved ? "  (WARNING: settings folder could not be resolved - using the folder next to the executable)" : ""));
                Application.Run(new MainForm(args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase))));
                Log.Write("stop");
            }
        }
    }
}
