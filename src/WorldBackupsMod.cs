using System;
using System.Collections;
using System.IO;
using System.Linq;
using Core.Services;
using Managers.Interface;
using Managers.Interface.Services;
using MelonLoader;
using Saving.Interface.Model;
using Saving.Services;
using UI.MainMenu.Common;
using UnityEngine;
using UnityEngine.SceneManagement;

[assembly: MelonInfo(typeof(WorldBackups.WorldBackupsMod), "World Backups", "1.0.0", "tidic84")]
[assembly: MelonGame("CuriousOwlGames", "StellarDrive")]

namespace WorldBackups
{
    public partial class WorldBackupsMod : MelonMod
    {
        internal const string MainGameScene = "MainGame";
        internal const string StartServerPrefKey = "StartServer"; // same key as the game's own "start server" toggle

        internal static WorldBackupsMod Instance { get; private set; }

        private MelonPreferences_Entry<bool> _autoBackupOnLoad;
        private MelonPreferences_Entry<int> _maxAutoBackups;
        private MelonPreferences_Entry<bool> _safetyBackup;
        private MelonPreferences_Entry<int> _maxSafetyBackups;
        private MelonPreferences_Entry<bool> _showMenuButton;
        private MelonPreferences_Entry<string> _language;

        internal BackupStore Store { get; private set; }

        internal static bool InGame => SceneManager.GetActiveScene().name == MainGameScene;

        internal static bool IsClient =>
            MainGameSceneLoadData.Mode == MainGameSceneLoadData.GameMode.JoinGameSteam ||
            MainGameSceneLoadData.Mode == MainGameSceneLoadData.GameMode.JoinGameIp;

        public override void OnInitializeMelon()
        {
            Instance = this;
            var cat = MelonPreferences.CreateCategory("WorldBackups", "World Backups");
            _autoBackupOnLoad = cat.CreateEntry("AutoBackupOnWorldLoad", true, "Auto backup on world load", "Make a backup every time you load one of your worlds.");
            _maxAutoBackups = cat.CreateEntry("MaxAutoBackupsPerWorld", 10, "Max auto backups per world", "Oldest automatic backups beyond this count are deleted (0 = keep all).");
            _safetyBackup = cat.CreateEntry("SafetyBackupBeforeRestore", true, "Safety backup before restore", "Back up the current world before it gets overwritten by a restore.");
            _maxSafetyBackups = cat.CreateEntry("MaxSafetyBackupsPerWorld", 5, "Max safety backups per world", "Oldest safety backups beyond this count are deleted (0 = keep all).");
            _showMenuButton = cat.CreateEntry("ShowMainMenuButton", true, "Main menu entry", "Add a Backups entry to the main menu.");
            _language = cat.CreateEntry("Language", "auto", "Language", "auto (follows the game language), en or fr.");

            Store = new BackupStore(Application.persistentDataPath);
            InstallPauseMenuHooks();
            LoggerInstance.Msg($"Backups folder: {Store.BackupsRoot}");
#if SELFTEST
            MelonCoroutines.Start(SelfTest());
#endif
#if SHOTS
            MelonCoroutines.Start(Screenshots());
#endif
#if UIDUMP
            MelonCoroutines.Start(UiDump());
#endif
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            if (sceneName == MainGameScene)
            {
                if (_autoBackupOnLoad.Value && !IsClient && _deferred == null)
                    AutoBackupCurrentWorld();
            }
            else if (buildIndex == 0)
            {
                MelonCoroutines.Start(SetUpMainMenu());
            }
        }

        private IEnumerator SetUpMainMenu()
        {
            // Wait for the menu's own Awake/Start (world list, navigation) before cloning its pieces.
            for (int i = 0; i < 5; i++)
                yield return null;

            Loc.Init(_language.Value);
            try
            {
                NativeUi.CaptureTemplates(LoggerInstance);
                if (_showMenuButton.Value)
                    NativeUi.AddMainMenuEntry(LoggerInstance);
            }
            catch (Exception ex)
            {
                LoggerInstance.Error($"Main menu integration failed: {ex}");
            }

            if (_deferred != null)
                yield return RunDeferred();
        }

        // ---------------------------------------------------------------- world helpers

