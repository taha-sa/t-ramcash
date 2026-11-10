using TSuite.Design;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using TSuite.Diagnostics;

namespace TRamCash;

public class SettingsForm : Form
{
    [DllImport("Gdi32.dll", EntryPoint = "CreateRoundRectRgn")]
    static extern IntPtr CreateRoundRectRgn(
        int nLeftRect, int nTopRect, int nRightRect, int nBottomRect,
        int nWidthEllipse, int nHeightEllipse);

    [DllImport("gdi32.dll")]
    static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll")]
    static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

    const int WM_NCLBUTTONDOWN = 0xA1;
    const int HT_CAPTION = 0x2;

    readonly AppSettings _settings;

    static readonly Color BgDark = TSuiteTokens.ColorNeutralDarkBg;
    static readonly Color CardBg = TSuiteTokens.ColorNeutralDarkSurface1;
    static readonly Color Accent = TSuiteTokens.ColorAccentTRamCashDarkBase;
    static readonly Color TextWhite = TSuiteTokens.ColorNeutralDarkText;
    static readonly Color TextMuted = TSuiteTokens.ColorNeutralDarkTextMuted;

    CheckBox chkAlwaysOnTop;
    CheckBox chkStartWithWindows;
    CheckBox chkStartMinimized;
    CheckBox chkShowNotification;

    CheckBox chkWorkingSets;
    CheckBox chkSystemWorkingSet;
    CheckBox chkModifiedList;
    CheckBox chkStandbyList;
    CheckBox chkStandbyLowPriority;

    CheckBox chkAutoRatio;
    NumericUpDown numAutoRatio;
    CheckBox chkAutoInterval;
    NumericUpDown numAutoInterval;

    ListBox lstExcluded;

    CheckBox chkAutoNotification;
    CheckBox chkCrashReports;
    readonly Action _checkUpdates;

    public SettingsForm(AppSettings settings, Action checkUpdates = null)
    {
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        _settings = settings;
        _checkUpdates = checkUpdates;
        KeyPreview = true;
        Gdi.Acquire();

        Text = "Ayarlar";
        Size = new Size(420, 500);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        BackColor = BgDark;
        ForeColor = TextWhite;

        ApplyRoundedRegion();

        BuildUI();
    }

    void ApplyRoundedRegion()
    {
        int rr = (int)Math.Round(20 * DeviceDpi / 96f);
        IntPtr ptr = CreateRoundRectRgn(0, 0, Width + 1, Height + 1, rr, rr);
        var old = Region;
        Region = Region.FromHrgn(ptr);
        old?.Dispose();
        DeleteObject(ptr);
    }

