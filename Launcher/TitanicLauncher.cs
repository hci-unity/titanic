// Titanic Launcher: a window next to Titanic.exe that edits settings.txt (the file the game reads
// at startup, see Assets/Game/Scripts/GameSettings.cs) and starts the game.
// Build: Launcher\build.cmd (uses the C# compiler that ships with Windows, no install needed).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

class TitanicLauncher : Form
{
    static readonly Color Gold = Color.FromArgb(212, 175, 55);
    static readonly Color Ink = Color.FromArgb(14, 18, 28);
    static readonly Color Panel = Color.FromArgb(28, 34, 48);

    readonly string dir = AppDomain.CurrentDomain.BaseDirectory;
    string SettingsPath { get { return Path.Combine(dir, "settings.txt"); } }
    string GamePath { get { return Path.Combine(dir, "Titanic.exe"); } }

    readonly Image background;
    readonly float s; // DPI scale
    readonly Dictionary<string, List<Button>> groups = new Dictionary<string, List<Button>>();
    readonly Dictionary<string, string> chosen = new Dictionary<string, string>();
    readonly NumericUpDown minutes;

    int S(float v) { return (int)Math.Round(v * s); }

    TitanicLauncher()
    {
        using (var g = CreateGraphics()) s = g.DpiX / 96f;
        Text = "Titanic";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(S(960), S(580));
        BackColor = Ink;
        DoubleBuffered = true;
        background = Image.FromStream(typeof(TitanicLauncher).Assembly.GetManifestResourceStream("background.png"));
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        Row("mode", "EXPERIENCE", 300, new[] { "vr", "VR Headset" }, new[] { "pc", "PC  ·  Keyboard & Mouse" });
        Row("character", "CHARACTER", 360, new[] { "jack", "Jack" }, new[] { "rose", "Rose" });
        Row("length", "LENGTH", 420, new[] { "short", "Short  ·  1.5 min" }, new[] { "mid", "Mid  ·  3 min" },
            new[] { "long", "Long  ·  5 min" }, new[] { "custom", "Custom" });

        minutes = new NumericUpDown
        {
            Minimum = 1, Maximum = 60, DecimalPlaces = 0, Value = 4,
            Font = new Font("Segoe UI", 11f), BackColor = Panel, ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle, TextAlign = HorizontalAlignment.Center,
            Bounds = new Rectangle(S(818), S(424), S(60), S(30)),
        };
        minutes.ValueChanged += (o, e) => Pick("length", "custom");
        Controls.Add(minutes);
        Controls.Add(Caption("min", 884, 428, 40));

        var save = ActionButton("Save", false, 250);
        var run = ActionButton("Run", false, 420);
        var both = ActionButton("Save && Run", true, 590);
        save.Click += (o, e) => { if (Save()) Close(); };
        run.Click += (o, e) => { if (Run()) Close(); };
        both.Click += (o, e) => { if (Save() && Run()) Close(); };

        Load();
    }

    // ---------- settings.txt ----------

