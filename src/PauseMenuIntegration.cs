using System;
using System.Reflection;
using HarmonyLib;
using UI.Common;
using UI.Pause;
using UnityEngine;
using UnityEngine.UI;

namespace WorldBackups
{
    // Adds a "Backups" entry to the in-game Escape menu, right under the game's own "Save" button.
    public partial class WorldBackupsMod
    {
        internal const string PauseButtonName = "WorldBackupsButton";

        private static readonly FieldInfo PauseCanvasField = AccessTools.Field(typeof(PauseMenuHandler), "_pauseCanvas");
        private static readonly FieldInfo SaveButtonField = AccessTools.Field(typeof(PauseButtonsActivator), "saveButton");

        private void InstallPauseMenuHooks()
        {
            try
            {
                HarmonyInstance.Patch(AccessTools.Method(typeof(PauseMenuHandler), nameof(PauseMenuHandler.Show)),
                    postfix: new HarmonyMethod(typeof(WorldBackupsMod), nameof(PauseShowPostfix)));
            }
            catch (Exception ex)
            {
                LoggerInstance.Error($"Could not hook the pause menu: {ex}");
            }
        }

        private static void PauseShowPostfix(PauseMenuHandler __instance)
        {
            try
            {
                Instance?.AddPauseEntry(PauseCanvasField?.GetValue(__instance) as GameObject);
            }
            catch (Exception ex)
            {
                Instance?.LoggerInstance.Error($"Adding the pause menu entry failed: {ex}");
            }
        }

        private void AddPauseEntry(GameObject pauseCanvas)
        {
            if (pauseCanvas == null)
                return;
            if (!NativeUi.Ready)
            {
                LoggerInstance.Warning("Backup menus are built from the main menu; start the game from the main menu to get them.");
                return;
            }

            var nav = pauseCanvas.GetComponent<MenuNavigation>() ?? pauseCanvas.GetComponentInChildren<MenuNavigation>(true);
            var activator = pauseCanvas.GetComponentInChildren<PauseButtonsActivator>(true);
            var template = activator != null ? SaveButtonField?.GetValue(activator) as GameObject : null;
            if (nav == null || template == null)
            {
                LoggerInstance.Warning("Pause menu layout not recognised; Backups entry not added.");
                return;
            }

            // Your own world: straight to its backups. As a client: pick one of your worlds first.
            string world = IsClient ? null : CurrentWorldName();
            GameObject submenu = world != null ? NativeUi.BackupsSelector : NativeUi.WorldsSelector;

            MenuButtonElement entry = NativeUi.AddMenuButton(nav, template, PauseButtonName, submenu);
            entry.gameObject.AddComponent<WorldRef>().World = world;
            entry.GetComponent<Button>().onClick.AddListener(entry.OpenMenu);
        }
    }
}
