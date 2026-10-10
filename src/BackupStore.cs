using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace WorldBackups
{
    public enum BackupKind
    {
        Manual,
        Auto,
        Safety,
        Game
    }

    [Serializable]
    public class BackupInfo
    {
        public string label = "";
        public string kind = nameof(BackupKind.Manual);
        public string createdUtc = "";
        public string gameVersion = "";
        public string world = "";
    }

    public class BackupEntry
    {
        public string World;
        public string Path;
        public string Label;
        public BackupKind Kind;
        public DateTime Created;
        public long SizeBytes;
        public string ScreenshotPath;

        public bool IsGameBackup => Kind == BackupKind.Game;
    }

    /// <summary>
    /// Snapshots live in persistentDataPath/WorldBackups/&lt;world&gt;/&lt;timestamp&gt;/ as a full copy of the world folder,
    /// next to the game's own WorldSaves directory so the game never lists them as worlds.
    /// The game's own rolling "backup_*.json" autosaves are listed too, read-only.
    /// </summary>
    public class BackupStore
    {
        public const string WorldFileName = "world";
        public const string InfoFileName = "backup-info.json";
        private const string GameBackupPrefix = "backup_";
        private const string TimestampFormat = "yyyy-MM-dd_HH-mm-ss";

        public string SavesRoot { get; }
        public string BackupsRoot { get; }

        public BackupStore(string persistentDataPath)
        {
            // Unity returns forward slashes; normalize so paths compare equal to DirectoryInfo.FullName.
            SavesRoot = System.IO.Path.GetFullPath(Path.Combine(persistentDataPath, "WorldSaves"));
            BackupsRoot = System.IO.Path.GetFullPath(Path.Combine(persistentDataPath, "WorldBackups"));
        }

        public string WorldDir(string world) => Path.Combine(SavesRoot, world);

        public string WorldSaveFile(string world) => Path.Combine(WorldDir(world), WorldFileName + ".json");

        public List<string> ListWorlds()
        {
            if (!Directory.Exists(SavesRoot))
                return new List<string>();

            return new DirectoryInfo(SavesRoot).GetDirectories()
                .Where(d => File.Exists(Path.Combine(d.FullName, WorldFileName + ".json")))
                .OrderByDescending(d => File.GetLastWriteTime(Path.Combine(d.FullName, WorldFileName + ".json")))
                .Select(d => d.Name)
                .ToList();
        }

        public bool WorldExists(string world) => File.Exists(WorldSaveFile(world));

        public List<BackupEntry> ListBackups(string world)
        {
            var result = new List<BackupEntry>();

            string root = Path.Combine(BackupsRoot, world);
            if (Directory.Exists(root))
            {
                foreach (var dir in new DirectoryInfo(root).GetDirectories())
                {
                    if (!File.Exists(Path.Combine(dir.FullName, WorldFileName + ".json")))
                        continue;

                    BackupInfo info = ReadInfo(dir.FullName);
                    var entry = new BackupEntry
                    {
                        World = world,
                        Path = dir.FullName,
                        Label = info?.label ?? "",
                        Kind = ParseKind(info?.kind),
                        Created = ParseCreated(info, dir),
                        SizeBytes = DirectorySize(dir),
                        ScreenshotPath = ExistingOrNull(Path.Combine(dir.FullName, WorldFileName + ".jpg"))
                    };
                    result.Add(entry);
                }
            }

            string worldDir = WorldDir(world);
            if (Directory.Exists(worldDir))
            {
                foreach (var file in new DirectoryInfo(worldDir).GetFiles(GameBackupPrefix + "*.json"))
                {
                    result.Add(new BackupEntry
                    {
                        World = world,
                        Path = file.FullName,
                        Label = "",
                        Kind = BackupKind.Game,
                        Created = file.LastWriteTime,
                        SizeBytes = file.Length,
                        ScreenshotPath = ExistingOrNull(System.IO.Path.ChangeExtension(file.FullName, ".jpg"))
                    });
                }
            }

            return result.OrderByDescending(b => b.Created).ToList();
        }

        public BackupEntry CreateBackup(string world, string label, BackupKind kind)
        {
            if (kind == BackupKind.Game)
                throw new ArgumentException("Game backups are created by the game itself.");

            string source = WorldDir(world);
            if (!File.Exists(Path.Combine(source, WorldFileName + ".json")))
                throw new FileNotFoundException("World save not found", source);

            DateTime now = DateTime.Now;
            string name = now.ToString(TimestampFormat);
            string target = Path.Combine(BackupsRoot, world, name);
            for (int i = 2; Directory.Exists(target); i++)
                target = Path.Combine(BackupsRoot, world, $"{name}_{i}");

            target = System.IO.Path.GetFullPath(target);
            CopyDirectory(source, target);
            WriteInfo(target, new BackupInfo
            {
                label = label ?? "",
                kind = kind.ToString(),
                createdUtc = now.ToUniversalTime().ToString("o"),
                gameVersion = Application.version,
                world = world
            });

            return ListBackups(world).First(b => string.Equals(b.Path, target, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Deletes the oldest backups of the given kind beyond <paramref name="keep"/>.</summary>
        public void Prune(string world, BackupKind kind, int keep)
        {
            if (keep <= 0 || kind == BackupKind.Game)
                return;

            foreach (var old in ListBackups(world).Where(b => b.Kind == kind).Skip(keep))
                Delete(old);
        }

        /// <summary>Replaces the world folder with the backup content.</summary>
        public void Restore(BackupEntry backup, string targetWorld)
        {
            string worldDir = WorldDir(targetWorld);

            if (backup.IsGameBackup)
            {
                // Game autosave inside the world folder: swap it in as the main save, keep everything else.
                Directory.CreateDirectory(worldDir);
                File.Copy(backup.Path, Path.Combine(worldDir, WorldFileName + ".json"), true);
                string jpg = System.IO.Path.ChangeExtension(backup.Path, ".jpg");
                if (File.Exists(jpg))
                    File.Copy(jpg, Path.Combine(worldDir, WorldFileName + ".jpg"), true);
                Touch(Path.Combine(worldDir, WorldFileName + ".json"));
                return;
            }

            // Stage next to the world, then swap, so a failed copy never leaves a half-restored world.
            string staging = worldDir + ".restoring";
            string old = worldDir + ".old";
            DeleteDirectory(staging);
            DeleteDirectory(old);

            CopyDirectory(backup.Path, staging, skipFile: InfoFileName);

            if (Directory.Exists(worldDir))
                Directory.Move(worldDir, old);
            try
            {
                Directory.Move(staging, worldDir);
            }
            catch
            {
                if (Directory.Exists(old) && !Directory.Exists(worldDir))
                    Directory.Move(old, worldDir);
                throw;
            }

            DeleteDirectory(old);
            Touch(Path.Combine(worldDir, WorldFileName + ".json"));
        }

        public void Delete(BackupEntry backup)
        {
            if (backup.IsGameBackup)
            {
                File.Delete(backup.Path);
                string jpg = System.IO.Path.ChangeExtension(backup.Path, ".jpg");
                if (File.Exists(jpg))
                    File.Delete(jpg);
                return;
            }

            // Only ever delete inside our own backups folder.
            string full = System.IO.Path.GetFullPath(backup.Path);
            string root = System.IO.Path.GetFullPath(BackupsRoot) + System.IO.Path.DirectorySeparatorChar;
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Refusing to delete outside the backups folder: " + full);

            DeleteDirectory(full);

            string worldRoot = System.IO.Path.GetDirectoryName(full);
            if (Directory.Exists(worldRoot) && !Directory.EnumerateFileSystemEntries(worldRoot).Any())
                Directory.Delete(worldRoot);
        }

        public void SetLabel(BackupEntry backup, string label)
        {
            if (backup.IsGameBackup)
                return;

            BackupInfo info = ReadInfo(backup.Path) ?? new BackupInfo { world = backup.World, kind = backup.Kind.ToString() };
            info.label = label ?? "";
            WriteInfo(backup.Path, info);
            backup.Label = info.label;
        }

        public static string ValidateNewWorldName(string name, BackupStore store)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "empty";
            if (name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0 || name.Trim() != name || name.EndsWith("."))
                return "invalid";
            if (Directory.Exists(store.WorldDir(name)))
                return "exists";
            return null;
        }

        private static BackupKind ParseKind(string kind)
        {
            return Enum.TryParse(kind, out BackupKind parsed) ? parsed : BackupKind.Manual;
        }

        private static DateTime ParseCreated(BackupInfo info, DirectoryInfo dir)
        {
            if (info != null && DateTime.TryParse(info.createdUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime created))
                return created.ToLocalTime();
            return dir.CreationTime;
        }

        private static BackupInfo ReadInfo(string backupDir)
        {
            string path = Path.Combine(backupDir, InfoFileName);
            if (!File.Exists(path))
                return null;
            try
            {
                return JsonUtility.FromJson<BackupInfo>(File.ReadAllText(path));
            }
            catch
            {
                return null;
            }
        }

        private static void WriteInfo(string backupDir, BackupInfo info)
        {
            File.WriteAllText(Path.Combine(backupDir, InfoFileName), JsonUtility.ToJson(info, true));
        }

        private static string ExistingOrNull(string path) => File.Exists(path) ? path : null;

        private static void Touch(string file)
        {
            if (File.Exists(file))
                File.SetLastWriteTime(file, DateTime.Now);
        }

        private static long DirectorySize(DirectoryInfo dir)
        {
            try
            {
                return dir.GetFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
            }
            catch
            {
                return 0;
            }
        }

        private static void CopyDirectory(string source, string target, string skipFile = null)
        {
            Directory.CreateDirectory(target);

            foreach (string file in Directory.GetFiles(source))
            {
                string name = System.IO.Path.GetFileName(file);
                if (skipFile != null && string.Equals(name, skipFile, StringComparison.OrdinalIgnoreCase))
                    continue;
                File.Copy(file, Path.Combine(target, name), true);
            }

            foreach (string dir in Directory.GetDirectories(source))
                CopyDirectory(dir, Path.Combine(target, System.IO.Path.GetFileName(dir)), skipFile);
        }

        private static void DeleteDirectory(string path)
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
    }
}
