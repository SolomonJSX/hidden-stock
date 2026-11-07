using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HiddenStock.Core;

public sealed record ProgressInfo(int Percent, string Message);

public sealed record SealReport(int Files, long Bytes, DateTimeOffset UnlockAt);

public sealed record UnsealReport(int Files, long Bytes, string Destination);

public sealed class VaultEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Rel { get; set; } = "";
    public long Size { get; set; }
    public long Offset { get; set; }
    public int Chunks { get; set; }
    public string Nonce { get; set; } = "";
    public string Sha256 { get; set; } = "";
}

public sealed class Manifest
{
    public string VaultId { get; set; } = "";
    public long CreatedAt { get; set; }
    public long UnlockAt { get; set; }
    public List<VaultEntry> Entries { get; set; } = new();
}

public sealed class VaultStatus
{
    public bool Exists { get; set; }
    public bool Corrupt { get; set; }
    public string Error { get; set; } = "";
    public long CreatedAt { get; set; }
    public long UnlockAt { get; set; }
    public int FileCount { get; set; }
    public long TotalBytes { get; set; }

    public DateTimeOffset UnlockLocal =>
        UnlockAt > 0 ? DateTimeOffset.FromUnixTimeSeconds(UnlockAt).ToLocalTime() : DateTimeOffset.MinValue;

    public DateTimeOffset CreatedLocal =>
        CreatedAt > 0 ? DateTimeOffset.FromUnixTimeSeconds(CreatedAt).ToLocalTime() : DateTimeOffset.MinValue;

    public bool Unlocked =>
        Exists && !Corrupt && DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= UnlockAt;
}

internal sealed class Header
{
    public const int Size = 160;

    public byte[] Raw = new byte[Size];
    public ushort Version;
    public int ChunkSize;
    public byte[] VaultId = new byte[16];
    public long CreatedAt;
    public long UnlockAt;
    public long ManifestOffset;
    public long ManifestLength;
    public long KeyBlobOffset;
    public int KeyBlobLength;
    public byte[] Mac = new byte[16];

    public Header()
    {
        Raw[0] = (byte)'H';
        Raw[1] = (byte)'S';
        Raw[2] = (byte)'X';
        Raw[3] = (byte)'1';
    }

    public static Header Read(Stream stream)
    {
        var raw = new byte[Size];
        stream.Position = 0;
        stream.ReadExactly(raw, 0, Size);

        if (raw[0] != (byte)'H' || raw[1] != (byte)'S' || raw[2] != (byte)'X' || raw[3] != (byte)'1')
            throw new InvalidDataException($"Файл не похож на сейф HiddenStock ({Convert.ToHexString(raw.AsSpan(0, 4))}).");

        var header = new Header { Raw = raw };
        header.Version = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(4));
        header.ChunkSize = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(8));
        Array.Copy(raw, 16, header.VaultId, 0, 16);
        header.CreatedAt = BinaryPrimitives.ReadInt64LittleEndian(raw.AsSpan(32));
        header.UnlockAt = BinaryPrimitives.ReadInt64LittleEndian(raw.AsSpan(40));
        header.ManifestOffset = BinaryPrimitives.ReadInt64LittleEndian(raw.AsSpan(48));
        header.ManifestLength = BinaryPrimitives.ReadInt64LittleEndian(raw.AsSpan(56));
        header.KeyBlobOffset = BinaryPrimitives.ReadInt64LittleEndian(raw.AsSpan(64));
        header.KeyBlobLength = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(72));
        Array.Copy(raw, 144, header.Mac, 0, 16);

        if (header.Version != 1)
            throw new InvalidDataException("Версия сейфа не поддерживается.");

        return header;
    }

    public void Write(Stream stream, byte[]? macKey = null)
    {
        Raw[0] = (byte)'H';
        Raw[1] = (byte)'S';
        Raw[2] = (byte)'X';
        Raw[3] = (byte)'1';
        BinaryPrimitives.WriteUInt16LittleEndian(Raw.AsSpan(4), Version);
        BinaryPrimitives.WriteInt32LittleEndian(Raw.AsSpan(8), ChunkSize);
        Array.Copy(VaultId, 0, Raw, 16, 16);
        BinaryPrimitives.WriteInt64LittleEndian(Raw.AsSpan(32), CreatedAt);
        BinaryPrimitives.WriteInt64LittleEndian(Raw.AsSpan(40), UnlockAt);
        BinaryPrimitives.WriteInt64LittleEndian(Raw.AsSpan(48), ManifestOffset);
        BinaryPrimitives.WriteInt64LittleEndian(Raw.AsSpan(56), ManifestLength);
        BinaryPrimitives.WriteInt64LittleEndian(Raw.AsSpan(64), KeyBlobOffset);
        BinaryPrimitives.WriteInt32LittleEndian(Raw.AsSpan(72), KeyBlobLength);

        if (macKey != null)
        {
            var hash = HMACSHA256.HashData(macKey, Raw.AsSpan(0, 144));
            Array.Copy(hash, 0, Mac, 0, 16);
        }

        Array.Copy(Mac, 0, Raw, 144, 16);

        stream.Position = 0;
        stream.Write(Raw, 0, Size);
    }
}

