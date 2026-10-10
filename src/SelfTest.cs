#if SELFTEST
using System;
using System.Collections;
using System.IO;
using System.Linq;
using MelonLoader;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WorldBackups
{
    // Dev-only end-to-end check, compiled with -p:DefineConstants=SELFTEST. Never part of the release build.
    public partial class WorldBackupsMod
    {
        private const string TestWorld = "WB_SelfTest";
        private const string TestCopy = "WB_SelfTest Copy";
        private int _failures;

        private void Check(bool ok, string what)
        {
            if (!ok) _failures++;
            LoggerInstance.Msg($"[SELFTEST] {(ok ? "PASS" : "FAIL")} {what}");
        }

        private static IEnumerator Wait(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end)
                yield return null;
        }

        private IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            string path = Path.Combine(Path.GetTempPath(), name);
            ScreenCapture.CaptureScreenshot(path);
            yield return Wait(1f);
            LoggerInstance.Msg($"[SELFTEST] screenshot {path}");
        }

        private IEnumerator SelfTest()
        {
            while (SceneManager.GetActiveScene().buildIndex != 0)
                yield return null;
            yield return Wait(8f);

            // Fresh test world, made the same way the game's "new world" does.
            string worldDir = _store.WorldDir(TestWorld);
            Directory.CreateDirectory(worldDir);
            File.Copy(Path.Combine(Application.streamingAssetsPath, "NewWorldSave.json"), Path.Combine(worldDir, "world.json"), true);
            File.Copy(Path.Combine(Application.streamingAssetsPath, "NewWorldSave.jpg"), Path.Combine(worldDir, "world.jpg"), true);
            RefreshGameWorldList();

            SetOpen(true);
            _selectedWorld = TestWorld;
            ReloadBackups();
            Check(_worlds.Contains(TestWorld), "test world listed");

            FinishBackup(TestWorld, "selftest");
            var manual = _store.ListBackups(TestWorld).Where(b => b.Kind == BackupKind.Manual).ToList();
            Check(manual.Count == 1 && manual[0].Label == "selftest", "manual backup created with label");
            Check(manual.Count == 1 && File.Exists(Path.Combine(manual[0].Path, "world.json")) && manual[0].ScreenshotPath != null, "backup holds world.json + screenshot");

            _selectedBackup = manual.FirstOrDefault();
            yield return Wait(1f);
            yield return Shot("wb_menu.png");

            // Restore wipes changes made after the backup and keeps a safety copy of them.
            File.WriteAllText(Path.Combine(worldDir, "marker.txt"), "after backup");
            DoRestore(manual[0]);
            Check(!File.Exists(Path.Combine(worldDir, "marker.txt")), "restore replaced world content");
            var safety = _store.ListBackups(TestWorld).Where(b => b.Kind == BackupKind.Safety).ToList();
            Check(safety.Count == 1 && File.Exists(Path.Combine(safety[0].Path, "marker.txt")), "safety backup kept pre-restore state");
            Check(!Directory.Exists(worldDir + ".restoring") && !Directory.Exists(worldDir + ".old"), "no staging leftovers");

            // The game's own rolling autosaves show up and restore over world.json.
            string gameBackup = Path.Combine(worldDir, "backup_2026-01-01_00-00-00.json");
            File.WriteAllText(gameBackup, File.ReadAllText(Path.Combine(worldDir, "world.json")));
            var game = _store.ListBackups(TestWorld).FirstOrDefault(b => b.Kind == BackupKind.Game);
            Check(game != null, "game autosave listed");
            if (game != null)
            {
                DoRestore(game);
                Check(File.Exists(Path.Combine(worldDir, "world.json")) && File.Exists(gameBackup), "game autosave restored, original kept");
                DoDelete(_store.ListBackups(TestWorld).First(b => b.Kind == BackupKind.Game));
                Check(!File.Exists(gameBackup), "game autosave deleted");
            }

            _store.SetLabel(manual[0], "renamed");
            Check(_store.ListBackups(TestWorld).Any(b => b.Label == "renamed"), "label edit persisted");

            // Restore as a separate world.
            DoNewWorld(manual[0], TestWorld);
            Check(_statusIsError, "new world refuses existing name");
            DoNewWorld(manual[0], TestCopy);
            Check(_store.WorldExists(TestCopy), "restored as new world");
            Directory.Delete(_store.WorldDir(TestCopy), true);
            RefreshGameWorldList();

            // Load: restore + launch the MainGame scene.
            MainGameSceneLoadData_ResetForTest();
            DoLoad(manual[0]);
            float deadline = Time.realtimeSinceStartup + 60f;
            while (SceneManager.GetActiveScene().name != MainGameScene && Time.realtimeSinceStartup < deadline)
                yield return null;
            Check(SceneManager.GetActiveScene().name == MainGameScene, "load launched MainGame scene");
            Check(CurrentWorldName() == TestWorld, "launched world is the test world");
            Check(_store.ListBackups(TestWorld).Any(b => b.Kind == BackupKind.Auto), "auto backup on world load");

            yield return Wait(25f);
            SetOpen(true);
            yield return Wait(1f);
            yield return Shot("wb_ingame.png");

            int before = _store.ListBackups(TestWorld).Count(b => b.Kind == BackupKind.Manual);
            DoCreateBackup();
            deadline = Time.realtimeSinceStartup + 30f;
            while (_store.ListBackups(TestWorld).Count(b => b.Kind == BackupKind.Manual) == before && Time.realtimeSinceStartup < deadline)
                yield return null;
            Check(_store.ListBackups(TestWorld).Count(b => b.Kind == BackupKind.Manual) == before + 1, "in-game save + backup");
            Check(!_statusIsError, "no error status: " + _status);

            LoggerInstance.Msg(_failures == 0 ? "[SELFTEST] ALL PASSED" : $"[SELFTEST] {_failures} FAILURE(S)");
            yield return Wait(2f);
            Application.Quit();
        }

        private static void MainGameSceneLoadData_ResetForTest()
        {
            Managers.Interface.MainGameSceneLoadData.SaveFileFullName = null;
        }
    }
}
#endif
