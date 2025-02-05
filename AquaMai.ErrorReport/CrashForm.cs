using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Matt.Encoding.Fountain;
using Matt.Random.Adapters;
using QRCoder;
using Timer = System.Windows.Forms.Timer;

namespace AquaMai.ErrorReport;

public partial class CrashForm : Form
{
    public CrashForm()
    {
        InitializeComponent();
        // 找那个竖着的屏幕
        var screen = Screen.AllScreens.FirstOrDefault(s => s.Bounds.Width < s.Bounds.Height);
        if (screen == null)
        {
            screen = Screen.PrimaryScreen;
        }

        // 把窗口大小设置成屏幕最短边的三分之二
        var minSize = Math.Min(screen.Bounds.Width, screen.Bounds.Height) * 2 / 3;
        Size = new Size(minSize, minSize);
        // 如果屏幕是竖的，就放在屏幕下方那个正方形 1:1 位置的中间
        if (screen.Bounds.Width < screen.Bounds.Height)
        {
            Location = new Point(screen.Bounds.Left + (screen.Bounds.Width - minSize) / 2, screen.Bounds.Top + (screen.Bounds.Height - screen.Bounds.Width) + (screen.Bounds.Width - minSize) / 2);
        }
    }

    private string? zipFile = null;
    private IEnumerator<Slice>? fountain = null;
    private Timer? timer = null;
    private byte[]? dataBuf = null;
    private uint chksum = 0;

