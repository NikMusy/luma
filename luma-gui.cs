using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

static class Program
{
    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }
}

class MainForm : Form
{
    const string SteamDir = @"C:\Program Files (x86)\Steam";

    static readonly Color Bg = Color.FromArgb(24, 26, 32);
    static readonly Color OnColor = Color.FromArgb(46, 160, 90);
    static readonly Color OffColor = Color.FromArgb(90, 94, 105);
    static readonly Color Busy = Color.FromArgb(200, 150, 40);

    Button toggle;
    Label status, hint;
    CheckBox launch;
    TextBox log;
    bool busy;

    public MainForm()
    {
        Text = "luma";
        ClientSize = new Size(340, 470);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Bg;
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 9f);

        status = new Label
        {
            Text = "", Font = new Font("Segoe UI", 15f, FontStyle.Bold),
            AutoSize = false, TextAlign = ContentAlignment.MiddleCenter,
            Bounds = new Rectangle(0, 16, 340, 40)
        };
        hint = new Label
        {
            Text = "", ForeColor = Color.FromArgb(150, 155, 165),
            AutoSize = false, TextAlign = ContentAlignment.MiddleCenter,
            Bounds = new Rectangle(10, 56, 320, 22)
        };

        toggle = new Button
        {
            Bounds = new Rectangle(80, 92, 180, 180),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 20f, FontStyle.Bold),
            ForeColor = Color.White,
            Cursor = Cursors.Hand
        };
        toggle.FlatAppearance.BorderSize = 0;
        toggle.FlatAppearance.MouseOverBackColor = Color.Empty;
        toggle.FlatAppearance.MouseDownBackColor = Color.Empty;
        toggle.Click += OnToggle;
        var path = new GraphicsPath();
        path.AddEllipse(0, 0, 180, 180);
        toggle.Region = new Region(path);

        launch = new CheckBox
        {
            Text = "Запустить Steam после переключения", Checked = true,
            AutoSize = false, Bounds = new Rectangle(40, 288, 270, 24),
            ForeColor = Color.FromArgb(200, 204, 212)
        };

        log = new TextBox
        {
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            Bounds = new Rectangle(16, 322, 308, 130),
            BackColor = Color.FromArgb(14, 15, 19), ForeColor = Color.FromArgb(170, 175, 185),
            BorderStyle = BorderStyle.None, Font = new Font("Consolas", 8.5f)
        };

        Controls.AddRange(new Control[] { status, hint, toggle, launch, log });
        Refresh_();
    }

    static bool IsOn()
    {
        return File.Exists(Path.Combine(SteamDir, "DLLInjector.exe"));
    }

    void Refresh_()
    {
        bool on = IsOn();
        status.Text = on ? "GreenLuma включён" : "GreenLuma выключен";
        status.ForeColor = on ? OnColor : Color.FromArgb(190, 194, 204);
        hint.Text = on ? "Steam запускай через инжектор (авто)" : "Dota 2 запустится без ошибки проверки";
        toggle.Text = on ? "ВКЛ" : "ВЫКЛ";
        toggle.BackColor = on ? OnColor : OffColor;
        toggle.Enabled = true;
    }

    void Append(string s)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(new Action<string>(Append), s); return; }
        log.AppendText(s + Environment.NewLine);
    }

    static string ExtractScript()
    {
        string dir = Path.Combine(Path.GetTempPath(), "luma-gui");
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, "luma.ps1");
        using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("luma.ps1"))
        using (var f = File.Create(file))
            s.CopyTo(f);
        return file;
    }

    async void OnToggle(object sender, EventArgs e)
    {
        if (busy) return;
        bool wasOn = IsOn();
        string cmd = wasOn ? "off" : "on";

        if (Process.GetProcessesByName("dota2").Length > 0)
        {
            var r = MessageBox.Show(
                "Dota 2 сейчас запущена. Steam и Dota будут закрыты.\nЕсли ты в матче — получишь LP.\n\nПродолжить?",
                "luma", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (r != DialogResult.Yes) return;
        }

        busy = true;
        toggle.Enabled = false;
        toggle.BackColor = Busy;
        toggle.Text = "...";
        status.Text = wasOn ? "Выключаю..." : "Включаю...";
        status.ForeColor = Busy;
        log.Clear();

        int code = await Task.Run(() => RunScript(cmd));

        busy = false;
        Refresh_();
        bool nowOn = IsOn();
        if (code != 0 || nowOn == wasOn)
        {
            Append("Не получилось - смотри лог выше.");
            return;
        }
        if (launch.Checked)
        {
            try
            {
                string exe = Path.Combine(SteamDir, nowOn ? "DLLInjector.exe" : "steam.exe");
                Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = SteamDir, UseShellExecute = true });
                Append("Steam запущен.");
            }
            catch (Exception ex) { Append("Запуск Steam не удался: " + ex.Message); }
        }
    }

    int RunScript(string cmd)
    {
        try
        {
            string script = ExtractScript();
            var psi = new ProcessStartInfo("powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\" " + cmd)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            using (var p = Process.Start(psi))
            {
                p.OutputDataReceived += (s, a) => { if (a.Data != null) Append(a.Data); };
                p.ErrorDataReceived += (s, a) => { if (a.Data != null) Append(a.Data); };
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                p.WaitForExit();
                return p.ExitCode;
            }
        }
        catch (Exception ex)
        {
            Append("Ошибка: " + ex.Message);
            return 1;
        }
    }
}