public static class Vault
{
    public const int ChunkSize = 1024 * 1024;

    private const string Magic = "HSX1";

    public static bool Exists => File.Exists(AppPaths.VaultFile);

    public static void EnsureSelfHeal()
    {
        try
        {
            if (File.Exists(AppPaths.VaultFile)) return;
            foreach (var replica in AppPaths.Replicas())
            {
                if (!File.Exists(replica)) continue;
                AppPaths.Ensure();
                File.Copy(replica, AppPaths.VaultFile, true);
                AppPaths.HideFile(AppPaths.VaultFile);
                return;
            }
        }
        catch { }
    }

    public static VaultStatus GetStatus()
    {
        EnsureSelfHeal();
        if (!File.Exists(AppPaths.VaultFile))
            return new VaultStatus { Exists = false };

        try
        {
            using var stream = OpenRead(AppPaths.VaultFile);
            var header = Header.Read(stream);
            var key = UnwrapKey(stream, header);
            var manifest = ReadManifest(stream, header, key);
            return new VaultStatus
            {
                Exists = true,
                CreatedAt = header.CreatedAt,
                UnlockAt = header.UnlockAt,
                FileCount = manifest.Entries.Count,
                TotalBytes = manifest.Entries.Sum(e => e.Size)
            };
        }
        catch (Exception ex)
        {
            return new VaultStatus { Exists = true, Corrupt = true, Error = ex.Message };
        }
    }

    public static async Task<SealReport> SealAsync(
        IReadOnlyList<string> inputs,
        DateTimeOffset unlockUtc,
        bool deleteOriginals,
        IProgress<ProgressInfo>? progress,
        CancellationToken cancellationToken)
    {
        if (File.Exists(AppPaths.VaultFile))
            throw new InvalidOperationException("Сейф уже создан. Сначала восстанови или удали его.");

        var files = Expand(inputs);
        if (files.Count == 0)
            throw new InvalidOperationException("Нечего запечатывать: файлы не найдены.");

        AppPaths.Ensure();

        var totalBytes = files.Sum(f => new FileInfo(f.Full).Length);
        var key = Crypto.Random(Crypto.KeySize);
        var vaultId = Crypto.Random(16);
        var macKey = Crypto.Derive(key, "mac");
        var chunkKey = Crypto.Derive(key, "chunks");
        var manifestKey = Crypto.Derive(key, "manifest");

        var manifest = new Manifest
        {
            VaultId = Crypto.Hex(vaultId),
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            UnlockAt = unlockUtc.ToUnixTimeSeconds()
        };

        if (File.Exists(AppPaths.TempFile))
            File.Delete(AppPaths.TempFile);

        long done = 0;

        await using (var output = new FileStream(
                         AppPaths.TempFile, FileMode.Create, FileAccess.ReadWrite, FileShare.None,
                         1 << 20, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            output.Write(new byte[Header.Size]);

            var keyBlob = Dpapi.Protect(key, vaultId);
            var keyBlobOffset = output.Position;
            output.Write(keyBlob);

            var index = 0;
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                index++;
                progress?.Report(new ProgressInfo(
                    Percent(done, totalBytes),
                    $"Шифрую {index} из {files.Count}: {file.Rel}"));

                var entry = await EncryptFileAsync(output, file.Full, file.Rel, chunkKey, cancellationToken)
                    .ConfigureAwait(false);
                manifest.Entries.Add(entry);
                done += entry.Size;
            }

            progress?.Report(new ProgressInfo(99, "Записываю манифест..."));

            var json = JsonSerializer.SerializeToUtf8Bytes(manifest);
            var manifestNonce = Crypto.Random(Crypto.NonceSize);
            var manifestTag = new byte[Crypto.TagSize];
            var manifestCipher = Crypto.Encrypt(manifestKey, manifestNonce, json, vaultId, out manifestTag);

            var manifestOffset = output.Position;
            output.Write(manifestNonce);
            output.Write(manifestCipher);
            output.Write(manifestTag);
            var manifestLength = Crypto.NonceSize + manifestCipher.Length + Crypto.TagSize;

            var header = new Header
            {
                Version = 1,
                ChunkSize = ChunkSize,
                VaultId = vaultId,
                CreatedAt = manifest.CreatedAt,
                UnlockAt = manifest.UnlockAt,
                ManifestOffset = manifestOffset,
                ManifestLength = manifestLength,
                KeyBlobOffset = keyBlobOffset,
                KeyBlobLength = keyBlob.Length
            };
            header.Write(output, macKey);
            output.Flush(true);
        }

        if (File.Exists(AppPaths.VaultFile))
            File.Delete(AppPaths.VaultFile);
        File.Move(AppPaths.TempFile, AppPaths.VaultFile);
        AppPaths.HideFile(AppPaths.VaultFile);
        AppPaths.Hide(AppPaths.Root);

        Replicate();

        if (deleteOriginals)
            SecureDelete(files, inputs);

        progress?.Report(new ProgressInfo(100, "Готово"));
        return new SealReport(files.Count, totalBytes, unlockUtc);
    }

