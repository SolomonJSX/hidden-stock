using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using HiddenStock.Core;
using HiddenStock.UI;

namespace HiddenStock;

internal static class Program
{
    internal static string LogPath
    {
        get
        {
            var custom = Environment.GetEnvironmentVariable("HSX_LOG");
            if (!string.IsNullOrWhiteSpace(custom))
                return custom;

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HSX", "logs", "app.log");
        }
    }

    internal static void Log(string message)
    {
        try
        {
            var path = LogPath;
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
        }
        catch { }
    }

    [STAThread]
    private static int Main(string[] args)
    {
        Log("старт, аргументы: " + string.Join(" ", args));

        try
        {
            var code = Run(args);
            Log("выход " + code);
            return code;
        }
        catch (Exception ex)
        {
            Log("СБОЙ " + ex);
            try
            {
                MessageBox.Show(
                    "HiddenStock не смог запуститься.\n\n" + ex.Message + "\n\nПодробности: " + LogPath,
                    "HiddenStock", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch { }
            return 2;
        }
    }

    private static int Run(string[] args)
    {
        if (args.Contains("--selftest"))
            return SelfTest.RunAsync().GetAwaiter().GetResult();

        if (args.Contains("--seed"))
            return Demo.Seed();

        TryInit();

        if (args.Contains("--shot"))
        {
            var index = Array.IndexOf(args, "--shot");
            var path = index + 1 < args.Length
                ? args[index + 1]
                : Path.Combine(Path.GetTempPath(), "hsx_shot.png");

            var form = new MainForm
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(0, 0)
            };

            form.Shown += (_, _) =>
            {
                var timer = new System.Windows.Forms.Timer { Interval = 1800 };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    try
                    {
                        using var bitmap = new Bitmap(form.Width, form.Height);
                        form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
                        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                    }
                    finally
                    {
                        form.AllowClose();
                        form.Close();
                    }
                };
                timer.Start();
            };

            Application.Run(form);
            return 0;
        }

        Application.ThreadException += (_, e) =>
        {
            Log("ThreadException " + e.Exception);
            MessageBox.Show(e.Exception.Message, "Ошибка HiddenStock", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log("UnhandledException " + e.ExceptionObject);
        };

        Application.Run(new MainForm());
        return 0;
    }

    private static void TryInit()
    {
        try
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        }
        catch (Exception ex)
        {
            Log("SetHighDpiMode не применён: " + ex.Message);
        }

        try
        {
            Application.EnableVisualStyles();
        }
        catch (Exception ex)
        {
            Log("EnableVisualStyles не применён: " + ex.Message);
        }

        try
        {
            Application.SetCompatibleTextRenderingDefault(false);
        }
        catch (Exception ex)
        {
            Log("CompatibleTextRendering не применён: " + ex.Message);
        }
    }
}
