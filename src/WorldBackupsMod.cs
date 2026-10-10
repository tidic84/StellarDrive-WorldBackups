using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Core.Services;
using Managers.Interface;
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
        private const string MainGameScene = "MainGame";
        private const string StartServerPrefKey = "StartServer"; // same key as the game's own "start server" toggle

        private MelonPreferences_Entry<string> _toggleKey;
        private MelonPreferences_Entry<bool> _autoBackupOnLoad;
        private MelonPreferences_Entry<int> _maxAutoBackups;
        private MelonPreferences_Entry<bool> _safetyBackup;
        private MelonPreferences_Entry<int> _maxSafetyBackups;
        private MelonPreferences_Entry<bool> _showMenuButton;
        private MelonPreferences_Entry<string> _language;
        private MelonPreferences_Entry<float> _uiScale;

        private BackupStore _store;
        private KeyCode _key = KeyCode.F8;

        // UI state
        private bool _open;
        private Rect _window = new Rect(60, 60, 900, 580);
        private Vector2 _worldScroll;
        private Vector2 _backupScroll;
        private List<string> _worlds = new List<string>();
        private string _selectedWorld;
        private List<BackupEntry> _backups = new List<BackupEntry>();
        private BackupEntry _selectedBackup;
        private string _newLabel = "";
        private string _status = "";
        private bool _statusIsError;
        private bool _busySaving;

        private enum Pending { None, Restore, Load, Delete, NewWorld, EditLabel }
        private Pending _pending;
        private BackupEntry _pendingTarget;
        private string _pendingText = "";

        private readonly Dictionary<string, Texture2D> _thumbnails = new Dictionary<string, Texture2D>();

        private CursorLockMode _prevLock;
        private bool _prevVisible;

        private GUIStyle _windowStyle, _rowStyle, _rowSelectedStyle,_mutedStyle, _headerStyle, _statusStyle, _errorStyle, _wrapStyle;
        private Texture2D _bgTex, _rowTex, _rowSelTex;

        private static bool InGame => SceneManager.GetActiveScene().name == MainGameScene;

        private static bool IsClient =>
            MainGameSceneLoadData.Mode == MainGameSceneLoadData.GameMode.JoinGameSteam ||
            MainGameSceneLoadData.Mode == MainGameSceneLoadData.GameMode.JoinGameIp;

        public override void OnInitializeMelon()
        {
            var cat = MelonPreferences.CreateCategory("WorldBackups", "World Backups");
            _toggleKey = cat.CreateEntry("ToggleKey", "F8", "Toggle key", "Key that opens/closes the backup window (UnityEngine.KeyCode name).");
            _autoBackupOnLoad = cat.CreateEntry("AutoBackupOnWorldLoad", true, "Auto backup on world load", "Make a backup every time you load one of your worlds.");
            _maxAutoBackups = cat.CreateEntry("MaxAutoBackupsPerWorld", 10, "Max auto backups per world", "Oldest automatic backups beyond this count are deleted (0 = keep all).");
            _safetyBackup = cat.CreateEntry("SafetyBackupBeforeRestore", true, "Safety backup before restore", "Back up the current world before it gets overwritten by a restore.");
            _maxSafetyBackups = cat.CreateEntry("MaxSafetyBackupsPerWorld", 5, "Max safety backups per world", "Oldest safety backups beyond this count are deleted (0 = keep all).");
            _showMenuButton = cat.CreateEntry("ShowMainMenuButton", true, "Main menu button", "Show a small button in the main menu to open the window.");
            _language = cat.CreateEntry("Language", "auto", "Language", "auto, en or fr.");
            _uiScale = cat.CreateEntry("UiScale", 0f, "UI scale", "Window scale factor (0 = automatic from screen height).");

            if (!Enum.TryParse(_toggleKey.Value, true, out _key))
            {
                LoggerInstance.Warning($"Unknown ToggleKey '{_toggleKey.Value}', using F8.");
                _key = KeyCode.F8;
            }

            Loc.Init(_language.Value);
            _store = new BackupStore(Application.persistentDataPath);
            LoggerInstance.Msg($"Backups folder: {_store.BackupsRoot} (press {_key} to open)");
#if SELFTEST
            MelonCoroutines.Start(SelfTest());
#endif
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            if (sceneName == MainGameScene)
            {
                if (_autoBackupOnLoad.Value && !IsClient)
                    AutoBackupCurrentWorld();
            }
            else if (_open)
            {
                Reload();
            }
        }

        public override void OnUpdate()
        {
            if (_open)
            {
                // The game re-locks the cursor while flying/walking; keep it free while our window is up.
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        public override void OnGUI()
        {
            Event e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == _key && !IsTyping())
            {
                SetOpen(!_open);
                e.Use();
            }

            if (_busySaving)
                return; // stay out of the game's save screenshot

            EnsureStyles();

            // IMGUI is pixel-based: scale it so the window stays readable at 1440p/4K.
            float scale = _uiScale.Value > 0f ? _uiScale.Value : Mathf.Max(1f, Screen.height / 1080f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float screenW = Screen.width / scale;
            float screenH = Screen.height / scale;

            if (!_open)
            {
                if (_showMenuButton.Value && !InGame && SceneManager.GetActiveScene().buildIndex == 0)
                {
                    if (GUI.Button(new Rect(screenW - 236, 16, 220, 34), Loc.T("OpenButton", _key)))
                        SetOpen(true);
                }
                return;
            }

            _window.width = Mathf.Min(900, screenW - 20);
            _window.height = Mathf.Min(580, screenH - 20);
            _window.x = Mathf.Clamp(_window.x, 0, screenW - _window.width);
            _window.y = Mathf.Clamp(_window.y, 0, screenH - _window.height);
            _window = GUI.Window(0x57B4C, _window, DrawWindow, Loc.T("Title"), _windowStyle);
        }

        private static bool IsTyping() => GUIUtility.keyboardControl != 0;

        private void SetOpen(bool open)
        {
            if (open == _open)
                return;

            _open = open;
            if (open)
            {
                _prevLock = Cursor.lockState;
                _prevVisible = Cursor.visible;
                _status = "";
                Reload();
            }
            else
            {
                _pending = Pending.None;
                GUIUtility.keyboardControl = 0;
                if (InGame)
                {
                    Cursor.lockState = _prevLock;
                    Cursor.visible = _prevVisible;
                }
                ClearThumbnails();
            }
        }

        // ---------------------------------------------------------------- data

        private string CurrentWorldName()
        {
            string file = MainGameSceneLoadData.SaveFileFullName;
            if (string.IsNullOrEmpty(file))
                return null;
            try
            {
                string dir = Path.GetDirectoryName(Path.GetFullPath(file));
                string parent = Path.GetDirectoryName(dir);
                if (string.Equals(Path.GetFullPath(parent), Path.GetFullPath(_store.SavesRoot), StringComparison.OrdinalIgnoreCase))
                    return Path.GetFileName(dir);
            }
            catch
            {
            }
            return null;
        }

        private void Reload()
        {
            try
            {
                _worlds = _store.ListWorlds();
                if (InGame && CurrentWorldName() is string current && _worlds.Contains(current))
                    _selectedWorld = current;
                if (_selectedWorld == null || !_worlds.Contains(_selectedWorld))
                    _selectedWorld = _worlds.FirstOrDefault();
                ReloadBackups();
            }
            catch (Exception ex)
            {
                SetError(ex);
            }
        }

        private void ReloadBackups()
        {
            string selectedPath = _selectedBackup?.Path;
            _backups = _selectedWorld == null ? new List<BackupEntry>() : _store.ListBackups(_selectedWorld);
            _selectedBackup = _backups.FirstOrDefault(b => b.Path == selectedPath) ?? _backups.FirstOrDefault();
        }

        private void AutoBackupCurrentWorld()
        {
            string world = CurrentWorldName();
            if (world == null || !_store.WorldExists(world))
                return;
            try
            {
                _store.CreateBackup(world, "", BackupKind.Auto);
                _store.Prune(world, BackupKind.Auto, _maxAutoBackups.Value);
                LoggerInstance.Msg($"Automatic backup of '{world}' created.");
            }
            catch (Exception ex)
            {
                LoggerInstance.Error($"Automatic backup of '{world}' failed: {ex}");
            }
        }

        private void DoCreateBackup()
        {
            string world = _selectedWorld;
            string label = _newLabel.Trim();

            if (InGame && world == CurrentWorldName() && !IsClient)
            {
                // Flush the running game to disk first so the backup holds the latest state.
                var saver = ServiceLocator.GetService<GameSaver>();
                if (saver != null)
                {
                    MelonCoroutines.Start(SaveThenBackup(saver, world, label));
                    return;
                }
            }

            FinishBackup(world, label);
        }

        private IEnumerator SaveThenBackup(GameSaver saver, string world, string label)
        {
            SetStatus(Loc.T("Saving"));
            _busySaving = true;
            yield return null;

            try
            {
                if (!saver.IsCurrentlySaving())
                    saver.SaveGame(new SaveGameParams { IsAutomatic = false });
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"Game save before backup failed: {ex.Message}");
            }

            float deadline = Time.realtimeSinceStartup + 15f;
            while (saver != null && saver.IsCurrentlySaving() && Time.realtimeSinceStartup < deadline)
                yield return null;

            _busySaving = false;
            FinishBackup(world, label);
        }

        private void FinishBackup(string world, string label)
        {
            try
            {
                BackupEntry entry = _store.CreateBackup(world, label, BackupKind.Manual);
                _newLabel = "";
                ReloadBackups();
                _selectedBackup = _backups.FirstOrDefault(b => b.Path == entry.Path) ?? _selectedBackup;
                SetStatus(Loc.T("Created"));
                LoggerInstance.Msg($"Backup of '{world}' created: {entry.Path}");
            }
            catch (Exception ex)
            {
                SetError(ex);
            }
        }

        private bool RestoreInto(BackupEntry backup, string world)
        {
            try
            {
                if (_safetyBackup.Value && _store.WorldExists(world))
                {
                    _store.CreateBackup(world, "", BackupKind.Safety);
                    _store.Prune(world, BackupKind.Safety, _maxSafetyBackups.Value);
                }

                _store.Restore(backup, world);
                LoggerInstance.Msg($"Restored '{backup.Path}' into world '{world}'.");
                RefreshGameWorldList();
                return true;
            }
            catch (Exception ex)
            {
                SetError(ex);
                return false;
            }
        }

        private void DoRestore(BackupEntry backup)
        {
            if (RestoreInto(backup, backup.World))
            {
                Reload();
                SetStatus(Loc.T("Restored", backup.World));
            }
        }

        private void DoLoad(BackupEntry backup)
        {
            if (!RestoreInto(backup, backup.World))
                return;
            LaunchWorld(backup.World);
        }

        private void DoNewWorld(BackupEntry backup, string name)
        {
            string error = BackupStore.ValidateNewWorldName(name, _store);
            if (error != null)
            {
                SetStatus(Loc.T(error == "empty" ? "NameEmpty" : error == "exists" ? "NameExists" : "NameInvalid"), true);
                return;
            }

            try
            {
                _store.Restore(backup, name);
                RefreshGameWorldList();
                _selectedWorld = name;
                _pending = Pending.None;
                Reload();
                SetStatus(Loc.T("CreatedWorld", name));
            }
            catch (Exception ex)
            {
                SetError(ex);
            }
        }

        private void DoDelete(BackupEntry backup)
        {
            try
            {
                _store.Delete(backup);
                if (_thumbnails.TryGetValue(backup.ScreenshotPath ?? "", out Texture2D tex))
                {
                    UnityEngine.Object.Destroy(tex);
                    _thumbnails.Remove(backup.ScreenshotPath);
                }
                ReloadBackups();
                SetStatus(Loc.T("Deleted"));
            }
            catch (Exception ex)
            {
                SetError(ex);
            }
        }

        private void LaunchWorld(string world)
        {
            bool host = PlayerPrefs.GetInt(StartServerPrefKey, 0) > 0;
            MainGameSceneLoadData.Mode = host ? MainGameSceneLoadData.GameMode.HostGameSteam : MainGameSceneLoadData.GameMode.SinglePlayer;
            MainGameSceneLoadData.SaveFileFullName = new FileInfo(_store.WorldSaveFile(world)).FullName;
            LoggerInstance.Msg($"Launching world '{world}' ({MainGameSceneLoadData.Mode}).");
            SetOpen(false);
            SceneManager.LoadScene(MainGameScene, LoadSceneMode.Single);
        }

        /// <summary>Makes the main menu's world list pick up restored/new worlds without restarting.</summary>
        private void RefreshGameWorldList()
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

        private void SetStatus(string text, bool error = false)
        {
            _status = text;
            _statusIsError = error;
        }

        private void SetError(Exception ex)
        {
            LoggerInstance.Error(ex.ToString());
            SetStatus(Loc.T("Error", ex.Message), true);
        }

        // ---------------------------------------------------------------- drawing

        private void DrawWindow(int id)
        {
            GUILayout.BeginHorizontal();
            DrawWorldList();
            GUILayout.Space(8);
            DrawWorldPanel();
            GUILayout.EndHorizontal();

            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (!string.IsNullOrEmpty(_status))
                GUILayout.Label(_status, _statusIsError ? _errorStyle : _statusStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(Loc.T("OpenFolder"), GUILayout.Width(140)))
                OpenFolder(_selectedWorld != null ? Path.Combine(_store.BackupsRoot, _selectedWorld) : _store.BackupsRoot);
            if (GUILayout.Button(Loc.T("Refresh"), GUILayout.Width(100)))
                Reload();
            if (GUILayout.Button(Loc.T("Close") + $" ({_key})", GUILayout.Width(110)))
                SetOpen(false);
            GUILayout.EndHorizontal();

            GUI.DragWindow(new Rect(0, 0, 10000, 22));
        }

        private void DrawWorldList()
        {
            GUILayout.BeginVertical(GUILayout.Width(210));
            GUILayout.Label(Loc.T("Worlds"), _headerStyle);
            _worldScroll = GUILayout.BeginScrollView(_worldScroll);
            if (_worlds.Count == 0)
                GUILayout.Label(Loc.T("NoWorlds"), _wrapStyle);

            string current = InGame ? CurrentWorldName() : null;
            foreach (string world in _worlds)
            {
                string text = world == current ? $"{world}  ({Loc.T("Current")})" : world;
                if (GUILayout.Button(text, world == _selectedWorld ? _rowSelectedStyle : _rowStyle, GUILayout.Height(28)))
                {
                    _selectedWorld = world;
                    _pending = Pending.None;
                    _selectedBackup = null;
                    ReloadBackups();
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void DrawWorldPanel()
        {
            GUILayout.BeginVertical();
            if (_selectedWorld == null)
            {
                GUILayout.Label(Loc.T("SelectWorld"), _wrapStyle);
                GUILayout.EndVertical();
                return;
            }

            bool inGame = InGame;
            bool canCreate = !(inGame && IsClient);

            GUILayout.Label(Loc.T("Backups", _selectedWorld), _headerStyle);

            GUILayout.BeginHorizontal();
            GUI.enabled = canCreate;
            GUILayout.Label(Loc.T("LabelHint"), _mutedStyle, GUILayout.Width(120));
            _newLabel = GUILayout.TextField(_newLabel, 60, GUILayout.MinWidth(160));
            bool flushFirst = inGame && _selectedWorld == CurrentWorldName();
            if (GUILayout.Button(Loc.T(flushFirst ? "SaveAndBackup" : "CreateBackup"), GUILayout.Width(150)))
                DoCreateBackup();
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            if (inGame)
                GUILayout.Label(Loc.T(IsClient ? "ClientNotice" : "InGameNotice"), _mutedStyle);
            else
            {
                bool host = PlayerPrefs.GetInt(StartServerPrefKey, 0) > 0;
                bool newHost = GUILayout.Toggle(host, " " + Loc.T("HostToggle"));
                if (newHost != host)
                    PlayerPrefs.SetInt(StartServerPrefKey, newHost ? 1 : 0);
            }

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();

            // backup list
            GUILayout.BeginVertical(GUILayout.MinWidth(340));
            _backupScroll = GUILayout.BeginScrollView(_backupScroll);
            if (_backups.Count == 0)
                GUILayout.Label(Loc.T("NoBackups"), _mutedStyle);
            foreach (BackupEntry backup in _backups)
                DrawBackupRow(backup);
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            // details of the selected backup
            GUILayout.BeginVertical(GUILayout.Width(250));
            if (_selectedBackup != null)
                DrawBackupDetails(_selectedBackup, inGame);
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private void DrawBackupRow(BackupEntry backup)
        {
            string title = backup.Created.ToString("yyyy-MM-dd  HH:mm:ss");
            string sub = KindName(backup.Kind) + "  ·  " + FormatSize(backup.SizeBytes);
            if (!string.IsNullOrEmpty(backup.Label))
                sub += "  ·  " + backup.Label;

            if (GUILayout.Button(title + "\n" + sub, backup == _selectedBackup ? _rowSelectedStyle : _rowStyle, GUILayout.Height(40)))
            {
                if (_selectedBackup != backup)
                    _pending = Pending.None;
                _selectedBackup = backup;
            }
        }

        private void DrawBackupDetails(BackupEntry backup, bool inGame)
        {
            Texture2D thumb = GetThumbnail(backup.ScreenshotPath);
            if (thumb != null)
                GUILayout.Box(thumb, GUIStyle.none, GUILayout.Width(250), GUILayout.Height(140));

            GUILayout.Label(backup.Created.ToString("F", Loc.Culture), _wrapStyle);
            GUILayout.Label(KindName(backup.Kind) + (string.IsNullOrEmpty(backup.Label) ? "" : " — " + backup.Label), _mutedStyle);
            GUILayout.Space(6);

            if (_pending != Pending.None && _pendingTarget == backup)
            {
                DrawPending(backup);
                return;
            }

            GUI.enabled = !inGame;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Loc.T("Load"), GUILayout.Height(30)))
                Ask(Pending.Load, backup);
            if (GUILayout.Button(Loc.T("Restore"), GUILayout.Height(30)))
                Ask(Pending.Restore, backup);
            GUILayout.EndHorizontal();
            if (GUILayout.Button(Loc.T("AsNewWorld")))
                Ask(Pending.NewWorld, backup, $"{backup.World} {backup.Created:yyyy-MM-dd HH-mm}");
            GUI.enabled = true;

            GUILayout.BeginHorizontal();
            GUI.enabled = !backup.IsGameBackup;
            if (GUILayout.Button(Loc.T("Rename")))
                Ask(Pending.EditLabel, backup, backup.Label);
            GUI.enabled = true;
            if (GUILayout.Button(Loc.T("Delete")))
                Ask(Pending.Delete, backup);
            GUILayout.EndHorizontal();
        }

        private void Ask(Pending what, BackupEntry target, string text = "")
        {
            _pending = what;
            _pendingTarget = target;
            _pendingText = text;
            _status = "";
        }

        private void DrawPending(BackupEntry backup)
        {
            switch (_pending)
            {
                case Pending.Restore:
                    GUILayout.Label(Loc.T("ConfirmRestore", backup.World), _wrapStyle);
                    break;
                case Pending.Load:
                    GUILayout.Label(Loc.T("ConfirmLoad", backup.World), _wrapStyle);
                    break;
                case Pending.Delete:
                    GUILayout.Label(Loc.T("ConfirmDelete"), _wrapStyle);
                    break;
                case Pending.NewWorld:
                    GUILayout.Label(Loc.T("NewWorldName"), _wrapStyle);
                    _pendingText = GUILayout.TextField(_pendingText, 60);
                    break;
                case Pending.EditLabel:
                    GUILayout.Label(Loc.T("LabelHint"), _wrapStyle);
                    _pendingText = GUILayout.TextField(_pendingText, 60);
                    break;
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Loc.T(_pending == Pending.NewWorld || _pending == Pending.EditLabel ? "OK" : "Confirm"), GUILayout.Height(28)))
            {
                Pending action = _pending;
                if (action != Pending.NewWorld)
                    _pending = Pending.None;
                GUIUtility.keyboardControl = 0;
                switch (action)
                {
                    case Pending.Restore: DoRestore(backup); break;
                    case Pending.Load: DoLoad(backup); break;
                    case Pending.Delete: DoDelete(backup); break;
                    case Pending.NewWorld: DoNewWorld(backup, _pendingText.Trim()); break;
                    case Pending.EditLabel:
                        try { _store.SetLabel(backup, _pendingText.Trim()); }
                        catch (Exception ex) { SetError(ex); }
                        break;
                }
            }
            if (GUILayout.Button(Loc.T("Cancel"), GUILayout.Height(28)))
            {
                _pending = Pending.None;
                GUIUtility.keyboardControl = 0;
            }
            GUILayout.EndHorizontal();
        }

        private static string KindName(BackupKind kind) => Loc.T("Kind" + kind);

        private static string FormatSize(long bytes)
        {
            if (bytes >= 1024 * 1024)
                return (bytes / (1024f * 1024f)).ToString("0.0") + " MB";
            return Math.Max(1, bytes / 1024) + " KB";
        }

        private Texture2D GetThumbnail(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;
            if (_thumbnails.TryGetValue(path, out Texture2D tex))
                return tex;

            tex = null;
            try
            {
                if (File.Exists(path))
                {
                    tex = new Texture2D(2, 2);
                    if (!tex.LoadImage(File.ReadAllBytes(path)))
                    {
                        UnityEngine.Object.Destroy(tex);
                        tex = null;
                    }
                }
            }
            catch
            {
                tex = null;
            }
            _thumbnails[path] = tex;
            return tex;
        }

        private void ClearThumbnails()
        {
            foreach (Texture2D tex in _thumbnails.Values)
                if (tex != null)
                    UnityEngine.Object.Destroy(tex);
            _thumbnails.Clear();
        }

        private void OpenFolder(string path)
        {
            try
            {
                if (!Directory.Exists(path))
                    path = Directory.Exists(_store.BackupsRoot) ? _store.BackupsRoot : Application.persistentDataPath;
                Application.OpenURL("file:///" + path.Replace('\\', '/'));
            }
            catch (Exception ex)
            {
                SetError(ex);
            }
        }

        private void EnsureStyles()
        {
            if (_windowStyle != null && _bgTex != null)
                return;

            _bgTex = SolidTexture(new Color(0.08f, 0.09f, 0.13f, 1f));
            _rowTex = SolidTexture(new Color(0.15f, 0.17f, 0.23f, 1f));
            _rowSelTex = SolidTexture(new Color(0.27f, 0.36f, 0.62f, 1f));

            _windowStyle = new GUIStyle(GUI.skin.window) { padding = new RectOffset(12, 12, 26, 10) };
            _windowStyle.normal.background = _bgTex;
            _windowStyle.onNormal.background = _bgTex;
            _windowStyle.normal.textColor = _windowStyle.onNormal.textColor = new Color(0.85f, 0.9f, 1f);
            _windowStyle.fontStyle = FontStyle.Bold;

            _rowStyle = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft, padding = new RectOffset(8, 8, 4, 4), wordWrap = false };
            _rowStyle.normal.background = _rowTex;
            _rowStyle.hover.background = _rowSelTex;
            _rowStyle.normal.textColor = _rowStyle.hover.textColor = Color.white;

            _rowSelectedStyle = new GUIStyle(_rowStyle);
            _rowSelectedStyle.normal.background = _rowSelTex;

            _headerStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 15 };
            _headerStyle.normal.textColor = new Color(0.75f, 0.85f, 1f);

            _mutedStyle = new GUIStyle(GUI.skin.label) { wordWrap = true };
            _mutedStyle.normal.textColor = new Color(0.65f, 0.68f, 0.75f);

            _wrapStyle = new GUIStyle(GUI.skin.label) { wordWrap = true };
            _wrapStyle.normal.textColor = Color.white;

            _statusStyle = new GUIStyle(GUI.skin.label);
            _statusStyle.normal.textColor = new Color(0.55f, 0.9f, 0.6f);

            _errorStyle = new GUIStyle(GUI.skin.label) { wordWrap = true };
            _errorStyle.normal.textColor = new Color(1f, 0.45f, 0.4f);
        }

        private static Texture2D SolidTexture(Color color)
        {
            var tex = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            tex.SetPixel(0, 0, color);
            tex.Apply();
            return tex;
        }
    }
}