    private async void CrashForm_Load(object sender, EventArgs e)
    {
        try
        {
            labelVersion.Text = "AquaMai v" + FileVersionInfo.GetVersionInfo(Application.ExecutablePath).ProductVersion;
        }
        catch
        {
            labelVersion.Text = "AquaMai (Version Unknown)";
        }

        labelStatus.Text = "正在生成错误报告... Gathering error log...";
        var exePath = Path.GetDirectoryName(Application.ExecutablePath);
        var gameDir = Path.GetDirectoryName(exePath);
        if (!File.Exists(Path.Combine(gameDir, "Sinmai.exe")))
        {
            gameDir = Environment.CurrentDirectory;
        }

        if (!File.Exists(Path.Combine(gameDir, "Sinmai.exe")))
        {
            labelStatus.Text = "未找到游戏文件夹 Game directory not found";
            return;
        }

        var errorLogPath = Path.Combine(gameDir, "Errorlog");
        if (!Directory.Exists(errorLogPath))
        {
            Directory.CreateDirectory(errorLogPath);
        }

        try
        {
            var logFiles = Directory.GetFiles(errorLogPath, "*.log");
            zipFile = Path.Combine(errorLogPath, $"AquaMaiErrorReport_{DateTime.Now:yyyyMMddHHmmss}.zip");
            using var zip = ZipFile.Open(zipFile, ZipArchiveMode.Create);

            long latestLogTime = 0;
            foreach (var logFile in logFiles)
            {
                zip.CreateEntryFromFile(logFile, Path.GetFileName(logFile));
                if (long.TryParse(Path.GetFileNameWithoutExtension(logFile), out var time) && time > latestLogTime)
                {
                    latestLogTime = time;
                }
            }

            if (latestLogTime != 0)
            {
                var latestLogFile = Path.Combine(errorLogPath, $"{latestLogTime}.log");
                var latestLogPng = Path.Combine(errorLogPath, $"{latestLogTime}.png");
                if (File.Exists(latestLogFile))
                {
                    textLog.Text = File.ReadAllText(latestLogFile).Replace("\r\n", "\n").Replace("\n", "\r\n");
                }

                // if (File.Exists(latestLogPng))
                // {
                //     zip.CreateEntryFromFile(latestLogPng, Path.GetFileName(latestLogPng));
                // }
            }

            await CreateZipTxtFromDirContent(zip, Path.Combine(gameDir, "Sinmai_Data", "StreamingAssets"));
            await CreateZipTxtFromDirContent(zip, Path.Combine(gameDir, "Mods"), true);
            await CreateZipTxtFromDirContent(zip, Path.Combine(gameDir, "UserLibs"));
            await CreateZipTxtFromDirContent(zip, Path.Combine(gameDir, "LocalAssets"));

            var melonLog = Path.Combine(gameDir, "MelonLoader", "Latest.log");
            if (File.Exists(melonLog))
            {
                // zip.CreateEntryFromFile(melonLog, Path.GetFileName(melonLog));
                var entry = zip.CreateEntry("MelonLoader.txt");
                using var stream = entry.Open();
                using var fs = new FileStream(melonLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                await fs.CopyToAsync(stream);
            }

            labelStatus.Text = $"Open https://qrss.netlify.app/ or scan the QRCode to get error report\n{zipFile}";
        }
        catch (Exception ex)
        {
            labelStatus.Text = $"生成错误报告失败 Failed to generate error report";
            if (string.IsNullOrWhiteSpace(textLog.Text))
            {
                textLog.Text = ex.ToString();
            }
        }
        finally
        {
            textLog.Select(0, 0);
        }

        try
        {
            var zipBuf = File.ReadAllBytes(zipFile);
            dataBuf = AppendFileHeaderMetaToBuffer(zipBuf, new FileHeaderMeta()
            {
                Filename = Path.GetFileName(zipFile),
                ContentType = "application/zip"
            });
            fountain = SliceHelpers.CreateGenerator(dataBuf, 500, () => new RandomAdapter(new Random())).GetEnumerator();
        }
        catch
        {
            labelStatus.Text = "生成喷泉码失败 Failed to generate fountain code";
        }

        timer = new Timer { Interval = 50 };
        timer.Tick += Tick;
        timer.Start();
    }

    private static byte[] AppendFileHeaderMetaToBuffer(byte[] data, FileHeaderMeta meta)
    {
        string json = $"{{\"filename\":\"{meta.Filename}\",\"contentType\":\"{meta.ContentType}\"}}";
        byte[] metaBuffer = Encoding.UTF8.GetBytes(json);
        return MergeByteArrays(metaBuffer, data);
    }

    QRCodeGenerator qrGenerator = new QRCodeGenerator();

    private void Tick(object _, EventArgs _ea)
    {
        if (fountain == null)
        {
            return;
        }

        if (fountain.MoveNext())
        {
            var slice = fountain.Current;
            int[] indices = slice.Coefficients
                .Select((value, index) => (value, index)) // 生成 (值, 索引) 对
                .Where(pair => pair.value) // 仅保留值为 true 的
                .Select(pair => pair.index) // 提取索引
                .ToArray();
            using var data = new MemoryStream();
            using var writer = new BinaryWriter(data);
            writer.Write((uint)indices.Length);
            foreach (var index in indices)
            {
                writer.Write((uint)index);
            }

            writer.Write((uint)slice.Coefficients.Count);
            var sliceData = slice.Data.ToArray();
            writer.Write((uint)dataBuf.Length);
            if (chksum == 0)
            {
                chksum = ChecksumCalculator.GetChecksum(dataBuf, (uint)slice.Coefficients.Count);
            }

            writer.Write((uint)chksum);
            writer.Write(sliceData);
            writer.Flush();

            using QRCodeData qrCodeData = qrGenerator.CreateQrCode($"https://qrss.netlify.app/#{Convert.ToBase64String(data.ToArray())}", QRCodeGenerator.ECCLevel.M);
            pictureBox1.Image = new QRCode(qrCodeData).GetGraphic(20);
        }
    }

    private async Task CreateZipTxtFromDirContent(ZipArchive zip, string dir, bool includeMd5 = false)
    {
        if (!Directory.Exists(dir)) return;
        var subFiles = Directory.GetFileSystemEntries(dir);
        using var subZip = zip.CreateEntry($"{Path.GetFileName(dir)}.txt").Open();
        using var writer = new StreamWriter(subZip);
        foreach (var subFile in subFiles)
        {
            if (includeMd5 && File.Exists(subFile))
            {
                await writer.WriteLineAsync($"{subFile} {GetFileMD5(subFile)}");
            }
            else
            {
                await writer.WriteLineAsync(subFile);
            }
        }

        await writer.FlushAsync();
    }

    public static byte[] MergeByteArrays(byte[] array1, byte[] array2)
    {
        byte[][] arrays = new byte[][] { array1, array2 };
        int totalLength = arrays.Sum(arr => arr.Length + 4); // 4 bytes for each length (Uint32)

        byte[] mergedArray = new byte[totalLength];
        int offset = 0;

        foreach (var arr in arrays)
        {
            int length = arr.Length;

            // Store the length as a 4-byte Uint32
            mergedArray[offset++] = (byte)((length >> 24) & 0xFF);
            mergedArray[offset++] = (byte)((length >> 16) & 0xFF);
            mergedArray[offset++] = (byte)((length >> 8) & 0xFF);
            mergedArray[offset++] = (byte)(length & 0xFF);

            // Copy data
            Array.Copy(arr, 0, mergedArray, offset, length);
            offset += length;
        }

        return mergedArray;
    }

    private static string GetFileMD5(string filePath)
    {
        using var md5 = MD5.Create();
        using var stream = File.OpenRead(filePath);
        byte[] hashBytes = md5.ComputeHash(stream);
        return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
    }

    private void CrashForm_KeyDown(object sender, KeyEventArgs e)
    {
        Application.Exit();
    }
}