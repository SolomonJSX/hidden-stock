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
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (Exception ex)
        {
            try
            {
                var target = Environment.GetEnvironmentVariable("HSX_LOG");
                if (string.IsNullOrWhiteSpace(target))
                    target = Path.Combine(Path.GetTempPath(), "hsx_crash.log");
                File.WriteAllText(target, ex.ToString());
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

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

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
            MessageBox.Show(e.Exception.ToString(), "Ошибка HiddenStock", MessageBoxButtons.OK, MessageBoxIcon.Error);

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            MessageBox.Show(e.ExceptionObject?.ToString() ?? "Неизвестная ошибка", "Ошибка HiddenStock", MessageBoxButtons.OK, MessageBoxIcon.Error);

        Application.Run(new MainForm());
        return 0;
    }
}