        internal string CurrentWorldName()
        {
            string file = MainGameSceneLoadData.SaveFileFullName;
            if (string.IsNullOrEmpty(file))
                return null;
            try
            {
                string dir = Path.GetDirectoryName(Path.GetFullPath(file));
                string parent = Path.GetDirectoryName(dir);
                if (string.Equals(Path.GetFullPath(parent), Store.SavesRoot, StringComparison.OrdinalIgnoreCase))
                    return Path.GetFileName(dir);
            }
            catch
            {
            }
            return null;
        }

        private void AutoBackupCurrentWorld()
        {
            string world = CurrentWorldName();
            if (world == null || !Store.WorldExists(world))
                return;
            try
            {
                Store.CreateBackup(world, "", BackupKind.Auto);
                Store.Prune(world, BackupKind.Auto, _maxAutoBackups.Value);
                LoggerInstance.Msg($"Automatic backup of '{world}' created.");
            }
            catch (Exception ex)
            {
                LoggerInstance.Error($"Automatic backup of '{world}' failed: {ex}");
            }
        }

        // ---------------------------------------------------------------- actions used by the UI

        internal bool IsBusy { get; private set; }

        /// <summary>Creates a manual backup. When it is the running world, the game is saved first so the backup is up to date.</summary>
        internal void CreateBackup(string world, string label, Action<BackupEntry> done, Action<string> fail)
        {
            if (InGame && !IsClient && world == CurrentWorldName())
            {
                var saver = ServiceLocator.GetService<GameSaver>();
                if (saver != null)
                {
                    MelonCoroutines.Start(SaveGameThen(saver, () => FinishBackup(world, label, done, fail)));
                    return;
                }
            }
            FinishBackup(world, label, done, fail);
        }

        private void FinishBackup(string world, string label, Action<BackupEntry> done, Action<string> fail)
        {
            try
            {
                BackupEntry entry = Store.CreateBackup(world, label, BackupKind.Manual);
                LoggerInstance.Msg($"Backup of '{world}' created: {entry.Path}");
                done?.Invoke(entry);
            }
            catch (Exception ex)
            {
                LoggerInstance.Error(ex.ToString());
                fail?.Invoke(ex.Message);
            }
        }

        private IEnumerator SaveGameThen(GameSaver saver, Action then)
        {
            IsBusy = true;
            // The game screenshots the screen while saving: keep the pause menu out of the picture.
            var hidden = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)
                .Where(c => c.isRootCanvas && c.enabled && c.name.StartsWith("PauseCanvas", StringComparison.Ordinal))
                .ToList();
            foreach (var c in hidden)
                c.enabled = false;
            yield return null;

            try
            {
                if (!saver.IsCurrentlySaving())
                    saver.SaveGame(new SaveGameParams { IsAutomatic = false });
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"Game save failed: {ex.Message}");
            }

            float deadline = Time.realtimeSinceStartup + 15f;
            while (saver != null && saver.IsCurrentlySaving() && Time.realtimeSinceStartup < deadline)
                yield return null;

