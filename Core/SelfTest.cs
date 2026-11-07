using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace HiddenStock.Core;

internal static class SelfTest
{
    public static async Task<int> RunAsync()
    {
        var lines = new List<string>();
        var ok = true;

        void Check(string name, bool condition, string? detail = null)
        {
            ok &= condition;
            lines.Add($"{(condition ? "PASS" : "FAIL")}  {name}{(detail == null ? "" : " :: " + detail)}");
        }

        var root = Environment.GetEnvironmentVariable("HSX_TEST_ROOT");
        if (string.IsNullOrWhiteSpace(root))
            root = Path.Combine(Path.GetTempPath(), "hsx_selftest_" + Guid.NewGuid().ToString("n"));
        else
            Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("HSX_ROOT", root);

        try
        {
            var src = Path.Combine(root, "src");
            Directory.CreateDirectory(Path.Combine(src, "sub"));

            File.WriteAllText(Path.Combine(src, "a.txt"), "hello hidden stock");
            var blob = new byte[3 * 1024 * 1024 + 123];
            Random.Shared.NextBytes(blob);
            File.WriteAllBytes(Path.Combine(src, "blob.bin"), blob);
            File.WriteAllBytes(Path.Combine(src, "empty.dat"), Array.Empty<byte>());
            File.WriteAllText(Path.Combine(src, "sub", "note.txt"), new string('ж', 50000));

            var originals = Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories)
                .ToDictionary(path => path, Hash, StringComparer.OrdinalIgnoreCase);
            var expectedBytes = originals.Keys.Sum(path => new FileInfo(path).Length);

            var unlock = DateTimeOffset.UtcNow.AddDays(2);
            var report = await Vault.SealAsync(new[] { src }, unlock, false, null, CancellationToken.None);
            Check("запечатано 4 файла", report.Files == 4, report.Files.ToString());
            lines.Add("INSPECT 1 " + Vault.Inspect());

            var status = Vault.GetStatus();
            Check("сейф существует", status.Exists);
            Check("сейф заблокирован", !status.Unlocked);
            Check("файлов в сейфе", status.FileCount == 4, status.FileCount.ToString());
            Check("объём совпал", status.TotalBytes == expectedBytes, $"{status.TotalBytes} vs {expectedBytes}");
            Check("оригиналы на месте", File.Exists(Path.Combine(src, "a.txt")));

            var blocked = false;
            try
            {
                await Vault.UnsealAsync(Path.Combine(root, "early"), null, CancellationToken.None);
            }
            catch (InvalidOperationException)
            {
                blocked = true;
            }
            Check("рано открыть нельзя", blocked);

            var doubleSeal = false;
            try
            {
                await Vault.SealAsync(new[] { src }, unlock, false, null, CancellationToken.None);
            }
            catch (InvalidOperationException)
            {
                doubleSeal = true;
            }
            Check("второй сейф не создаётся", doubleSeal);

            Vault.Erase();
            Check("сейф удалён", !Vault.Exists);

            var past = DateTimeOffset.UtcNow.AddSeconds(-60);
            await Vault.SealAsync(new[] { src }, past, false, null, CancellationToken.None);
            var unlocked = Vault.GetStatus();
            lines.Add("INSPECT 2 " + Vault.Inspect());
            Check("после даты открыт", unlocked.Unlocked);

            var dest = Path.Combine(root, "out");
            var restored = await Vault.UnsealAsync(dest, null, CancellationToken.None);
            Check("восстановлено 4 файла", restored.Files == 4, restored.Files.ToString());

            foreach (var pair in originals)
            {
                var relative = Path.GetRelativePath(root, pair.Key);
                var target = Path.Combine(dest, relative);
                var same = File.Exists(target) && Hash(target) == pair.Value;
                Check("совпал " + Path.GetFileName(pair.Key), same);
            }

            var attributes = File.GetAttributes(AppPaths.VaultFile);
            File.SetAttributes(AppPaths.VaultFile, FileAttributes.Normal);
            var vaultBytes = File.ReadAllBytes(AppPaths.VaultFile);
            vaultBytes[45] ^= 0xFF;
            File.WriteAllBytes(AppPaths.VaultFile, vaultBytes);
            File.SetAttributes(AppPaths.VaultFile, attributes);
            Check("подмена заголовка замечена", Vault.GetStatus().Corrupt);

            Vault.Erase();
            var victim = Path.Combine(root, "victim.txt");
            File.WriteAllText(victim, "delete me");
            await Vault.SealAsync(new[] { victim }, DateTimeOffset.UtcNow.AddSeconds(-30), true, null, CancellationToken.None);
            Check("исходник удалён после запечатывания", !File.Exists(victim));

            TimeGuard.Touch();
            Check("сторож времени без отката", !TimeGuard.Check().Rollback);
        }
        catch (Exception ex)
        {
            ok = false;
            lines.Add("EXCEPTION " + ex);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }

        lines.Add("RESULT: " + (ok ? "OK" : "FAILED"));
        var logPath = Environment.GetEnvironmentVariable("HSX_LOG");
        if (string.IsNullOrWhiteSpace(logPath))
            logPath = Path.Combine(Path.GetTempPath(), "hsx_selftest.log");

        try
        {
            File.WriteAllLines(logPath, lines, new System.Text.UTF8Encoding(true));
        }
        catch { }

        return ok ? 0 : 1;
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
