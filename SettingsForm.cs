using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

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

    static readonly Color BgDark = Color.FromArgb(16, 16, 20);
    static readonly Color CardBg = Color.FromArgb(24, 24, 30);
    static readonly Color DarkGold = Color.FromArgb(212, 160, 23);
    static readonly Color TextWhite = Color.FromArgb(245, 245, 247);
    static readonly Color TextMuted = Color.FromArgb(150, 150, 160);

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

    NumericUpDown numWarning;
    NumericUpDown numDanger;

    public SettingsForm(AppSettings settings)
    {
        _settings = settings;
        Gdi.Acquire();

        Text = "Ayarlar";
        Size = new Size(420, 500);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        BackColor = BgDark;
        ForeColor = TextWhite;

        IntPtr ptr = CreateRoundRectRgn(0, 0, Width, Height, 20, 20);
        Region = Region.FromHrgn(ptr);
        DeleteObject(ptr);

        BuildUI();
    }

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
            ForeColor = DarkGold,
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
        chkStartWithWindows = AddCheckBox(tabGeneral, "Windows başlangıcında otomatik başlat", _settings.StartWithWindows, 20, 60);
        chkStartMinimized = AddCheckBox(tabGeneral, "Sistem tepsisinde küçültülmüş başlat", _settings.StartMinimized, 20, 95);
        chkShowNotification = AddCheckBox(tabGeneral, "Temizlik sonuç bildirim balonunu göster", _settings.ShowCleanNotification, 20, 130);

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
            ForeColor = DarkGold,
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
            BackColor = Color.FromArgb(34, 34, 40),
            ForeColor = Color.White
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
            BackColor = Color.FromArgb(34, 34, 40),
            ForeColor = Color.White
        };
        tabClean.Controls.Add(numAutoInterval);

        // Tab 3: Eşik Seviyeleri
        var tabLevels = new TabPage("Eşikler") { BackColor = CardBg };
        var lblWarn = new Label
        {
            Text = "Uyarı Seviyesi Eşiği (%):",
            Location = new Point(20, 30),
            AutoSize = true,
            ForeColor = TextWhite
        };
        numWarning = new NumericUpDown
        {
            Location = new Point(220, 28),
            Width = 60,
            Minimum = 10,
            Maximum = 95,
            Value = _settings.WarningLevel,
            BackColor = Color.FromArgb(34, 34, 40),
            ForeColor = Color.White
        };
        tabLevels.Controls.Add(lblWarn);
        tabLevels.Controls.Add(numWarning);

        var lblDanger = new Label
        {
            Text = "Tehlike Seviyesi Eşiği (%):",
            Location = new Point(20, 75),
            AutoSize = true,
            ForeColor = TextWhite
        };
        numDanger = new NumericUpDown
        {
            Location = new Point(220, 73),
            Width = 60,
            Minimum = 20,
            Maximum = 99,
            Value = _settings.DangerLevel,
            BackColor = Color.FromArgb(34, 34, 40),
            ForeColor = Color.White
        };
        tabLevels.Controls.Add(lblDanger);
        tabLevels.Controls.Add(numDanger);

        // Otomatik temizleme modu (sabit: Akıllı)
        tabClean.Controls.Add(new Label
        {
            Text = "Otomatik temizleme modu: Akıllı",
            Font = Gdi.GetFont("Segoe UI", 8.5F, FontStyle.Bold),
            ForeColor = DarkGold,
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
            BackColor = Color.FromArgb(20, 20, 26),
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
        tabControl.TabPages.Add(tabLevels);
        tabControl.TabPages.Add(tabExc);
        Controls.Add(tabControl);

        // Kaydet Butonu
        var btnSave = new Button
        {
            Text = "KAYDET VE KAPAT",
            DialogResult = DialogResult.OK,
            Location = new Point(20, 442),
            Size = new Size(380, 38),
            BackColor = DarkGold,
            ForeColor = Color.Black,
            Font = Gdi.GetFont("Segoe UI", 9F, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        btnSave.FlatAppearance.BorderSize = 0;
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
            ForeColor = DarkGold,
            Font = Gdi.GetFont("Segoe UI", 8.5F, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        b.FlatAppearance.BorderColor = Color.FromArgb(34, 34, 44);
        b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(28, 28, 36);
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

    void SaveChanges()
    {
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

        _settings.WarningLevel = (int)numWarning.Value;
        _settings.DangerLevel = (int)numDanger.Value;

        MemoryCleaner.InvalidateExcludeCache();
        _settings.Save();
    }
}

/// <summary>Calisan sureclerden birini secmek icin kucuk, ayni temada diyalog.</summary>
public class ProcessPickerForm : Form
{
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

    static readonly Color BgDark = Color.FromArgb(16, 16, 20);
    static readonly Color DarkGold = Color.FromArgb(212, 160, 23);
    static readonly Color TextWhite = Color.FromArgb(245, 245, 247);

    readonly ListBox _list;
    public string Selected { get; private set; }

    public ProcessPickerForm(HashSet<string> exclude)
    {
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
            ForeColor = DarkGold,
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
            BackColor = Color.FromArgb(24, 24, 30),
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
            Text = "EKLE", Bounds = new Rectangle(16, 368, 196, 40), BackColor = DarkGold, ForeColor = Color.Black,
            Font = Gdi.GetFont("Segoe UI", 9F, FontStyle.Bold), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand
        };
        ok.FlatAppearance.BorderSize = 0;
        ok.Click += (s, e) => Accept();
        var cancel = new Button
        {
            Text = "Vazgeç", Bounds = new Rectangle(220, 368, 104, 40), BackColor = Color.FromArgb(24, 24, 30), ForeColor = DarkGold,
            Font = Gdi.GetFont("Segoe UI", 9F, FontStyle.Bold), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
            DialogResult = DialogResult.Cancel
        };
        cancel.FlatAppearance.BorderColor = Color.FromArgb(34, 34, 44);
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
