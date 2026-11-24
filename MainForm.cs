using LibVLCSharp.Shared;
using LibVLCSharp.WinForms;

namespace CtvViewer;

public class MainForm : Form
{
    const int PER = 9; // sayfa başına kamera
    readonly bool isAdmin;
    readonly List<Cam> cams = Store.Load();
    readonly LibVLC vlc = new();

    readonly MediaPlayer[] mps = new MediaPlayer[PER];
    readonly VideoView[] views = new VideoView[PER];
    readonly Label[] titles = new Label[PER];
    readonly TableLayoutPanel grid;
    readonly ListBox list;
    readonly Label pageLbl;

    readonly Panel bigPanel;
    readonly VideoView bigView;
    readonly MediaPlayer bigMp;
    readonly Label bigTitle;

    readonly Button[] pauseBtns = new Button[PER];
    readonly bool[] paused = new bool[PER];
    readonly bool[] closed = new bool[PER];
    Button bigPauseBtn;
    int bigIdx = -1;
    bool bigPaused;

    int page;
    Point dragStart;
    int downIdx = -1;
    bool dragged;

    public MainForm(bool admin)
    {
        isAdmin = admin;
        HookLog();
        Text = "CTV İzleme" + (isAdmin ? " (Admin)" : "") + "  |  design by ahmedc";
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(1000, 600);
        BackColor = Color.Black;
        KeyPreview = true;

        // ---------- üst araç çubuğu ----------
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, BackColor = Color.FromArgb(30, 30, 30), Padding = new Padding(4) };
        pageLbl = new Label { ForeColor = Color.White, Width = 110, Height = 30, TextAlign = ContentAlignment.MiddleCenter };
        bar.Controls.Add(Btn("◀", (s, e) => Go(-1)));
        bar.Controls.Add(pageLbl);
        bar.Controls.Add(Btn("▶", (s, e) => Go(1)));
        bar.Controls.Add(Btn("Excel İçe Aktar", ImportExcel));
        bar.Controls.Add(Btn("Excel Dışa Aktar", ExportExcel));
        bar.Controls.Add(Btn("Örnek Excel", TemplateExcel));
        bar.Controls.Add(Btn("Tümünü Yeniden Bağlan", (s, e) => { if (bigPanel.Visible) BigReconnect(); else LoadPage(); }));
        if (isAdmin) bar.Controls.Add(Btn("Şifre Ayarları", (s, e) => { using var f = new SettingsForm(); f.ShowDialog(this); }));

        // ---------- sağ playlist ----------
        var right = new Panel { Dock = DockStyle.Right, Width = 270, BackColor = Color.FromArgb(40, 40, 40) };
        list = new ListBox
        {
            Dock = DockStyle.Fill, BackColor = Color.FromArgb(40, 40, 40), ForeColor = Color.White,
            BorderStyle = BorderStyle.None, IntegralHeight = false, Font = new Font("Segoe UI", 10f)
        };
        list.MouseDown += (s, e) =>
        {
            downIdx = list.IndexFromPoint(e.Location);
            dragStart = e.Location;
            dragged = false;
            if (downIdx >= 0) list.SelectedIndex = downIdx;
        };
        list.MouseMove += (s, e) =>
        {
            if (e.Button == MouseButtons.Left && downIdx >= 0 && !dragged &&
                (Math.Abs(e.X - dragStart.X) > 6 || Math.Abs(e.Y - dragStart.Y) > 6))
            {
                dragged = true;
                list.DoDragDrop(downIdx.ToString(), DragDropEffects.Move);
            }
        };
        list.MouseUp += (s, e) =>
        {
            // tek tık = büyük ekranda aç
            if (!dragged && downIdx >= 0 && downIdx == list.IndexFromPoint(e.Location)) OpenBig(downIdx);
        };

