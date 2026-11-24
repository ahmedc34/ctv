using System.IO.Compression;
using System.Reflection;
using LibVLCSharp.Shared;

namespace CtvViewer;

static class Program
{
    // exe içine gömülü libvlc.zip varsa ilk çalıştırmada C:\ctv\libvlc altına açar
    static string PrepareLibVlc()
    {
        using var res = Assembly.GetExecutingAssembly().GetManifestResourceStream("libvlc.zip");
        if (res == null) return null;   // geliştirme modu: libvlc exe'nin yanında

        string dir = @"C:\ctv\libvlc";
        if (!File.Exists(Path.Combine(dir, "libvlc.dll")))
        {
            Directory.CreateDirectory(dir);
            using var zip = new ZipArchive(res);
            zip.ExtractToDirectory(dir, true);
        }
        return dir;
    }

    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        try { Core.Initialize(PrepareLibVlc()); }
        catch (Exception ex)
        {
            MessageBox.Show("LibVLC başlatılamadı:\n" + ex.Message, "CTV");
            return;
        }

        using var login = new LoginForm();
        if (login.ShowDialog() != DialogResult.OK) return;

        Application.Run(new MainForm(login.IsAdmin));
    }
}

class LoginForm : Form
{
    public bool IsAdmin;
    readonly TextBox tb = new() { UseSystemPasswordChar = true, Left = 15, Top = 38, Width = 270 };

    public LoginForm()
    {
        Text = "CTV İzleme - Giriş";
        ClientSize = new Size(300, 135);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = MinimizeBox = false;

        var ok = new Button { Text = "Giriş", Left = 205, Top = 72, Width = 80 };
        ok.Click += (s, e) => Try();
        Controls.Add(new Label { Text = "Şifre:", Left = 15, Top = 15, AutoSize = true });
        Controls.Add(tb);
        Controls.Add(ok);
        Controls.Add(new Label
        {
            Text = "design by ahmedc", Left = 0, Top = 108, Width = 300, ForeColor = Color.Gray,
            TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 8f, FontStyle.Italic)
        });
        AcceptButton = ok;
    }

    void Try()
    {
        string v = tb.Text;
        if (v == Store.AdminPass) { IsAdmin = true; DialogResult = DialogResult.OK; return; }

        string up = Store.GetUserPass();
        if (up.Length > 0 && v == up) { DialogResult = DialogResult.OK; return; }

        MessageBox.Show("Hatalı şifre.", "CTV", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        tb.Clear();
        tb.Focus();
    }
}

// Sadece admin girişinde açılır: kullanıcı şifresini görür ve değiştirir
class SettingsForm : Form
{
    readonly TextBox tb = new() { Left = 15, Top = 38, Width = 270, Text = Store.GetUserPass() };

    public SettingsForm()
    {
        Text = "Kullanıcı Şifresi";
        ClientSize = new Size(300, 110);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = MinimizeBox = false;

        var save = new Button { Text = "Kaydet", Left = 205, Top = 72, Width = 80 };
        save.Click += (s, e) =>
        {
            Store.SetUserPass(tb.Text);
            MessageBox.Show("Kaydedildi.");
            Close();
        };
        Controls.Add(new Label { Text = "Mevcut kullanıcı şifresi (boşsa sadece admin girebilir):", Left = 15, Top = 12, AutoSize = true });
        Controls.Add(tb);
        Controls.Add(save);
    }
}

class CamEditForm : Form
{
    public Cam Result;
    readonly Control[] f = new Control[8];
    readonly ComboBox brand = new() { DropDownStyle = ComboBoxStyle.DropDownList };

    public CamEditForm(Cam c)
    {
        Text = "Kamera";
        ClientSize = new Size(370, 8 * 32 + 60);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = MinimizeBox = false;

        brand.Items.AddRange(new object[] { "Hikvision", "Dahua", "Uniview", "Diger" });
        brand.SelectedItem = brand.Items.Contains(c.Brand) ? c.Brand : "Diger";

        string[] labels = { "Kamera adı", "IP adresi", "Kullanıcı adı", "Şifre", "Marka", "Port", "Kanal", "Özel RTSP" };
        f[0] = new TextBox { Text = c.Name };
        f[1] = new TextBox { Text = c.Ip };
        f[2] = new TextBox { Text = c.User };
        f[3] = new TextBox { Text = c.Pass };
        f[4] = brand;
        f[5] = new TextBox { Text = c.Port.ToString() };
        f[6] = new TextBox { Text = c.Channel.ToString() };
        f[7] = new TextBox { Text = c.CustomUrl };

        for (int i = 0; i < 8; i++)
        {
            Controls.Add(new Label { Text = labels[i], Left = 10, Top = 12 + i * 32, Width = 100 });
            f[i].SetBounds(120, 8 + i * 32, 240, 24);
            Controls.Add(f[i]);
        }

        var ok = new Button { Text = "Kaydet", Left = 280, Top = 8 * 32 + 16, Width = 80 };
        ok.Click += (s, e) =>
        {
            if (f[1].Text.Trim() == "" && f[7].Text.Trim() == "")
            {
                MessageBox.Show("IP adresi gerekli.");
                return;
            }
            int.TryParse(f[5].Text, out int port);
            int.TryParse(f[6].Text, out int ch);
            Result = new Cam
            {
                Name = f[0].Text.Trim() == "" ? f[1].Text.Trim() : f[0].Text.Trim(),
                Ip = f[1].Text.Trim(),
                User = f[2].Text.Trim(),
                Pass = f[3].Text,
                Brand = (string)brand.SelectedItem,
                Port = port > 0 ? port : 554,
                Channel = ch > 0 ? ch : 1,
                CustomUrl = f[7].Text.Trim()
            };
            DialogResult = DialogResult.OK;
        };
        Controls.Add(ok);
    }
}
