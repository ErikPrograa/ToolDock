using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace ToolDock.Services;

internal enum TextReadStatus
{
    Missing,
    Ok,
    IoFailure,
    Corrupt
}

internal readonly record struct TextRead(string? Text, TextReadStatus Status);

/// <summary>
/// Read with retries and atomic write (temp file plus replace) with a .bak copy.
/// A WriteAllText interrupted when Windows shuts down leaves the JSON half-written; on the
/// next launch that file cannot be read and, if it is saved over, the settings disappear.
/// </summary>
internal static class DurableFile
{
    public const string BackupDirectoryName = "Backups_TD";
    public const string TempDirectoryName = "Temp_TD";

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private const int ReadAttempts = 8;

    /// <summary>.bak copy inside Backups_TD, next to the original file's folder.</summary>
    public static string GetBackupPath(string path)
    {
        var full = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(full);
        if (string.IsNullOrEmpty(directory))
        {
            directory = full;
        }

        return Path.Combine(directory, BackupDirectoryName, Path.GetFileName(full) + ".bak");
    }

    /// <summary>Reads the copy in Backups_TD. If it does not exist yet, uses the old .bak next to the file.</summary>
    public static TextRead ReadBackup(string path)
    {
        var current = Read(GetBackupPath(path));
        if (current.Status == TextReadStatus.Ok)
        {
            return current;
        }

        var legacy = Read(path + ".bak");
        if (legacy.Status == TextReadStatus.Ok)
        {
            return legacy;
        }

        return current.Status == TextReadStatus.Missing ? legacy : current;
    }

    public static TextRead Read(string path)
    {
        if (!File.Exists(path))
        {
            return new TextRead(null, TextReadStatus.Missing);
        }

        using var gate = new FileGate(path);
        for (var attempt = 0; attempt < ReadAttempts; attempt++)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                var text = reader.ReadToEnd();
                if (string.IsNullOrWhiteSpace(text))
                {
                    return new TextRead(text, TextReadStatus.Corrupt);
                }

                return new TextRead(text, TextReadStatus.Ok);
            }
            catch (Exception ex) when (IsTransient(ex) && attempt < ReadAttempts - 1)
            {
                Thread.Sleep(75 * (attempt + 1));
            }
            catch (Exception ex) when (IsTransient(ex))
            {
                return new TextRead(null, TextReadStatus.IoFailure);
            }
        }

        return new TextRead(null, TextReadStatus.IoFailure);
    }

    public static bool Write(string path, string contents, bool preserveBackup = false)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (string.IsNullOrEmpty(directory))
        {
            return false;
        }

        using var gate = new FileGate(path);
        Directory.CreateDirectory(directory);
        // Temps stay in Temp_TD so a killed write can be removed on the next launch.
        var tempDirectory = Path.Combine(directory, TempDirectoryName);
        var tempPath = Path.Combine(tempDirectory, $".{Path.GetFileName(path)}.{Environment.ProcessId}.tmp");

        for (var attempt = 0; attempt < ReadAttempts; attempt++)
        {
            try
            {
                Directory.CreateDirectory(tempDirectory);
                using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                using (var writer = new StreamWriter(stream, Utf8))
                {
                    writer.Write(contents);
                    writer.Flush();
                    stream.Flush(true);
                }

                if (File.Exists(path))
                {
                    string? backupPath = null;
                    if (!preserveBackup)
                    {
                        backupPath = GetBackupPath(path);
                        var backupDirectory = Path.GetDirectoryName(backupPath);
                        if (!string.IsNullOrEmpty(backupDirectory))
                        {
                            Directory.CreateDirectory(backupDirectory);
                        }
                    }

                    File.Replace(tempPath, path, destinationBackupFileName: backupPath, ignoreMetadataErrors: true);
                    if (!preserveBackup)
                    {
                        TryDelete(path + ".bak");
                    }
                }
                else
                {
                    File.Move(tempPath, path);
                }

                TryDeleteDirectoryIfEmpty(tempDirectory);
                return true;
            }
            catch (Exception ex) when (IsTransient(ex) && attempt < ReadAttempts - 1)
            {
                Thread.Sleep(75 * (attempt + 1));
            }
            catch (Exception ex) when (IsTransient(ex))
            {
                TryDelete(tempPath);
                TryDeleteDirectoryIfEmpty(tempDirectory);
                return false;
            }
        }

        TryDelete(tempPath);
        TryDeleteDirectoryIfEmpty(tempDirectory);
        return false;
    }

    /// <summary>Deletes leftover write temps. A temp still open by another instance is left alone.</summary>
    public static void ClearStaleTemps(string dataDirectory)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory))
        {
            return;
        }

        var tempDirectory = Path.Combine(dataDirectory, TempDirectoryName);
        if (!Directory.Exists(tempDirectory))
        {
            return;
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(tempDirectory, "*.tmp"))
            {
                TryDelete(file);
            }
        }
        catch
        {
            return;
        }

        TryDeleteDirectoryIfEmpty(tempDirectory);
    }

    public static void Quarantine(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var dest = path + ".corrupt";
        TryDelete(dest);
        try
        {
            File.Move(path, dest);
        }
        catch
        {
            // If it cannot be moved aside, the caller decides whether it is safe to write again.
        }
    }

    public static void DeleteFamily(string path)
    {
        TryDelete(path);
        TryDelete(GetBackupPath(path));
        TryDelete(path + ".bak");
        TryDelete(path + ".corrupt");
    }

    private static void TryDeleteDirectoryIfEmpty(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            using var entries = Directory.EnumerateFileSystemEntries(directory).GetEnumerator();
            if (!entries.MoveNext())
            {
                Directory.Delete(directory);
            }
        }
        catch
        {
            // ignore
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // ignore
        }
    }

    private static bool IsTransient(Exception ex) =>
        ex is IOException or UnauthorizedAccessException;

    private sealed class FileGate : IDisposable
    {
        private readonly Mutex _mutex;
        private readonly bool _owned;

        public FileGate(string path)
        {
            var full = Path.GetFullPath(path);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full.ToUpperInvariant())))[..16];
            _mutex = new Mutex(initiallyOwned: false, name: $@"Local\ToolDockFile_{hash}");
            try
            {
                _owned = _mutex.WaitOne(TimeSpan.FromSeconds(5));
            }
            catch (AbandonedMutexException)
            {
                _owned = true;
            }
        }

        public void Dispose()
        {
            try
            {
                if (_owned)
                {
                    _mutex.ReleaseMutex();
                }
            }
            catch
            {
                // ignore
            }

            _mutex.Dispose();
        }
    }
}