        var btns = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 78, Padding = new Padding(4) };
        btns.Controls.Add(Btn("Yeni", (s, e) => NewCam()));
        btns.Controls.Add(Btn("Düzenle", (s, e) => EditCam()));
        btns.Controls.Add(Btn("Sil", (s, e) => DelCam()));
        btns.Controls.Add(Btn("▲", (s, e) => MoveSel(-1)));
        btns.Controls.Add(Btn("▼", (s, e) => MoveSel(1)));
        right.Controls.Add(list);   // Fill önce
        right.Controls.Add(btns);
        right.Controls.Add(new Label
        {
            Text = "design by ahmedc", Dock = DockStyle.Bottom, Height = 22, ForeColor = Color.Gray,
            TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 8f, FontStyle.Italic)
        });

        // ---------- 3x3 ızgara ----------
        grid = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 3, BackColor = Color.Black, AllowDrop = true };
        for (int i = 0; i < 3; i++)
        {
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / 3));
        }

        for (int i = 0; i < PER; i++)
        {
            int cell = i;
            var panel = new Panel { Dock = DockStyle.Fill, Margin = new Padding(1), BackColor = Color.Black, AllowDrop = true };
            var view = new VideoView { Dock = DockStyle.Fill, BackColor = Color.Black, AllowDrop = true };
            var title = new Label
            {
                Dock = DockStyle.Fill, ForeColor = Color.White, BackColor = Color.FromArgb(55, 55, 55),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft,
                Cursor = Cursors.Hand, AllowDrop = true
            };

            title.MouseDown += (s, e) => { dragStart = e.Location; dragged = false; };
            title.MouseMove += (s, e) =>
            {
                int idx = page * PER + cell;
                if (e.Button == MouseButtons.Left && idx < cams.Count &&
                    (Math.Abs(e.X - dragStart.X) > 6 || Math.Abs(e.Y - dragStart.Y) > 6))
                    title.DoDragDrop(idx.ToString(), DragDropEffects.Move);
            };
            title.DoubleClick += (s, e) => { int idx = page * PER + cell; if (idx < cams.Count) OpenBig(idx); };

            var titleBar = new Panel { Dock = DockStyle.Top, Height = 26, BackColor = Color.FromArgb(55, 55, 55), AllowDrop = true };
            var tools = new FlowLayoutPanel
            {
                Dock = DockStyle.Right, AutoSize = true, WrapContents = false,
                BackColor = Color.FromArgb(55, 55, 55), AllowDrop = true
            };
            var bConn = Tiny("Bağlan");
            var bPause = Tiny("Pause");
            var bClose = Tiny("Kapat");
            pauseBtns[i] = bPause;
            bConn.Click += (s, e) => Reconnect(cell);
            bPause.Click += (s, e) => TogglePause(cell);
            bClose.Click += (s, e) => CloseCell(cell);
            tools.Controls.AddRange(new Control[] { bConn, bPause, bClose });
            titleBar.Controls.Add(title);   // Fill önce
            titleBar.Controls.Add(tools);

            foreach (Control c in new Control[] { panel, view, title, titleBar, tools })
            {
                c.DragEnter += Grid_DragEnter;
                c.DragDrop += Grid_Drop;
            }

            panel.Controls.Add(view);   // Fill önce
            panel.Controls.Add(titleBar);
            grid.Controls.Add(panel, cell % 3, cell / 3);

            views[i] = view;
            titles[i] = title;
            mps[i] = new MediaPlayer(vlc) { Mute = true };
            mps[i].EncounteredError += (s, e) => UiState(() => SetState(cell, "HATA: görüntü alınamadı"));
            mps[i].Playing += (s, e) => UiState(() => SetState(cell, "oynatılıyor"));
        }
        grid.DragEnter += Grid_DragEnter;
        grid.DragDrop += Grid_Drop;

        // ---------- büyük ekran ----------
        bigPanel = new Panel { Dock = DockStyle.Fill, Visible = false, BackColor = Color.Black };
        bigView = new VideoView { Dock = DockStyle.Fill, BackColor = Color.Black };
        var top = new Panel { Dock = DockStyle.Top, Height = 32, BackColor = Color.FromArgb(55, 55, 55) };
        bigTitle = new Label { Dock = DockStyle.Fill, ForeColor = Color.White, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 10f, FontStyle.Bold) };
        var back = new Button { Text = "◀ Izgaraya dön (Esc)", Dock = DockStyle.Right, Width = 180, FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false, BackColor = Color.FromArgb(85, 85, 85), ForeColor = Color.White };
        back.Click += (s, e) => CloseBig();
        var bigTools = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, BackColor = top.BackColor };
        var bigConn = Tiny("Bağlan");
        bigPauseBtn = Tiny("Pause");
        var bigClose = Tiny("Kapat");
        bigConn.Click += (s, e) => BigReconnect();
        bigPauseBtn.Click += (s, e) => BigPause();
        bigClose.Click += (s, e) => BigStop();
        bigTools.Controls.AddRange(new Control[] { bigConn, bigPauseBtn, bigClose });
        top.Controls.Add(bigTitle);
        top.Controls.Add(bigTools);
        top.Controls.Add(back);
        bigPanel.Controls.Add(bigView);
        bigPanel.Controls.Add(top);
        bigMp = new MediaPlayer(vlc) { Mute = true };
        bigMp.EncounteredError += (s, e) => UiState(() => BigState("HATA: görüntü alınamadı"));
        bigMp.Playing += (s, e) => UiState(() => BigState("oynatılıyor"));

        var center = new Panel { Dock = DockStyle.Fill };
        center.Controls.Add(grid);
        center.Controls.Add(bigPanel);

        Controls.Add(center);   // Fill önce
        Controls.Add(right);
        Controls.Add(bar);

        KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape && bigPanel.Visible) CloseBig(); };
        FormClosing += (s, e) =>
        {
            foreach (var m in mps) { m.Stop(); m.Dispose(); }
            bigMp.Stop(); bigMp.Dispose();
            vlc.Dispose();
        };
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        for (int i = 0; i < PER; i++) views[i].MediaPlayer = mps[i];
        RefreshList();
        LoadPage();
    }

    static Button Btn(string text, EventHandler click)
    {
        var b = new Button { Text = text, AutoSize = true, Height = 30, FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false, BackColor = Color.Gainsboro, ForeColor = Color.Black };
        b.Click += click;
        return b;
    }

    // ================= sayfa / oynatma =================

    void Go(int d)
    {
        if (bigPanel.Visible) return;
        page += d;
        LoadPage();
    }

    void LoadPage()
    {
        if (bigPanel.Visible) return;
        int pages = Math.Max(1, (cams.Count + PER - 1) / PER);
        page = Math.Clamp(page, 0, pages - 1);
        pageLbl.Text = $"Sayfa {page + 1}/{pages}";

        for (int i = 0; i < PER; i++)
        {
            int idx = page * PER + i;
            mps[i].Stop();
            paused[i] = false; closed[i] = false; pauseBtns[i].Text = "Pause";
            if (idx < cams.Count)
            {
                titles[i].Text = $"  {idx + 1}. {cams[idx].Name}";
                PlayCell(i);
            }
            else titles[i].Text = "  —";
        }
    }

    // ---------- tanı: durum yazısı ve günlük (C:\ctv\vlc.log) ----------

    static readonly object logLock = new();

    static void LogLine(string s)
    {
        try
        {
            Directory.CreateDirectory(@"C:\ctv");
            lock (logLock) File.AppendAllText(@"C:\ctv\vlc.log", $"{DateTime.Now:HH:mm:ss} {s}\r\n");
        }
        catch { }
    }

    void HookLog()
    {
        try { File.Delete(@"C:\ctv\vlc.log"); } catch { }
        vlc.Log += (s, e) =>
        {
            if (e.Level >= LogLevel.Warning) LogLine($"[{e.Level}] {e.Module}: {e.Message}");
        };
    }

    // VLC olayları farklı thread'den gelir, arayüze güvenli aktar
    void UiState(Action a)
    {
        try { if (IsHandleCreated && !IsDisposed) BeginInvoke(a); } catch { }
    }

    void SetState(int i, string txt)
    {
        int idx = page * PER + i;
        if (idx >= cams.Count || closed[i] || paused[i] || bigPanel.Visible) return;
        titles[i].Text = $"  {idx + 1}. {cams[idx].Name}  [{txt}]";
        if (txt.StartsWith("HATA")) LogLine($"KAMERA {idx + 1} ({cams[idx].Name}): {txt}  adres: {cams[idx].MaskedUrl(false)}");
    }

    static Button Tiny(string text) => new Button
    {
        Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, ForeColor = Color.White,
        BackColor = Color.FromArgb(85, 85, 85), Font = new Font("Segoe UI", 8f), Margin = new Padding(1, 2, 1, 0)
    };

    // ---------- ızgara hücresi kontrolleri ----------

    void PlayCell(int i)
    {
        int idx = page * PER + i;
        if (idx >= cams.Count) return;
        mps[i].Stop();
        LogLine($"PLAY kamera {idx + 1}: {cams[idx].MaskedUrl(false)}");
        using var m = new Media(vlc, cams[idx].Url(false), FromType.FromLocation, ":rtsp-tcp", ":network-caching=300");
        mps[i].Play(m);
    }

    void Reconnect(int i)
    {
        int idx = page * PER + i;
        if (idx >= cams.Count) return;
        paused[i] = false; closed[i] = false; pauseBtns[i].Text = "Pause";
        titles[i].Text = $"  {idx + 1}. {cams[idx].Name}";
        PlayCell(i);
    }

    void TogglePause(int i)
    {
        int idx = page * PER + i;
        if (idx >= cams.Count || closed[i]) return;
        if (!paused[i])
        {
            mps[i].SetPause(true);
            paused[i] = true;
            pauseBtns[i].Text = "Devam";
            titles[i].Text = $"  {idx + 1}. {cams[idx].Name}  (duraklatıldı)";
        }
        else Reconnect(i);   // canlı yayında devam = yeniden bağlan, eski görüntüde kalmasın
    }

    void CloseCell(int i)
    {
        int idx = page * PER + i;
        if (idx >= cams.Count) return;
        mps[i].Stop();
        closed[i] = true; paused[i] = false; pauseBtns[i].Text = "Pause";
        titles[i].Text = $"  {idx + 1}. {cams[idx].Name}  (kapalı)";
    }

    // ---------- büyük ekran kontrolleri ----------

    string BigName() => $"  {bigIdx + 1}. {cams[bigIdx].Name}  {cams[bigIdx].MaskedUrl(true)}";

    void BigState(string txt)
    {
        if (bigIdx < 0 || bigIdx >= cams.Count || !bigPanel.Visible || bigPaused) return;
        bigTitle.Text = BigName() + $"  [{txt}]";
        LogLine($"BÜYÜK EKRAN: {txt}");
    }

    void BigReconnect()
    {
        if (bigIdx < 0 || bigIdx >= cams.Count) return;
        bigPaused = false; bigPauseBtn.Text = "Pause";
        bigTitle.Text = BigName();
        bigMp.Stop();
        LogLine($"PLAY büyük ekran: {cams[bigIdx].MaskedUrl(true)}");
        using var media = new Media(vlc, cams[bigIdx].Url(true), FromType.FromLocation, ":rtsp-tcp", ":network-caching=300");
        bigMp.Play(media);
    }

    void BigPause()
    {
        if (bigIdx < 0 || bigIdx >= cams.Count) return;
        if (!bigPaused)
        {
            bigMp.SetPause(true);
            bigPaused = true; bigPauseBtn.Text = "Devam";
            bigTitle.Text = BigName() + "  (duraklatıldı)";
        }
        else BigReconnect();
    }

    void BigStop()
    {
        if (bigIdx < 0 || bigIdx >= cams.Count) return;
        bigMp.Stop();
        bigPaused = false; bigPauseBtn.Text = "Pause";
        bigTitle.Text = BigName() + "  (kapalı)";
    }

    void RefreshList()
    {
        list.Items.Clear();
        for (int i = 0; i < cams.Count; i++) list.Items.Add($"{i + 1:00}   {cams[i].Name}");
    }

    void Persist() => Store.Save(cams);

    // ================= büyük ekran =================

    void OpenBig(int idx)
    {
        if (idx < 0 || idx >= cams.Count) return;
        foreach (var m in mps) m.Stop();   // bant genişliği için ızgarayı durdur

        bigPanel.Visible = true;
        bigPanel.BringToFront();
        if (bigView.MediaPlayer == null) bigView.MediaPlayer = bigMp;

        bigIdx = idx;
        BigReconnect();
    }

    void CloseBig()
    {
        bigMp.Stop();
        bigPanel.Visible = false;
        LoadPage();
    }

    // ================= sürükle - bırak =================

    void Grid_DragEnter(object s, DragEventArgs e)
        => e.Effect = e.Data.GetDataPresent(typeof(string)) ? DragDropEffects.Move : DragDropEffects.None;

    void Grid_Drop(object s, DragEventArgs e)
    {
        if (!int.TryParse((string)e.Data.GetData(typeof(string)), out int src)) return;
        if (src < 0 || src >= cams.Count) return;

        var p = grid.PointToClient(new Point(e.X, e.Y));
        int col = Math.Clamp(p.X * 3 / Math.Max(1, grid.Width), 0, 2);
        int row = Math.Clamp(p.Y * 3 / Math.Max(1, grid.Height), 0, 2);
        int dst = page * PER + row * 3 + col;
        if (dst >= cams.Count) dst = cams.Count - 1;
        if (src == dst) return;

        (cams[src], cams[dst]) = (cams[dst], cams[src]);   // yer değiştir
        Persist();
        RefreshList();
        LoadPage();
    }

    // ================= liste düzenleme =================

    void NewCam()
    {
        using var f = new CamEditForm(new Cam());
        if (f.ShowDialog(this) != DialogResult.OK) return;
        cams.Add(f.Result);
        Persist(); RefreshList(); LoadPage();
    }

    void EditCam()
    {
        int i = list.SelectedIndex;
        if (i < 0) return;
        using var f = new CamEditForm(cams[i]);
        if (f.ShowDialog(this) != DialogResult.OK) return;
        cams[i] = f.Result;
        Persist(); RefreshList(); list.SelectedIndex = i; LoadPage();
    }

    void DelCam()
    {
        int i = list.SelectedIndex;
        if (i < 0) return;
        if (MessageBox.Show($"'{cams[i].Name}' silinsin mi?", "Sil", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        cams.RemoveAt(i);
        Persist(); RefreshList(); LoadPage();
    }

    void MoveSel(int d)
    {
        int i = list.SelectedIndex, j = i + d;
        if (i < 0 || j < 0 || j >= cams.Count) return;
        (cams[i], cams[j]) = (cams[j], cams[i]);
        Persist(); RefreshList(); list.SelectedIndex = j; LoadPage();
    }

    // ================= Excel =================

    void ImportExcel(object s, EventArgs e)
    {
        using var d = new OpenFileDialog { Filter = "Excel (*.xlsx)|*.xlsx" };
        if (d.ShowDialog() != DialogResult.OK) return;
        try
        {
            var n = ExcelIO.Import(d.FileName);
            if (n.Count == 0) { MessageBox.Show("Dosyada kamera bulunamadı."); return; }

            var r = DialogResult.No;
            if (cams.Count > 0)
                r = MessageBox.Show("Mevcut listeye eklensin mi?\n\nEvet = Ekle\nHayır = Listeyi tamamen değiştir",
                                    "İçe aktar", MessageBoxButtons.YesNoCancel);
            if (r == DialogResult.Cancel) return;
            if (r == DialogResult.No) cams.Clear();

            cams.AddRange(n);
            page = 0;
            Persist(); RefreshList(); LoadPage();
        }
        catch (Exception ex) { MessageBox.Show("İçe aktarma hatası: " + ex.Message); }
    }

    void ExportExcel(object s, EventArgs e)
    {
        using var d = new SaveFileDialog { Filter = "Excel (*.xlsx)|*.xlsx", FileName = "kameralar.xlsx" };
        if (d.ShowDialog() != DialogResult.OK) return;
        try { ExcelIO.Export(d.FileName, cams); MessageBox.Show("Dışa aktarıldı."); }
        catch (Exception ex) { MessageBox.Show("Hata: " + ex.Message); }
    }

    void TemplateExcel(object s, EventArgs e)
    {
        using var d = new SaveFileDialog { Filter = "Excel (*.xlsx)|*.xlsx", FileName = "ornek_kameralar.xlsx" };
        if (d.ShowDialog() != DialogResult.OK) return;
        var sample = new List<Cam>
        {
            new Cam { Name = "Giriş", Ip = "192.168.1.64", User = "admin", Pass = "12345", Brand = "Hikvision" },
            new Cam { Name = "Otopark", Ip = "192.168.1.108", User = "admin", Pass = "12345", Brand = "Dahua" },
            new Cam { Name = "Depo", Ip = "192.168.1.201", User = "admin", Pass = "12345", Brand = "Uniview" },
            new Cam { Name = "Pelco", Ip = "192.168.1.60", User = "", Pass = "", Brand = "Pelco" },
            new Cam { Name = "Diğer marka", Ip = "192.168.1.50", User = "admin", Pass = "12345", Brand = "Diger",
                      CustomUrl = "rtsp://{user}:{pass}@{ip}:{port}/stream1" }
        };
        try { ExcelIO.Export(d.FileName, sample); MessageBox.Show("Örnek dosya oluşturuldu."); }
        catch (Exception ex) { MessageBox.Show("Hata: " + ex.Message); }
    }
}
