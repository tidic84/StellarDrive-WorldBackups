#if SELFTEST
using System;
using System.Collections;
using System.IO;
using System.Linq;
using MelonLoader;
using TMPro;
using UI.Common;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace WorldBackups
{
    // Dev-only end-to-end check driving the real menus, compiled with -p:DefineConstants=SELFTEST. Never in the release build.
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
            yield return Wait(0.5f);
            yield return new WaitForEndOfFrame();
            string path = Path.Combine(Path.GetTempPath(), "wb_" + name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            yield return Wait(1f);
            LoggerInstance.Msg($"[SELFTEST] screenshot {path}");
        }

        private static T Find<T>() where T : Behaviour =>
            UnityEngine.Object.FindObjectsByType<T>(FindObjectsSortMode.None).FirstOrDefault(c => c.isActiveAndEnabled);

        private static Transform Modal(string kind) =>
            UnityEngine.Object.FindObjectsByType<RectTransform>(FindObjectsSortMode.None)
                .FirstOrDefault(t => t.name == "WorldBackups." + kind + "(Clone)" && t.gameObject.activeInHierarchy);

        private static void Click(Transform t) => t.GetComponent<Button>().onClick.Invoke();

        private static BackupRow Row(Func<BackupEntry, bool> match) =>
            UnityEngine.Object.FindObjectsByType<BackupRow>(FindObjectsSortMode.None).FirstOrDefault(r => r.Backup != null && match(r.Backup));

        private IEnumerator Open(Component row)
        {
            row.GetComponent<MenuButtonElement>().OpenMenu();
            yield return Wait(0.5f);
        }

        private Transform DetailsButton(string path) => Find<BackupDetailsView>()?.transform.Find("Wrapper/Buttons/" + path);

        private IEnumerator SubmitPrompt(string text)
        {
            Transform prompt = Modal("Prompt");
            Check(prompt != null, "prompt modal shown");
            if (prompt == null) yield break;
            prompt.GetComponentInChildren<TMP_InputField>().text = text;
            yield return null;
            Click(prompt.Find("CreateNewWorldModal/DoneBtn"));
            yield return Wait(0.5f);
        }

        private IEnumerator ConfirmModal()
        {
            Transform confirm = Modal("Confirm");
            Check(confirm != null, "confirm modal shown");
            if (confirm != null)
                Click(confirm.Find("CreateNewWorldModal/DoneBtn"));
            yield return Wait(0.5f);
        }

        private void CloseMessage()
        {
            Transform message = Modal("Confirm");
            if (message != null)
                Click(message.Find("CreateNewWorldModal/DoneBtn"));
        }

        private IEnumerator SelfTest()
        {
            while (SceneManager.GetActiveScene().buildIndex != 0)
                yield return null;
            yield return Wait(8f);

            string worldDir = Store.WorldDir(TestWorld);
            Directory.CreateDirectory(worldDir);
            File.Copy(Path.Combine(Application.streamingAssetsPath, "NewWorldSave.json"), Path.Combine(worldDir, "world.json"), true);
            File.Copy(Path.Combine(Application.streamingAssetsPath, "NewWorldSave.jpg"), Path.Combine(worldDir, "world.jpg"), true);
            File.Copy(Path.Combine(worldDir, "world.json"), Path.Combine(worldDir, "backup_2026-01-01_00-00-00.json"), true);
            RefreshGameWorldList();

            Check(NativeUi.Ready, "templates captured from the main menu");
            var menuEntry = UnityEngine.Object.FindObjectsByType<MenuButtonElement>(FindObjectsSortMode.None).FirstOrDefault(b => b.name == "WorldBackupsMenuButton");
            Check(menuEntry != null, "Backups entry in main menu");
            menuEntry?.OpenMenu();
            yield return Wait(1f);
            var worlds = Find<WorldsListView>();
            Check(worlds != null, "worlds column opened");
            var worldRow = UnityEngine.Object.FindObjectsByType<WorldRef>(FindObjectsSortMode.None).FirstOrDefault(w => w.World == TestWorld && w.GetComponent<BackupRow>() != null);
            Check(worldRow != null, "test world row listed");
            yield return Shot("menu_worlds");
            if (worldRow == null) { Finish(); yield break; }

            yield return Open(worldRow);
            var list = Find<BackupsListView>();
            Check(list != null, "backups column opened");
            Check(Row(b => b.Kind == BackupKind.Game) != null, "game autosave listed");

            // Create with a note through the prompt.
            Click(list.GetComponent<ScrollRect>().content.Find("CreateButtonWrapper/CreateWorldButton"));
            yield return Wait(0.5f);
            yield return Shot("menu_prompt");
            yield return SubmitPrompt("selftest");
            var manual = Store.ListBackups(TestWorld).FirstOrDefault(b => b.Kind == BackupKind.Manual);
            Check(manual != null && manual.Label == "selftest", "manual backup created with note");
            Check(Find<BackupDetailsView>() != null, "new backup's details opened");
            yield return Shot("menu_details");

            // Restore wipes later changes, after a confirmation, with a safety copy.
            File.WriteAllText(Path.Combine(worldDir, "marker.txt"), "after backup");
            Click(DetailsButton("GameObject/Rename"));
            yield return Wait(0.5f);
            yield return Shot("menu_confirm");
            yield return ConfirmModal();
            Check(!File.Exists(Path.Combine(worldDir, "marker.txt")), "restore replaced world content");
            Check(Store.ListBackups(TestWorld).Any(b => b.Kind == BackupKind.Safety), "safety backup made");
            yield return Shot("menu_message");
            CloseMessage();
            yield return Wait(0.5f);

            // Note edit.
            var row = Row(b => b.Kind == BackupKind.Manual);
            yield return Open(row);
            Click(DetailsButton("GameObject2/Note"));
            yield return Wait(0.5f);
            yield return SubmitPrompt("renamed");
            Check(Store.ListBackups(TestWorld).Any(b => b.Label == "renamed"), "note edited");
            Check(Find<BackupDetailsView>() != null, "details reopened after note edit");

            // Restore as a new world (name validation greys the button out).
            Click(DetailsButton("GameObject2/NewWorld"));
            yield return Wait(0.5f);
            Transform prompt = Modal("Prompt");
            prompt.GetComponentInChildren<TMP_InputField>().text = TestWorld;
            yield return Wait(0.2f);
            Check(!prompt.Find("CreateNewWorldModal/DoneBtn").GetComponent<Button>().interactable, "existing name refused");
            yield return SubmitPrompt(TestCopy);
            Check(Store.WorldExists(TestCopy), "restored as new world");
            CloseMessage();
            Directory.Delete(Store.WorldDir(TestCopy), true);
            RefreshGameWorldList();
            yield return Wait(0.5f);

            // Delete the game autosave.
            worldRow = UnityEngine.Object.FindObjectsByType<WorldRef>(FindObjectsSortMode.None).FirstOrDefault(w => w.World == TestWorld && w.GetComponent<BackupRow>() != null);
            yield return Open(worldRow);
            yield return Open(Row(b => b.Kind == BackupKind.Game));
            Click(DetailsButton("GameObject/Delete"));
            yield return ConfirmModal();
            Check(!File.Exists(Path.Combine(worldDir, "backup_2026-01-01_00-00-00.json")), "game autosave deleted");
            Check(Row(b => b.Kind == BackupKind.Game) == null, "list refreshed after delete");

            // Load from the main menu.
            yield return Open(Row(b => b.Kind == BackupKind.Manual));
            Click(DetailsButton("ActionButton"));
            yield return ConfirmModal();
            float deadline = Time.realtimeSinceStartup + 60f;
            while (SceneManager.GetActiveScene().name != MainGameScene && Time.realtimeSinceStartup < deadline)
                yield return null;
            Check(SceneManager.GetActiveScene().name == MainGameScene && CurrentWorldName() == TestWorld, "load launched the world");
            yield return Wait(25f);

            // Escape menu.
            var toggler = UnityEngine.Object.FindFirstObjectByType<Players.Services.PlayerPauseToggler>();
            toggler?.TogglePause();
            yield return Wait(1.5f);
            var pauseEntry = UnityEngine.Object.FindObjectsByType<MenuButtonElement>(FindObjectsSortMode.None).FirstOrDefault(b => b.name == PauseButtonName);
            Check(pauseEntry != null && pauseEntry.isActiveAndEnabled, "Backups entry in pause menu");
            pauseEntry?.OpenMenu();
            yield return Wait(1f);
            list = Find<BackupsListView>();
            Check(list != null, "pause backups column opened");
            yield return Shot("pause_list");

            int before = Store.ListBackups(TestWorld).Count(b => b.Kind == BackupKind.Manual);
            Click(list.GetComponent<ScrollRect>().content.Find("CreateButtonWrapper/CreateWorldButton"));
            yield return Wait(0.5f);
            yield return SubmitPrompt("in game");
            deadline = Time.realtimeSinceStartup + 30f;
            while (Store.ListBackups(TestWorld).Count(b => b.Kind == BackupKind.Manual) == before && Time.realtimeSinceStartup < deadline)
                yield return null;
            yield return Wait(1f);
            Check(Store.ListBackups(TestWorld).Count(b => b.Kind == BackupKind.Manual) == before + 1, "in-game save + backup");
            Check(Find<BackupDetailsView>() != null, "in-game details opened");
            yield return Shot("pause_details");

            // Load from inside the game: save, back to menu, restore, relaunch.
            int safetyBefore = Store.ListBackups(TestWorld).Count(b => b.Kind == BackupKind.Safety);
            Click(DetailsButton("ActionButton"));
            yield return Wait(0.5f);
            yield return Shot("pause_confirm");
            yield return ConfirmModal();
            deadline = Time.realtimeSinceStartup + 60f;
            bool sawMenu = false;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (SceneManager.GetActiveScene().buildIndex == 0)
                    sawMenu = true;
                if (sawMenu && SceneManager.GetActiveScene().name == MainGameScene)
                    break;
                yield return null;
            }
            Check(sawMenu && SceneManager.GetActiveScene().name == MainGameScene && CurrentWorldName() == TestWorld, "in-game load went through menu and relaunched");
            Check(Store.ListBackups(TestWorld).Count(b => b.Kind == BackupKind.Safety) == Math.Min(safetyBefore + 1, 5), "safety backup made before in-game load");
            yield return Wait(10f);

            Finish();
        }

        private void Finish()
        {
            LoggerInstance.Msg(_failures == 0 ? "[SELFTEST] ALL PASSED" : $"[SELFTEST] {_failures} FAILURE(S)");
            Application.Quit();
        }
    }
}
#endif
