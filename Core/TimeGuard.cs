using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace HiddenStock.Core;

public sealed record TimeCheck(DateTimeOffset Now, DateTimeOffset Anchor, bool Rollback);

public static class TimeGuard
{
    private const long ToleranceSeconds = 300;
    private const string RegPath = @"Software\HSX";
    private const string RegValue = "a";

    public static TimeCheck Check()
    {
        var now = DateTimeOffset.UtcNow;
        var max = MaxAnchor();
        var anchor = DateTimeOffset.FromUnixTimeSeconds(max);
        var rollback = max > 0 && now.ToUnixTimeSeconds() + ToleranceSeconds < max;
        return new TimeCheck(now, anchor, rollback);
    }

    public static void Touch()
    {
        try
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var value = Math.Max(now, MaxAnchor());
            Write(value);
        }
        catch { }
    }

    private static long MaxAnchor()
    {
        long max = 0;
        foreach (var value in Read())
            max = Math.Max(max, value);
        return max;
    }

    private static IEnumerable<long> Read()
    {
        var file = ReadFile();
        if (file > 0) yield return file;

        var registry = ReadRegistry();
        if (registry > 0) yield return registry;
    }

    private static long ReadFile()
    {
        try
        {
            if (!File.Exists(AppPaths.AnchorFile)) return 0;
            var blob = File.ReadAllBytes(AppPaths.AnchorFile);
            var raw = Dpapi.Unprotect(blob);
            var text = Encoding.UTF8.GetString(raw);
            return long.TryParse(text, out var value) ? value : 0;
        }
        catch { return 0; }
    }

    private static long ReadRegistry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegPath);
            if (key?.GetValue(RegValue) is not string text) return 0;
            var blob = Convert.FromBase64String(text);
            var raw = Dpapi.Unprotect(blob);
            return long.TryParse(Encoding.UTF8.GetString(raw), out var value) ? value : 0;
        }
        catch { return 0; }
    }

    private static void Write(long value)
    {
        var raw = Encoding.UTF8.GetBytes(value.ToString());
        byte[] blob;
        try { blob = Dpapi.Protect(raw); }
        catch { return; }

        try
        {
            AppPaths.Ensure();
            File.WriteAllBytes(AppPaths.AnchorFile, blob);
            AppPaths.HideFile(AppPaths.AnchorFile);
        }
        catch { }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegPath);
            key?.SetValue(RegValue, Convert.ToBase64String(blob));
        }
        catch { }
    }
}
