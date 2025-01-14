using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;

namespace LocalAdmin.V2.IO.Logging
{
    public static class LogCleaner
    {
        private static bool _abort;
        private static Thread? _cleanupThread;
        private static DateTime? _lastCleanup;

        internal static void Initialize()
        {
            if (_abort || _cleanupThread != null)
                return;

            _cleanupThread = new Thread(Cleanup)
            {
                Name = "LocalAdmin logs cleanup",
                Priority = ThreadPriority.Lowest,
                IsBackground = true
            };

            _cleanupThread.Start();
        }

        internal static bool Abort() => _abort = true;

        private static void Cleanup()
        {
            while (!_abort)
            {
                if (_abort)
                    return;

                DateTime now = DateTime.Now;

                if (_lastCleanup == null)
                {
                    _lastCleanup = DateTime.UtcNow;

                    for (uint i = 0; i < 15 && !_abort; i++)
                        Thread.Sleep(1000);
                }
                else if (now.DayOfYear == _lastCleanup.Value.DayOfYear)
                {
                    for (uint i = 0; i < 1800 && !_abort; i++) //30 * 60
                        Thread.Sleep(1000);
                    continue;
                }
                else
                {
                    for (uint i = 0; i < 1800 && !_abort; i++) //30 * 60
                        Thread.Sleep(1000);
                }

                if (Core.LocalAdmin.Configuration!.DeleteOldRoundLogs ||
                    Core.LocalAdmin.Configuration!.CompressOldRoundLogs)
                {
                    string? root = Core.LocalAdmin.GameLogsPath ?? $"{Core.LocalAdmin.GameUserDataRoot}ServerLogs{Path.DirectorySeparatorChar}{Core.LocalAdmin.GamePort}{Path.DirectorySeparatorChar}";

                    if (Directory.Exists(root))
                    {
                        foreach (string? file in Directory.GetFiles(root, "Round *", SearchOption.TopDirectoryOnly))
                        {
                            string stage = string.Empty;

                            try
                            {
                                if (_abort)
                                    return;

                                stage = "Processing file name";
                                string? name = Path.GetFileName(file);
                                if (name.Length < 25 || !name.StartsWith("Round ", StringComparison.Ordinal) ||
                                    !name.EndsWith(".txt", StringComparison.Ordinal))
                                    continue;

                                string? d = name.Substring(6, 10);
                                if (!DateTime.TryParseExact(d, "yyyy-MM-dd",
                                    CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime date))
                                    continue;

                                TimeSpan diff = now - date;

                                stage = "File name processed";
                                if (Core.LocalAdmin.Configuration!.DeleteOldRoundLogs && diff.TotalDays >
                                    Core.LocalAdmin.Configuration!.RoundLogsExpirationDays)
                                {
                                    stage = "Deletion";
                                    File.Delete(file);
                                    continue;
                                }

                                if (Core.LocalAdmin.Configuration!.CompressOldRoundLogs && diff.TotalDays >
                                    Core.LocalAdmin.Configuration!.RoundLogsCompressionThresholdDays)
                                {
                                    stage = "Compression - p2";
                                    string? p2 = root + "LA-ToCompress-" + d + Path.DirectorySeparatorChar;
                                    if (!Directory.Exists(p2))
                                        Directory.CreateDirectory(p2);

                                    stage = "Compression - moving";
                                    File.Move(file, p2 + name);
                                }

                                stage = "End";
                            }
                            catch (Exception e)
                            {
                                ConsoleUtil.WriteLine($"[Log Maintenance] Failed to process old round log {file}. Processing stage: {stage}. Exception: {e.Message}", ConsoleColor.Red);
                            }
                        }

                        foreach (string? file in Directory.GetFiles(root, "Round Logs Archive *", SearchOption.TopDirectoryOnly))
                        {
                            string stage = string.Empty;

                            try
                            {
                                if (_abort)
                                    return;

                                stage = "Processing file name";
                                string? name = Path.GetFileName(file);
                                if (name.Length < 29 || !name.StartsWith("Round Logs Archive ", StringComparison.Ordinal) ||
                                    !name.EndsWith(".zip", StringComparison.Ordinal))
                                    continue;

                                string? d = name.Substring(19, 10);
                                if (!DateTime.TryParseExact(d, "yyyy-MM-dd",
                                    CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime date))
                                    continue;

                                TimeSpan diff = now - date;

                                stage = "File name processed";
                                if (Core.LocalAdmin.Configuration!.DeleteOldRoundLogs && diff.TotalDays >
                                    Core.LocalAdmin.Configuration!.RoundLogsExpirationDays)
                                {
                                    stage = "Deletion";
                                    File.Delete(file);
                                    continue;
                                }

                                stage = "End";
                            }
                            catch (Exception e)
                            {
                                ConsoleUtil.WriteLine($"[Log Maintenance] Failed to process old archive of round logs {file}. Processing stage: {stage}. Exception: {e.Message}", ConsoleColor.Red);
                            }
                        }

                        foreach (string? dir in Directory.GetDirectories(root, "LA-ToCompress-*", SearchOption.TopDirectoryOnly))
                        {
                            string stage = string.Empty;

                            try
                            {
                                if (_abort)
                                    return;

                                stage = "Processing directory name";
                                string? name = new DirectoryInfo(dir).Name;
                                if (name.Length != 24 || !name.StartsWith("LA-ToCompress-", StringComparison.Ordinal))
                                    continue;

                                string? d = root + "Round Logs Archive " + name[14..];

                                if (Directory.Exists(d))
                                {
                                    ConsoleUtil.WriteLine($"[Log Maintenance] Failed to compress old round log directory. Target directory already exists.", ConsoleColor.Red);
                                    continue;
                                }

                                if (File.Exists(d + ".zip"))
                                {
                                    ConsoleUtil.WriteLine($"[Log Maintenance] Failed to compress old round log directory. Target ZIP file already exists.", ConsoleColor.Red);
                                    continue;
                                }

                                stage = "Renaming directory";
                                Directory.Move(dir, d);

                                stage = "Compressing directory";
                                ZipFile.CreateFromDirectory(d, d + ".zip", CompressionLevel.Optimal, true, Encoding.UTF8);

                                stage = "Removing uncompressed directory";
                                Directory.Delete(d, true);

                                stage = "End";
                            }
                            catch (Exception e)
                            {
                                ConsoleUtil.WriteLine($"[Log Maintenance] Failed to compress old round log directory {dir}. Processing stage: {stage}. Exception: {e.Message}", ConsoleColor.Red);
                            }
                        }
                    }
                }

                if (Core.LocalAdmin.Configuration!.LaDeleteOldLogs)
                {
                    string? root = Core.LocalAdmin.LaLogsPath ?? $"{Core.LocalAdmin.GameUserDataRoot}{Logger.LogFolderName}{Path.DirectorySeparatorChar}{Core.LocalAdmin.GamePort}{Path.DirectorySeparatorChar}";
                    if (Directory.Exists(root))
                    {
                        foreach (string? file in Directory.GetFiles(root, "LocalAdmin Log *", SearchOption.TopDirectoryOnly))
                        {
                            string stage = string.Empty;

                            try
                            {
                                if (_abort)
                                    return;

                                stage = "Processing file name";
                                string? name = Path.GetFileName(file);
                                if (name.Length < 34 || !name.StartsWith("LocalAdmin Log ", StringComparison.Ordinal) ||
                                    !name.EndsWith(".txt", StringComparison.Ordinal))
                                    continue;

                                string? d = name.Substring(15, 10);
                                if (!DateTime.TryParseExact(d, "yyyy-MM-dd",
                                    CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime date))
                                    continue;

                                TimeSpan diff = (now - date);

                                stage = "File name processed";
                                if (diff.TotalDays > Core.LocalAdmin.Configuration!.LaLogsExpirationDays)
                                {
                                    stage = "Deletion";
                                    File.Delete(file);
                                    continue;
                                }

                                stage = "End";
                            }
                            catch (Exception e)
                            {
                                ConsoleUtil.WriteLine($"[Log Maintenance] Failed to process old LocalAdmin log {file}. Processing stage: {stage}. Exception: {e.Message}", ConsoleColor.Red);
                            }
                        }
                    }
                }
            }
        }
    }
}