    new void Load()
    {
        var values = File.Exists(SettingsPath) ? File.ReadAllLines(SettingsPath)
            .Select(l => l.Split('#')[0].Split('='))
            .Where(kv => kv.Length == 2)
            .GroupBy(kv => kv[0].Trim().ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.Last()[1].Trim().ToLowerInvariant())
            : new Dictionary<string, string>();
        string v;
        Pick("mode", values.TryGetValue("mode", out v) && v == "pc" ? "pc" : "vr");
        Pick("character", values.TryGetValue("character", out v) && (v == "rose" || v == "female") ? "rose" : "jack");
        decimal m;
        if (!values.TryGetValue("length", out v)) v = "mid";
        if (decimal.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out m))
        {
            minutes.Value = Math.Max(minutes.Minimum, Math.Min(minutes.Maximum, Math.Round(m)));
            v = "custom";
        }
        Pick("length", groups["length"].Any(b => (string)b.Tag == v) ? v : "mid");
    }

    string Length
    {
        get
        {
            return chosen["length"] == "custom"
                ? ((int)minutes.Value).ToString(CultureInfo.InvariantCulture) : chosen["length"];
        }
    }

    bool Save()
    {
        string length = Length;
        try
        {
            File.WriteAllText(SettingsPath,
                "# Titanic VR settings. Edit, save, then start the .exe (or use Titanic Launcher.exe).\n\n" +
                "# vr (Quest headset + controllers) or pc (keyboard + mouse; the mirror shows a stand-in, not Jack/Rose)\n" +
                "mode = " + chosen["mode"] + "\n\n" +
                "# jack or rose\n" +
                "character = " + chosen["character"] + "\n\n" +
                "# short (~1.5 min), mid (3 min), long (5 min), or a number of minutes, e.g. 4\n" +
                "length = " + length + "\n");
        }
        catch (Exception e)
        {
            MessageBox.Show(this, "Could not save settings.txt:\n" + e.Message, "Titanic");
            return false;
        }
        return true;
    }

    bool Run()
    {
        if (!File.Exists(GamePath))
        {
            MessageBox.Show(this, "Titanic.exe was not found next to the launcher:\n" + dir, "Titanic");
            return false;
        }
        // The menu's current choices go in as args (they override settings.txt, which stays untouched).
        string args = "mode=" + chosen["mode"] + " character=" + chosen["character"] + " length=" + Length;
        Process.Start(new ProcessStartInfo(GamePath, args) { WorkingDirectory = dir });
        return true;
    }

    // ---------- UI pieces ----------

    void Row(string key, string caption, int y, params string[][] options)
    {
        Controls.Add(Caption(caption, 64, y + 7, 170));
        groups[key] = new List<Button>();
        int x = 250;
        foreach (var o in options)
        {
            int w = key == "length" ? (o[0] == "custom" ? 100 : 140) : o[1].Length > 12 ? 230 : 140;
            var b = new Button
            {
                Text = o[1], Tag = o[0], FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, UseMnemonic = false,
                Font = new Font("Segoe UI", 10.5f), Bounds = new Rectangle(S(x), S(y), S(w), S(38)),
            };
            b.FlatAppearance.BorderColor = Gold;
            b.FlatAppearance.BorderSize = 1;
            string value = o[0];
            b.Click += (sender, e) => Pick(key, value);
            groups[key].Add(b);
            Controls.Add(b);
            x += w + 12;
        }
    }

    void Pick(string key, string value)
    {
        chosen[key] = value;
        foreach (var b in groups[key])
        {
            bool on = (string)b.Tag == value;
            b.BackColor = on ? Gold : Panel;
            b.ForeColor = on ? Ink : Color.FromArgb(230, 225, 210);
            b.FlatAppearance.MouseOverBackColor = on ? Gold : Color.FromArgb(48, 56, 76);
        }
    }

    Button ActionButton(string text, bool primary, int x)
    {
        var b = new Button
        {
            Text = text, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
            Font = new Font("Segoe UI Semibold", 11.5f),
            Bounds = new Rectangle(S(x), S(494), S(primary ? 220 : 158), S(46)),
            BackColor = primary ? Gold : Panel, ForeColor = primary ? Ink : Color.White,
        };
        b.FlatAppearance.BorderColor = Gold;
        b.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(232, 198, 90) : Color.FromArgb(48, 56, 76);
        Controls.Add(b);
        return b;
    }

    Label Caption(string text, int x, int y, int w)
    {
        return new Label
        {
            Text = text, ForeColor = Gold, BackColor = Color.Transparent, AutoSize = false,
            Font = new Font("Segoe UI Semibold", 9.5f), Bounds = new Rectangle(S(x), S(y), S(w), S(26)),
        };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        var r = ClientRectangle;

        // Staircase photo, cropped to fill, darkening toward the bottom for the controls.
        float scale = Math.Max(r.Width / (float)background.Width, r.Height / (float)background.Height);
        float w = background.Width * scale, h = background.Height * scale;
        g.DrawImage(background, (r.Width - w) / 2, (r.Height - h) / 2 - S(40), w, h);
        using (var shade = new LinearGradientBrush(r, Color.FromArgb(90, Ink), Color.FromArgb(250, Ink), 90f))
        {
            shade.InterpolationColors = new ColorBlend
            {
                Colors = new[] { Color.FromArgb(110, Ink), Color.FromArgb(60, Ink), Color.FromArgb(225, Ink), Color.FromArgb(248, Ink) },
                Positions = new[] { 0f, 0.3f, 0.47f, 1f },
            };
            g.FillRectangle(shade, r);
        }

        // Title: letter-spaced serif with a soft shadow, gold rules either side of the subtitle.
        using (var title = new Font("Georgia", 54f, FontStyle.Bold))
        using (var sub = new Font("Georgia", 11.5f, FontStyle.Italic))
        using (var fmt = new StringFormat { Alignment = StringAlignment.Center })
        using (var gold = new SolidBrush(Gold))
        using (var shadow = new SolidBrush(Color.FromArgb(170, 0, 0, 0)))
        using (var pen = new Pen(Color.FromArgb(180, Gold), Math.Max(1f, s)))
        {
            const string name = "T I T A N I C";
            g.DrawString(name, title, shadow, new RectangleF(S(3), S(73), r.Width, S(100)), fmt);
            g.DrawString(name, title, gold, new RectangleF(0, S(70), r.Width, S(100)), fmt);
            const string tagline = "The Grand Staircase  ·  April 14, 1912";
            g.DrawString(tagline, sub, Brushes.White, new RectangleF(0, S(170), r.Width, S(30)), fmt);
            float tw = g.MeasureString(tagline, sub).Width / 2 + S(18), cx = r.Width / 2f, cy = S(181);
            g.DrawLine(pen, cx - tw - S(120), cy, cx - tw, cy);
            g.DrawLine(pen, cx + tw, cy, cx + tw + S(120), cy);
        }
    }

    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

    [STAThread]
    static void Main()
    {
        SetProcessDPIAware();
        Application.EnableVisualStyles();
        Application.Run(new TitanicLauncher());
    }
}
