using System;
using System.IO;
using System.Threading;

namespace HiddenStock.Core;

internal static class Demo
{
    public static int Seed()
    {
        var root = Environment.GetEnvironmentVariable("HSX_ROOT");
        if (string.IsNullOrWhiteSpace(root))
        {
            Console.Error.WriteLine("HSX_ROOT не задан");
            return 1;
        }

        var source = Path.Combine(root, "sample");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(Path.Combine(source, "photos"));

        File.WriteAllText(Path.Combine(source, "договор.txt"), "Тестовый документ HiddenStock.");
        File.WriteAllText(Path.Combine(source, "photos", "отпуск-2026.jpg"), "не настоящее фото");
        File.WriteAllBytes(Path.Combine(source, "архив.zip"), new byte[512 * 1024]);
        File.WriteAllText(Path.Combine(source, "photos", "scan.png"), "не настоящая картинка");

        if (Vault.Exists)
        {
            Console.WriteLine("Сейф уже есть");
            return 0;
        }

        var unlock = string.Equals(Environment.GetEnvironmentVariable("HSX_DEMO_UNLOCK"), "past",
            StringComparison.OrdinalIgnoreCase)
            ? DateTimeOffset.UtcNow.AddSeconds(-60)
            : DateTimeOffset.UtcNow.AddDays(41);
        Vault.SealAsync(new[] { source }, unlock, false, null, CancellationToken.None)
            .GetAwaiter().GetResult();

        Console.WriteLine("Сейф создан, откроется " + unlock.ToLocalTime());
        return 0;
    }
}
