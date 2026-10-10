#if SHOTS
using System.Collections;
using System.IO;
using System.Linq;
using MelonLoader;
using UI.Common;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WorldBackups
{
    // Dev-only: takes the README screenshots, compiled with -p:DefineConstants=SHOTS. Never in the release build.
    public partial class WorldBackupsMod
    {
        private const string ShotWorld = "Outpost Alpha";
        private static readonly string ShotDir = Path.Combine(Path.GetTempPath(), "wb_shots");

        private static IEnumerator ShotWait(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end)
                yield return null;
        }

        private IEnumerator Capture(string name)
        {
            yield return ShotWait(1f);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(ShotDir, name + ".png"));
            yield return ShotWait(1f);
            LoggerInstance.Msg($"[SHOTS] {name}");
        }

        private static IEnumerator OpenRow(Component row)
        {
            row?.GetComponent<MenuButtonElement>()?.OpenMenu();
            yield return ShotWait(1f);
        }

        private static BackupRow ShotRow(string label) =>
            Object.FindObjectsByType<BackupRow>(FindObjectsSortMode.None).FirstOrDefault(r => r.Backup != null && r.Backup.Label == label);

        private IEnumerator Screenshots()
        {
            Directory.CreateDirectory(ShotDir);
            while (SceneManager.GetActiveScene().buildIndex != 0)
                yield return null;
            yield return ShotWait(8f);

            string worldDir = Store.WorldDir(ShotWorld);
            Directory.CreateDirectory(worldDir);
            File.Copy(Path.Combine(Application.streamingAssetsPath, "NewWorldSave.json"), Path.Combine(worldDir, "world.json"), true);
            File.Copy(Path.Combine(Application.streamingAssetsPath, "NewWorldSave.jpg"), Path.Combine(worldDir, "world.jpg"), true);
            File.Copy(Path.Combine(worldDir, "world.json"), Path.Combine(worldDir, "backup_" + System.DateTime.Now.AddMinutes(-30).ToString("yyyy-MM-dd_HH-mm-ss") + ".json"), true);
            Store.CreateBackup(ShotWorld, "", BackupKind.Auto);
            yield return ShotWait(1.2f);
            Store.CreateBackup(ShotWorld, "Before the reactor upgrade", BackupKind.Manual);
            yield return ShotWait(1.2f);
            Store.CreateBackup(ShotWorld, "Base finished", BackupKind.Manual);
            RefreshGameWorldList();

            Object.FindObjectsByType<MenuButtonElement>(FindObjectsSortMode.None).FirstOrDefault(b => b.name == "WorldBackupsMenuButton")?.OpenMenu();
            yield return ShotWait(1f);
            yield return Capture("menu_worlds");

            yield return OpenRow(Object.FindObjectsByType<WorldRef>(FindObjectsSortMode.None).FirstOrDefault(w => w.World == ShotWorld && w.GetComponent<BackupRow>() != null));
            yield return Capture("menu_backups");
            yield return OpenRow(ShotRow("Base finished"));
            yield return Capture("menu_details");

            LaunchWorld(ShotWorld);
            while (SceneManager.GetActiveScene().name != MainGameScene)
                yield return null;
            yield return ShotWait(25f);
            Object.FindFirstObjectByType<Players.Services.PlayerPauseToggler>()?.TogglePause();
            yield return ShotWait(1.5f);
            Object.FindObjectsByType<MenuButtonElement>(FindObjectsSortMode.None).FirstOrDefault(b => b.name == PauseButtonName)?.OpenMenu();
            yield return ShotWait(1f);
            yield return Capture("pause_backups");
            yield return OpenRow(ShotRow("Before the reactor upgrade"));
            yield return Capture("pause_details");

            LoggerInstance.Msg("[SHOTS] DONE");
            Application.Quit();
        }
    }
}
#endif
