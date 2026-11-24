using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;

namespace CtvViewer;

public class Cam
{
    public string Name { get; set; } = "";
    public string Ip { get; set; } = "";
    public string User { get; set; } = "admin";
    public string Pass { get; set; } = "";
    public string Brand { get; set; } = "Hikvision";
    public int Port { get; set; } = 554;
    public int Channel { get; set; } = 1;
    public string CustomUrl { get; set; } = "";

    // main = true: ana akış (büyük ekran), false: alt akış (3x3 ızgara, daha hafif)
    public string Url(bool main)
    {
        string u = Uri.EscapeDataString(User ?? ""), p = Uri.EscapeDataString(Pass ?? "");

        if (!string.IsNullOrWhiteSpace(CustomUrl))
            return CustomUrl.Replace("{user}", u).Replace("{pass}", p).Replace("{ip}", Ip)
                            .Replace("{port}", Port.ToString()).Replace("{ch}", Channel.ToString());

        string head = $"rtsp://{u}:{p}@{Ip}:{Port}";
        return (Brand ?? "").ToLowerInvariant() switch
        {
            "hikvision" => $"{head}/Streaming/Channels/{Channel}0{(main ? 1 : 2)}",
            "dahua"     => $"{head}/cam/realmonitor?channel={Channel}&subtype={(main ? 0 : 1)}",
            "uniview"   => $"{head}/unicast/c{Channel}/s{(main ? 0 : 1)}/live",
            _           => head + "/"
        };
    }
}

public static class Store
{
    public const string Dir = @"C:\ctv\database";
    public static readonly string Lic = Path.Combine(Dir, "ctvconfig.lic");
    public static readonly string CamFile = Path.Combine(Dir, "cameras.dat");

    // Varsayılan admin şifresi. Derlemeden önce değiştir.
    public const string AdminPass = "Admin@ctv2026";

    static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ctv-v1");

    static byte[] Protect(byte[] d) => ProtectedData.Protect(d, Entropy, DataProtectionScope.LocalMachine);
    static byte[] Unprotect(byte[] d) => ProtectedData.Unprotect(d, Entropy, DataProtectionScope.LocalMachine);

    // Kullanıcı şifresi (ctvconfig.lic içinde şifreli saklanır)
    public static string GetUserPass()
    {
        try
        {
            if (!File.Exists(Lic)) return "";
            return Encoding.UTF8.GetString(Unprotect(Convert.FromBase64String(File.ReadAllText(Lic))));
        }
        catch { return ""; }
    }

    public static void SetUserPass(string pass)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(Lic, Convert.ToBase64String(Protect(Encoding.UTF8.GetBytes(pass ?? ""))));
    }

    // Kamera listesi (cameras.dat içinde şifreli saklanır)
    public static List<Cam> Load()
    {
        try
        {
            if (!File.Exists(CamFile)) return new List<Cam>();
            var json = Encoding.UTF8.GetString(Unprotect(File.ReadAllBytes(CamFile)));
            return JsonSerializer.Deserialize<List<Cam>>(json) ?? new List<Cam>();
        }
        catch { return new List<Cam>(); }
    }

    public static void Save(List<Cam> cams)
    {
        Directory.CreateDirectory(Dir);
        var json = JsonSerializer.Serialize(cams);
        File.WriteAllBytes(CamFile, Protect(Encoding.UTF8.GetBytes(json)));
    }
}

public static class ExcelIO
{
    static readonly string[] Headers =
    {
        "Kamera Adı", "IP Adresi", "Kullanıcı Adı", "Şifre",
        "Marka", "Port", "Kanal", "Özel RTSP (opsiyonel)"
    };

    public static void Export(string path, IEnumerable<Cam> cams)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Kameralar");
        for (int i = 0; i < Headers.Length; i++) ws.Cell(1, i + 1).Value = Headers[i];
        ws.Row(1).Style.Font.Bold = true;

        int r = 2;
        foreach (var c in cams)
        {
            ws.Cell(r, 1).Value = c.Name;
            ws.Cell(r, 2).Value = c.Ip;
            ws.Cell(r, 3).Value = c.User;
            ws.Cell(r, 4).Value = c.Pass;
            ws.Cell(r, 5).Value = c.Brand;
            ws.Cell(r, 6).Value = c.Port;
            ws.Cell(r, 7).Value = c.Channel;
            ws.Cell(r, 8).Value = c.CustomUrl;
            r++;
        }
        ws.Columns().AdjustToContents();
        wb.SaveAs(path);
    }

    public static List<Cam> Import(string path)
    {
        var list = new List<Cam>();
        using var wb = new XLWorkbook(path);
        var ws = wb.Worksheet(1);

        foreach (var row in ws.RowsUsed().Skip(1)) // 1. satır başlık
        {
            string name = row.Cell(1).GetString().Trim();
            string ip = row.Cell(2).GetString().Trim();
            if (name == "" && ip == "") continue;

            int.TryParse(row.Cell(6).GetString(), out int port);
            int.TryParse(row.Cell(7).GetString(), out int ch);
            string brand = row.Cell(5).GetString().Trim();

            list.Add(new Cam
            {
                Name = name == "" ? ip : name,
                Ip = ip,
                User = row.Cell(3).GetString().Trim(),
                Pass = row.Cell(4).GetString(),
                Brand = brand == "" ? "Hikvision" : brand,
                Port = port > 0 ? port : 554,
                Channel = ch > 0 ? ch : 1,
                CustomUrl = row.Cell(8).GetString().Trim()
            });
        }
        return list; // Excel'deki sıra = ekrandaki sıra
    }
}