    // DPI olcekleme sonrasi boyut degisir: yuvarlak bolge yeniden hesaplanir.
    protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); ApplyRoundedRegion(); }

    void BuildUI()
    {
        // Header
        var pnlHeader = new Panel { Dock = DockStyle.Top, Height = 45 };
        pnlHeader.MouseDown += (s, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
            }
        };

        var lblTitle = new Label
        {
            Text = "AYARLAR & TERCİHLER",
            Font = Gdi.GetFont("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = Accent,
            Location = new Point(20, 15),
            AutoSize = true
        };
        pnlHeader.Controls.Add(lblTitle);

        var btnClose = new Label
        {
            Text = "✕",
            Font = Gdi.GetFont("Segoe UI", 10F, FontStyle.Bold),
            ForeColor = TextMuted,
            Location = new Point(385, 12),
            Cursor = Cursors.Hand,
            AutoSize = true
        };
        btnClose.Click += (s, e) => Close();
        pnlHeader.Controls.Add(btnClose);
        Controls.Add(pnlHeader);

        // TabControl
        var tabControl = new TabControl
        {
            Location = new Point(20, 50),
            Size = new Size(380, 380)
        };

        // Tab 1: Genel
        var tabGeneral = new TabPage("Genel") { BackColor = CardBg };
        chkAlwaysOnTop = AddCheckBox(tabGeneral, "Her zaman en üstte göster", _settings.AlwaysOnTop, 20, 25);
        chkStartWithWindows = AddCheckBox(tabGeneral, "Windows ile başlat", _settings.StartWithWindows, 20, 60);
        chkStartMinimized = AddCheckBox(tabGeneral, "Sistem tepsisinde küçültülmüş başlat", _settings.StartMinimized, 20, 95);
        chkShowNotification = AddCheckBox(tabGeneral, "Temizlik sonuç bildirim balonunu göster", _settings.ShowCleanNotification, 20, 130);
        chkAutoNotification = AddCheckBox(tabGeneral, "Otomatik temizlikte de bildirim göster (varsayılan sessiz)", _settings.AutoCleanNotification, 20, 165);
        chkCrashReports = AddCheckBox(tabGeneral, "Hata raporlarını otomatik hazırla", CrashReporter.AutomaticallyPrepareReports, 20, 200);
        var btnReport = MakeGhostButton("Hata raporu gönder…", new Rectangle(20, 275, 190, 32));
        btnReport.Click += (s, e) => ManualReport.Show(this);
        tabGeneral.Controls.Add(btnReport);
        if (_checkUpdates != null)
        {
            var btnUpd = MakeGhostButton("Güncellemeleri denetle", new Rectangle(20, 235, 190, 32));
            btnUpd.Click += (s, e) => _checkUpdates();
            tabGeneral.Controls.Add(btnUpd);
        }

        // Tab 2: Bellek Temizleme
        var tabClean = new TabPage("Temizleme Alanları") { BackColor = CardBg };
        chkWorkingSets = AddCheckBox(tabClean, "Süreç çalışma kümeleri (Working Sets)", _settings.CleanProcessWorkingSets, 20, 20);
        chkSystemWorkingSet = AddCheckBox(tabClean, "Sistem çalışma kümesi (kaldırıldı: dışlamaları yok sayıyordu)", _settings.CleanSystemWorkingSet, 20, 50);
        chkSystemWorkingSet.Enabled = false; chkSystemWorkingSet.Visible = false;
        chkModifiedList = AddCheckBox(tabClean, "Değiştirilmiş sayfa listesi (Modified List)", _settings.CleanModifiedPageList, 20, 80);
        chkStandbyList = AddCheckBox(tabClean, "Bekleme listesi (Standby List / RAM Cache)", _settings.CleanStandbyList, 20, 110);
        chkStandbyLowPriority = AddCheckBox(tabClean, "Düşük öncelikli bekleme listesi", _settings.CleanStandbyLowPriority, 20, 140);

        var lblDivider = new Label
        {
            Text = "Otomasyon Kuralları:",
            Font = Gdi.GetFont("Segoe UI", 8F, FontStyle.Bold),
            ForeColor = Accent,
            Location = new Point(20, 180),
            AutoSize = true
        };
        tabClean.Controls.Add(lblDivider);

        chkAutoRatio = AddCheckBox(tabClean, "Kullanım şu oranı aşınca temizle (%):", _settings.AutoCleanByRatio, 20, 210);
        chkAutoRatio.Width = 240;
        numAutoRatio = new NumericUpDown
        {
            Location = new Point(270, 208),
            Width = 60,
            Minimum = 10,
            Maximum = 99,
            Value = _settings.AutoCleanRatioThreshold,
            BackColor = TSuiteTokens.ColorNeutralDarkSurface3,
            ForeColor = TSuiteTokens.ColorNeutralDarkText
        };
        tabClean.Controls.Add(numAutoRatio);

        chkAutoInterval = AddCheckBox(tabClean, "Belirli aralıklarla temizle (dakika):", _settings.AutoCleanByInterval, 20, 250);
        chkAutoInterval.Width = 240;
        numAutoInterval = new NumericUpDown
        {
            Location = new Point(270, 248),
            Width = 60,
            Minimum = 1,
            Maximum = 360,
            Value = _settings.AutoCleanIntervalMinutes,
            BackColor = TSuiteTokens.ColorNeutralDarkSurface3,
            ForeColor = TSuiteTokens.ColorNeutralDarkText
        };
        tabClean.Controls.Add(numAutoInterval);

        // Otomatik temizleme modu (sabit: Akıllı)
        tabClean.Controls.Add(new Label
        {
            Text = "Otomatik temizleme modu: Akıllı",
            Font = Gdi.GetFont("Segoe UI", 8.5F, FontStyle.Bold),
            ForeColor = Accent,
            Location = new Point(20, 296),
            AutoSize = true
        });
        tabClean.Controls.Add(new Label
        {
            Text = "Otomatik temizlik her zaman Akıllı modda çalışır: ön plan uygulaması korunur, değiştirilmiş sayfalar ve dosya önbelleğine dokunulmaz. Tam mod yalnızca elle çalıştırılır.",
            Font = Gdi.GetFont("Segoe UI", 8F),
            ForeColor = TextMuted,
            Location = new Point(20, 316),
            Size = new Size(340, 48)
        });

        // Tab 4: Hariç tutulanlar
        var tabExc = new TabPage("Hariç Tutulanlar") { BackColor = CardBg };
        tabExc.Controls.Add(new Label
        {
            Text = "Bu süreçlerin belleği temizlenmez. Ön plandaki uygulama, T-RamCash ve diğer T-Suite uygulamaları her zaman hariçtir. Tam modda 'Sistem çalışma kümesi' adımı bu listeyi yok sayar.",
            Font = Gdi.GetFont("Segoe UI", 8F),
            ForeColor = TextMuted,
            Location = new Point(16, 16),
            Size = new Size(340, 40)
        });
        lstExcluded = new ListBox
        {
            Location = new Point(16, 64),
            Size = new Size(340, 200),
            BackColor = TSuiteTokens.ColorNeutralDarkSurface1,
            ForeColor = TextWhite,
            BorderStyle = BorderStyle.None,
            IntegralHeight = false,
            Font = Gdi.GetFont("Segoe UI", 9F)
        };
        foreach (var n in _settings.ExcludedProcesses ?? new List<string>()) lstExcluded.Items.Add(n);
        tabExc.Controls.Add(lstExcluded);

        var btnAdd = MakeGhostButton("Çalışan süreçlerden ekle…", new Rectangle(16, 272, 208, 32));
        btnAdd.Click += (s, e) => AddFromRunning();
        var btnRemove = MakeGhostButton("Kaldır", new Rectangle(232, 272, 124, 32));
        btnRemove.Click += (s, e) => { if (lstExcluded.SelectedIndex >= 0) lstExcluded.Items.RemoveAt(lstExcluded.SelectedIndex); };
        tabExc.Controls.Add(btnAdd);
        tabExc.Controls.Add(btnRemove);

        tabControl.TabPages.Add(tabGeneral);
        tabControl.TabPages.Add(tabClean);
        tabControl.TabPages.Add(tabExc);
        Controls.Add(tabControl);

        // Kaydet Butonu
        var btnSave = new Button
        {
            Text = "KAYDET VE KAPAT",
            DialogResult = DialogResult.OK,
            Location = new Point(20, 442),
            Size = new Size(380, 38),
            BackColor = Accent,
            ForeColor = TSuiteTokens.ColorAccentTRamCashDarkOnAccent,
            Font = Gdi.GetFont("Segoe UI", 9F, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        btnSave.FlatAppearance.BorderSize = 0;
        btnSave.FlatAppearance.MouseOverBackColor = TSuiteTokens.ColorAccentTRamCashDarkHover;
        btnSave.FlatAppearance.MouseDownBackColor = TSuiteTokens.ColorAccentTRamCashDarkPressed;
        btnSave.Click += (s, e) => SaveChanges();
        Controls.Add(btnSave);
        AcceptButton = btnSave;
    }

    static Button MakeGhostButton(string text, Rectangle r)
    {
        var b = new Button
        {
            Text = text,
            Bounds = r,
            BackColor = CardBg,
            ForeColor = Accent,
            Font = Gdi.GetFont("Segoe UI", 8.5F, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        b.FlatAppearance.BorderColor = TSuiteTokens.ColorNeutralDarkBorder;
        b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.MouseOverBackColor = TSuiteTokens.ColorNeutralDarkSurface2;
        return b;
    }

    void AddFromRunning()
    {
        var existing = new HashSet<string>(lstExcluded.Items.Cast<string>(), StringComparer.OrdinalIgnoreCase);
        using var dlg = new ProcessPickerForm(existing);
        if (dlg.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(dlg.Selected))
            lstExcluded.Items.Add(dlg.Selected);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) Gdi.Release();
    }

    CheckBox AddCheckBox(Control parent, string text, bool isChecked, int x, int y)
    {
        var chk = new CheckBox
        {
            Text = text,
            Checked = isChecked,
            Location = new Point(x, y),
            AutoSize = true,
            Font = Gdi.GetFont("Segoe UI", 8.5F),
            ForeColor = TextWhite
        };
        parent.Controls.Add(chk);
        return chk;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { Close(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    void SaveChanges()
    {
        CrashReporter.AutomaticallyPrepareReports = chkCrashReports.Checked;
        _settings.AlwaysOnTop = chkAlwaysOnTop.Checked;
        _settings.StartWithWindows = chkStartWithWindows.Checked;
        _settings.StartMinimized = chkStartMinimized.Checked;
        _settings.ShowCleanNotification = chkShowNotification.Checked;

        _settings.CleanProcessWorkingSets = chkWorkingSets.Checked;
        _settings.CleanSystemWorkingSet = chkSystemWorkingSet.Checked;
        _settings.CleanModifiedPageList = chkModifiedList.Checked;
        _settings.CleanStandbyList = chkStandbyList.Checked;
        _settings.CleanStandbyLowPriority = chkStandbyLowPriority.Checked;

        _settings.AutoCleanByRatio = chkAutoRatio.Checked;
        _settings.AutoCleanRatioThreshold = (int)numAutoRatio.Value;
        _settings.AutoCleanByInterval = chkAutoInterval.Checked;
        _settings.AutoCleanIntervalMinutes = (int)numAutoInterval.Value;

        _settings.ExcludedProcesses = lstExcluded.Items.Cast<string>().Select(MemoryCleaner.NormalizeName)
            .Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        _settings.AutoCleanNotification = chkAutoNotification.Checked;

        MemoryCleaner.InvalidateExcludeCache();
        _settings.Save();
    }
}

/// <summary>Calisan sureclerden birini secmek icin kucuk, ayni temada diyalog.</summary>
public class ProcessPickerForm : Form
{
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

    static readonly Color BgDark = TSuiteTokens.ColorNeutralDarkBg;
    static readonly Color Accent = TSuiteTokens.ColorAccentTRamCashDarkBase;
    static readonly Color TextWhite = TSuiteTokens.ColorNeutralDarkText;

    readonly ListBox _list;
    public string Selected { get; private set; }

    public ProcessPickerForm(HashSet<string> exclude)
    {
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Gdi.Acquire();
        Text = "Süreç seç";
        Size = new Size(340, 440);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        BackColor = BgDark;
        ForeColor = TextWhite;

        var header = new Label
        {
            Text = "ÇALIŞAN SÜREÇLER",
            Font = Gdi.GetFont("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = Accent,
            Dock = DockStyle.Top,
            Height = 40,
            Padding = new Padding(16, 14, 0, 0)
        };
        header.MouseDown += (s, e) => { ReleaseCapture(); SendMessage(Handle, 0xA1, 0x2, 0); };
        Controls.Add(header);

        _list = new ListBox
        {
            Location = new Point(16, 48),
            Size = new Size(308, 304),
            BackColor = TSuiteTokens.ColorNeutralDarkSurface1,
            ForeColor = TextWhite,
            BorderStyle = BorderStyle.None,
            IntegralHeight = false,
            Font = Gdi.GetFont("Segoe UI", 9F)
        };
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            try { if (p.Id > 4) names.Add(p.ProcessName); } catch { } finally { p.Dispose(); }
        }
        foreach (var n in names) if (!exclude.Contains(n)) _list.Items.Add(n);
        _list.DoubleClick += (s, e) => Accept();
        Controls.Add(_list);

        var ok = new Button
        {
            Text = "EKLE", Bounds = new Rectangle(16, 368, 196, 40), BackColor = Accent, ForeColor = TSuiteTokens.ColorAccentTRamCashDarkOnAccent,
            Font = Gdi.GetFont("Segoe UI", 9F, FontStyle.Bold), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand
        };
        ok.FlatAppearance.BorderSize = 0;
        ok.FlatAppearance.MouseOverBackColor = TSuiteTokens.ColorAccentTRamCashDarkHover;
        ok.FlatAppearance.MouseDownBackColor = TSuiteTokens.ColorAccentTRamCashDarkPressed;
        ok.Click += (s, e) => Accept();
        var cancel = new Button
        {
            Text = "Vazgeç", Bounds = new Rectangle(220, 368, 104, 40), BackColor = TSuiteTokens.ColorNeutralDarkSurface1, ForeColor = Accent,
            Font = Gdi.GetFont("Segoe UI", 9F, FontStyle.Bold), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
            DialogResult = DialogResult.Cancel
        };
        cancel.FlatAppearance.BorderColor = TSuiteTokens.ColorNeutralDarkBorder;
        Controls.Add(ok);
        Controls.Add(cancel);
        CancelButton = cancel;
    }

    void Accept()
    {
        if (_list.SelectedItem is string s) { Selected = s; DialogResult = DialogResult.OK; }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) Gdi.Release();
    }
}
