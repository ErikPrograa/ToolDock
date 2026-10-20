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
/// Lectura con reintentos y escritura atómica (archivo temporal + reemplazo) con copia .bak.
/// Un WriteAllText interrumpido al apagar Windows deja el JSON a medias; al siguiente
/// arranque ese archivo no se puede leer y, si se guarda encima, los ajustes desaparecen.
/// </summary>
internal static class DurableFile
{
    public const string BackupDirectoryName = "Backups_TD";

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private const int ReadAttempts = 8;

    /// <summary>Copia .bak dentro de Backups_TD, junto a la carpeta del archivo original.</summary>
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

    /// <summary>Lee la copia en Backups_TD. Si aún no existe, usa el .bak antiguo junto al archivo.</summary>
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
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Environment.ProcessId}.tmp");

        for (var attempt = 0; attempt < ReadAttempts; attempt++)
        {
            try
            {
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

                return true;
            }
            catch (Exception ex) when (IsTransient(ex) && attempt < ReadAttempts - 1)
            {
                Thread.Sleep(75 * (attempt + 1));
            }
            catch (Exception ex) when (IsTransient(ex))
            {
                TryDelete(tempPath);
                return false;
            }
        }

        TryDelete(tempPath);
        return false;
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
            // Si no se puede apartar, el llamador decidirá si es seguro volver a escribir.
        }
    }

    public static void DeleteFamily(string path)
    {
        TryDelete(path);
        TryDelete(GetBackupPath(path));
        TryDelete(path + ".bak");
        TryDelete(path + ".corrupt");
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