            foreach (var c in hidden)
                if (c != null)
                    c.enabled = true;
            IsBusy = false;
            then();
        }

        /// <summary>True when restoring this backup has to leave the running game first.</summary>
        internal bool RestoreLeavesGame(BackupEntry backup) => InGame && backup.World == CurrentWorldName();

        internal void Restore(BackupEntry backup, Action done, Action<string> fail)
        {
            if (RestoreLeavesGame(backup))
            {
                LeaveGameThen(backup, launch: false);
                return;
            }
            if (RestoreInto(backup, backup.World, fail))
                done?.Invoke();
        }

        internal void Load(BackupEntry backup, Action<string> fail)
        {
            if (InGame)
            {
                LeaveGameThen(backup, launch: true);
                return;
            }
            if (RestoreInto(backup, backup.World, fail))
                LaunchWorld(backup.World);
        }

        internal bool RestoreAsNewWorld(BackupEntry backup, string name, out string error)
        {
            error = BackupStore.ValidateNewWorldName(name, Store);
            if (error != null)
            {
                error = Loc.T(error == "empty" ? "NameEmpty" : error == "exists" ? "NameExists" : "NameInvalid");
                return false;
            }
            try
            {
                Store.Restore(backup, name);
                RefreshGameWorldList();
                LoggerInstance.Msg($"Backup '{backup.Path}' restored as new world '{name}'.");
                return true;
            }
            catch (Exception ex)
            {
                LoggerInstance.Error(ex.ToString());
                error = ex.Message;
                return false;
            }
        }

        internal bool Delete(BackupEntry backup, out string error)
        {
            error = null;
            try
            {
                Store.Delete(backup);
                return true;
            }
            catch (Exception ex)
            {
                LoggerInstance.Error(ex.ToString());
                error = ex.Message;
                return false;
            }
        }

        private bool RestoreInto(BackupEntry backup, string world, Action<string> fail)
        {
            try
            {
                if (_safetyBackup.Value && Store.WorldExists(world))
                {
                    Store.CreateBackup(world, "", BackupKind.Safety);
                    Store.Prune(world, BackupKind.Safety, _maxSafetyBackups.Value);
                }

                Store.Restore(backup, world);
                LoggerInstance.Msg($"Restored '{backup.Path}' into world '{world}'.");
                RefreshGameWorldList();
                return true;
            }
            catch (Exception ex)
            {
                LoggerInstance.Error(ex.ToString());
                fail?.Invoke(ex.Message);
                return false;
            }
        }

        private class DeferredRestore
        {
            public BackupEntry Backup;
            public bool Launch;
        }

        private DeferredRestore _deferred;

        /// <summary>Saves the running world (so the safety backup holds the latest progress), quits to the main menu,
        /// then <see cref="RunDeferred"/> restores (and optionally relaunches) once the menu is up.</summary>
        private void LeaveGameThen(BackupEntry backup, bool launch)
        {
            _deferred = new DeferredRestore { Backup = backup, Launch = launch };
            var saver = IsClient || CurrentWorldName() == null ? null : ServiceLocator.GetService<GameSaver>();
            if (saver != null)
                MelonCoroutines.Start(SaveGameThen(saver, QuitToMainMenu));
            else
                QuitToMainMenu();
        }

        private void QuitToMainMenu()
        {
            LoggerInstance.Msg("Leaving the world to restore a backup.");
            try
            {
                // Same as the pause menu's "quit to main menu" button.
                ServiceLocator.GetService<INetworkConnectionStopService>()?.StopConnection();
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"StopConnection failed: {ex.Message}");
            }
            SceneManager.LoadScene(0, LoadSceneMode.Single);
        }

        private IEnumerator RunDeferred()
        {
            DeferredRestore job = _deferred;
            _deferred = null;
            yield return null;

            string error = null;
            if (!RestoreInto(job.Backup, job.Backup.World, e => error = e))
            {
                NativeUi.ShowMessageInMainMenu(Loc.T("Error", error));
                yield break;
            }

            if (job.Launch)
                LaunchWorld(job.Backup.World);
            else
                NativeUi.ShowMessageInMainMenu(Loc.T("Restored", job.Backup.World));
        }

        internal void LaunchWorld(string world)
        {
            bool host = PlayerPrefs.GetInt(StartServerPrefKey, 0) > 0;
            MainGameSceneLoadData.Mode = host ? MainGameSceneLoadData.GameMode.HostGameSteam : MainGameSceneLoadData.GameMode.SinglePlayer;
            MainGameSceneLoadData.SaveFileFullName = new FileInfo(Store.WorldSaveFile(world)).FullName;
            LoggerInstance.Msg($"Launching world '{world}' ({MainGameSceneLoadData.Mode}).");
            SceneManager.LoadScene(MainGameScene, LoadSceneMode.Single);
        }

        /// <summary>Makes the main menu's own world list pick up restored/new worlds.</summary>
        internal void RefreshGameWorldList()
        {
            if (InGame)
                return;
            foreach (var list in Resources.FindObjectsOfTypeAll<WorldDirectoriesList>())
            {
                if (list == null || !list.gameObject.scene.IsValid())
                    continue;
                try
                {
                    list.UpdateList();
                }
                catch (Exception ex)
                {
                    // Lists that were never enabled have no services yet; they populate themselves on Start.
                    LoggerInstance.Msg($"World list refresh skipped: {ex.GetType().Name}");
                }
            }
        }

        internal void OpenFolder(string world)
        {
            string path = world != null ? Path.Combine(Store.BackupsRoot, world) : Store.BackupsRoot;
            if (!Directory.Exists(path))
                path = Directory.Exists(Store.BackupsRoot) ? Store.BackupsRoot : Application.persistentDataPath;
            Application.OpenURL("file:///" + path.Replace('\\', '/'));
        }
    }
}
