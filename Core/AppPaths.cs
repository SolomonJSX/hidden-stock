using System;
using System.Collections.Generic;
using System.IO;

namespace HiddenStock.Core;

public static class AppPaths
{
    public static string Root { get; } = ResolveRoot();

    private static string ResolveRoot()
    {
        var custom = Environment.GetEnvironmentVariable("HSX_ROOT");
        if (!string.IsNullOrWhiteSpace(custom))
            return custom;

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HSX");
    }

    public static string DataDir => Path.Combine(Root, "data");
    public static string StateDir => Path.Combine(Root, "state");
    public static string VaultFile => Path.Combine(DataDir, "vault.hsx");
    public static string TempFile => Path.Combine(DataDir, "vault.tmp");
    public static string AnchorFile => Path.Combine(StateDir, "anchor.dat");

    public static void Ensure()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(StateDir);
        Hide(Root);
        Hide(DataDir);
        Hide(StateDir);
    }

    public static bool CanWrite()
    {
        try
        {
            Ensure();
            var probe = Path.Combine(StateDir, "probe.tmp");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static IEnumerable<string> Replicas()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HSX_ROOT")))
        {
            yield return Path.Combine(Root, "mirror1", "vault.hsx");
            yield return Path.Combine(Root, "mirror2", "vault.hsx");
            yield break;
        }

        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrWhiteSpace(roaming))
            yield return Path.Combine(roaming, "HSX", "mirror", "vault.hsx");

        var common = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (!string.IsNullOrWhiteSpace(common))
            yield return Path.Combine(common, "HSX", "mirror", "vault.hsx");
    }

    public static void Hide(string path)
    {
        try
        {
            if (Directory.Exists(path))
                File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden | FileAttributes.System);
        }
        catch { }
    }

    public static void HideFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden | FileAttributes.System);
        }
        catch { }
    }
}