    public static async Task<UnsealReport> UnsealAsync(
        string destination,
        IProgress<ProgressInfo>? progress,
        CancellationToken cancellationToken)
    {
        EnsureSelfHeal();
        if (!File.Exists(AppPaths.VaultFile))
            throw new InvalidOperationException("Сейф не найден.");

        using var stream = OpenRead(AppPaths.VaultFile);
        var header = Header.Read(stream);

        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() < header.UnlockAt)
            throw new InvalidOperationException("Время ещё не наступило.");

        var key = UnwrapKey(stream, header);
        var manifest = ReadManifest(stream, header, key);
        var chunkKey = Crypto.Derive(key, "chunks");

        Directory.CreateDirectory(destination);
        var root = Path.GetFullPath(destination);
        long done = 0;
        var total = manifest.Entries.Sum(e => e.Size);
        var count = 0;

        foreach (var entry in manifest.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            count++;
            progress?.Report(new ProgressInfo(
                Percent(done, total),
                $"Восстанавливаю {count} из {manifest.Entries.Count}: {entry.Rel}"));

            var relative = SanitizeRel(entry.Rel);
            var target = Path.GetFullPath(Path.Combine(root, relative));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Недопустимый путь внутри сейфа.");

            target = UniquePath(target);
            await DecryptFileAsync(stream, entry, chunkKey, target, cancellationToken).ConfigureAwait(false);
            done += entry.Size;
        }

        progress?.Report(new ProgressInfo(100, "Готово"));
        return new UnsealReport(manifest.Entries.Count, total, destination);
    }

    public static string Inspect()
    {
        try
        {
            using var stream = OpenRead(AppPaths.VaultFile);
            var header = Header.Read(stream);
            var key = UnwrapKey(stream, header);
            var manifest = ReadManifest(stream, header, key);
            return $"size={stream.Length} keyBlob={header.KeyBlobOffset}/{header.KeyBlobLength} " +
                   $"manifest={header.ManifestOffset}/{header.ManifestLength} entries={manifest.Entries.Count} " +
                   $"unlock={manifest.UnlockAt}";
        }
        catch (Exception ex)
        {
            return "INSPECT ERROR: " + ex.GetType().Name + ": " + ex.Message;
        }
    }

    public static void Erase()
    {        TryDelete(AppPaths.VaultFile);
        TryDelete(AppPaths.TempFile);
        foreach (var replica in AppPaths.Replicas())
            TryDelete(replica);
    }

    private static async Task<VaultEntry> EncryptFileAsync(
        Stream output, string path, string relative, byte[] chunkKey, CancellationToken cancellationToken)
    {
        var fileId = Crypto.Random(16);
        var nonceBase = Crypto.Random(8);
        var fileKey = Crypto.Derive(chunkKey, fileId);
        var offset = output.Position;
        var chunkIndex = 0;
        long size = 0;

        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var aes = new AesGcm(fileKey, Crypto.TagSize);
        var buffer = new byte[ChunkSize];

        await using (var source = new FileStream(
                         path, FileMode.Open, FileAccess.Read, FileShare.Read,
                         1 << 20, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(0, ChunkSize), cancellationToken).ConfigureAwait(false)) > 0)
            {
                var isLast = source.Position >= source.Length;
                WriteChunk(output, aes, fileId, nonceBase, chunkIndex, buffer.AsSpan(0, read), isLast);
                sha.AppendData(buffer, 0, read);
                size += read;
                chunkIndex++;
            }
        }

        if (chunkIndex == 0)
        {
            WriteChunk(output, aes, fileId, nonceBase, 0, ReadOnlySpan<byte>.Empty, true);
            chunkIndex = 1;
        }

        return new VaultEntry
        {
            Id = Crypto.Hex(fileId),
            Name = Path.GetFileName(relative),
            Rel = relative,
            Size = size,
            Offset = offset,
            Chunks = chunkIndex,
            Nonce = Convert.ToBase64String(nonceBase),
            Sha256 = Crypto.Hex(sha.GetHashAndReset())
        };
    }

    private static void WriteChunk(
        Stream output, AesGcm aes, byte[] fileId, byte[] nonceBase, int chunkIndex,
        ReadOnlySpan<byte> plain, bool isLast)
    {
        var nonce = new byte[Crypto.NonceSize];
        nonceBase.CopyTo(nonce, 0);
        BinaryPrimitives.WriteInt32BigEndian(nonce.AsSpan(8), chunkIndex);

        var aad = BuildAad(fileId, chunkIndex, isLast);
        var cipher = new byte[plain.Length];
        var tag = new byte[Crypto.TagSize];
        aes.Encrypt(nonce, plain, cipher, tag, aad);

        var length = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(length, cipher.Length + Crypto.TagSize);
        output.Write(length);
        output.Write(cipher);
        output.Write(tag);
    }

    private static async Task DecryptFileAsync(
        Stream input, VaultEntry entry, byte[] chunkKey, string destination, CancellationToken cancellationToken)
    {
        var fileId = Crypto.FromHex(entry.Id);
        var nonceBase = Convert.FromBase64String(entry.Nonce);
        var fileKey = Crypto.Derive(chunkKey, fileId);

        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var aes = new AesGcm(fileKey, Crypto.TagSize);

        input.Position = entry.Offset;
        var directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await using var target = new FileStream(
            destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            1 << 20, FileOptions.Asynchronous);

        var lengthBytes = new byte[4];
        for (var i = 0; i < entry.Chunks; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await input.ReadExactlyAsync(lengthBytes, cancellationToken).ConfigureAwait(false);
            var cipherLength = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
            if (cipherLength < Crypto.TagSize)
                throw new InvalidDataException("Повреждённый блок данных.");

            var blob = new byte[cipherLength];
            await input.ReadExactlyAsync(blob, cancellationToken).ConfigureAwait(false);

            var cipher = blob.AsSpan(0, cipherLength - Crypto.TagSize).ToArray();
            var tag = blob.AsSpan(cipherLength - Crypto.TagSize, Crypto.TagSize).ToArray();

            var nonce = new byte[Crypto.NonceSize];
            nonceBase.CopyTo(nonce, 0);
            BinaryPrimitives.WriteInt32BigEndian(nonce.AsSpan(8), i);

            var isLast = i == entry.Chunks - 1;
            var aad = BuildAad(fileId, i, isLast);
            var plain = new byte[cipher.Length];
            aes.Decrypt(nonce, cipher, tag, plain, aad);

            await target.WriteAsync(plain, cancellationToken).ConfigureAwait(false);
            sha.AppendData(plain);
        }

        await target.FlushAsync(cancellationToken).ConfigureAwait(false);

        var actual = Crypto.Hex(sha.GetHashAndReset());
        if (!string.Equals(actual, entry.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Файл «{entry.Rel}» повреждён: не совпала контрольная сумма.");
    }

    private static byte[] BuildAad(byte[] fileId, int chunkIndex, bool isLast)
    {
        var aad = new byte[16 + 4 + 1];
        Array.Copy(fileId, 0, aad, 0, 16);
        BinaryPrimitives.WriteInt32BigEndian(aad.AsSpan(16), chunkIndex);
        aad[20] = isLast ? (byte)1 : (byte)0;
        return aad;
    }

    private static byte[] UnwrapKey(Stream stream, Header header)
    {
        if (header.KeyBlobLength <= 0 || header.KeyBlobOffset < Header.Size)
            throw new InvalidDataException("Повреждён заголовок сейфа.");

        stream.Position = header.KeyBlobOffset;
        var blob = new byte[header.KeyBlobLength];
        stream.ReadExactly(blob, 0, blob.Length);

        var key = Dpapi.Unprotect(blob, header.VaultId);
        if (key.Length != Crypto.KeySize)
            throw new InvalidDataException("Повреждён ключ сейфа.");

        var macKey = Crypto.Derive(key, "mac");
        var expected = HMACSHA256.HashData(macKey, header.Raw.AsSpan(0, 144));
        if (!CryptographicOperations.FixedTimeEquals(expected.AsSpan(0, 16), header.Mac))
            throw new InvalidDataException("Заголовок сейфа повреждён или изменён.");

        return key;
    }

    private static Manifest ReadManifest(Stream stream, Header header, byte[] key)
    {
        if (header.ManifestLength < Crypto.NonceSize + Crypto.TagSize ||
            header.ManifestOffset < Header.Size ||
            header.ManifestOffset + header.ManifestLength > stream.Length)
            throw new InvalidDataException("Повреждён манифест сейфа.");

        stream.Position = header.ManifestOffset;
        var blob = new byte[header.ManifestLength];
        stream.ReadExactly(blob, 0, blob.Length);

        var nonce = blob.AsSpan(0, Crypto.NonceSize).ToArray();
        var tag = blob.AsSpan(blob.Length - Crypto.TagSize, Crypto.TagSize).ToArray();
        var cipher = blob.AsSpan(Crypto.NonceSize, blob.Length - Crypto.NonceSize - Crypto.TagSize).ToArray();

        var manifestKey = Crypto.Derive(key, "manifest");
        var json = Crypto.Decrypt(manifestKey, nonce, cipher, tag, header.VaultId);
        var manifest = JsonSerializer.Deserialize<Manifest>(json)
                       ?? throw new InvalidDataException("Манифест пуст.");

        if (manifest.UnlockAt != header.UnlockAt)
            throw new InvalidDataException("Дата в заголовке не совпадает с датой внутри сейфа.");

        return manifest;
    }

    private static FileStream OpenRead(string path) => new(
        path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20,
        FileOptions.RandomAccess);

    private static int Percent(long done, long total) =>
        total <= 0 ? 0 : (int)Math.Clamp(done * 100 / total, 0, 100);

    private static List<SourceFile> Expand(IEnumerable<string> inputs)
    {
        var result = new List<SourceFile>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var input in inputs)
        {
            if (File.Exists(input))
            {
                if (seen.Add(input))
                    result.Add(new SourceFile(input, Path.GetFileName(input)));
                continue;
            }

            if (!Directory.Exists(input))
                continue;

            var root = new DirectoryInfo(input);
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            };

            foreach (var file in root.EnumerateFiles("*", options))
            {
                if (!seen.Add(file.FullName))
                    continue;
                var relative = Path.GetRelativePath(root.FullName, file.FullName);
                result.Add(new SourceFile(file.FullName, Path.Combine(root.Name, relative)));
            }
        }

        return result;
    }

    private static string SanitizeRel(string relative)
    {
        var parts = relative
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p != "." && p != ".." && p.Length > 0)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToArray();

        return parts.Length == 0 ? "file" : Path.Combine(parts);
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
            return path;

        var directory = Path.GetDirectoryName(path) ?? "";
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);

        for (var i = 1; i < 10000; i++)
        {
            var candidate = Path.Combine(directory, $"{name} ({i}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
                return candidate;
        }

        throw new IOException("Не удалось подобрать имя для восстановленного файла.");
    }

    private static void Replicate()
    {
        foreach (var replica in AppPaths.Replicas())
        {
            try
            {
                var directory = Path.GetDirectoryName(replica);
                if (string.IsNullOrEmpty(directory)) continue;
                Directory.CreateDirectory(directory);
                File.Copy(AppPaths.VaultFile, replica, true);
                AppPaths.HideFile(replica);
            }
            catch { }
        }
    }

    private static void SecureDelete(IReadOnlyList<SourceFile> files, IEnumerable<string> inputs)
    {
        foreach (var file in files)
        {
            try
            {
                var info = new FileInfo(file.Full);
                if (!info.Exists) continue;

                using (var stream = new FileStream(file.Full, FileMode.Open, FileAccess.Write, FileShare.None))
                {
                    var zeros = new byte[1 << 20];
                    var left = stream.Length;
                    while (left > 0)
                    {
                        var count = (int)Math.Min(zeros.Length, left);
                        stream.Write(zeros, 0, count);
                        left -= count;
                    }
                    stream.Flush(true);
                }

                File.SetAttributes(file.Full, FileAttributes.Normal);
                File.Delete(file.Full);
            }
            catch { }
        }

        foreach (var input in inputs)
        {
            try
            {
                if (!Directory.Exists(input)) continue;
                foreach (var directory in Directory.EnumerateDirectories(input, "*", SearchOption.AllDirectories)
                             .OrderByDescending(d => d.Length))
                {
                    if (!Directory.EnumerateFileSystemEntries(directory).Any())
                        Directory.Delete(directory);
                }

                if (!Directory.EnumerateFileSystemEntries(input).Any())
                    Directory.Delete(input);
            }
            catch { }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }
        catch { }
    }

    private sealed record SourceFile(string Full, string Rel);
}
