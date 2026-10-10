using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using TMPro;
using UI.Common;
using UI.MainMenu;
using UI.MainMenu.Common;
using UI.Options;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace WorldBackups
{
    /// <summary>
    /// Builds the backup screens out of the game's own menu prefabs (world list rows, world details panel,
    /// delete/rename modals), captured from the main menu, so they look exactly like the rest of StellarDrive.
    /// </summary>
    internal static class NativeUi
    {
        private const string MainMenuButtonName = "WorldBackupsMenuButton";

        private static readonly Color BlueBackground = new Color32(0x32, 0x87, 0xBE, 0xFF);
        private static readonly Color BlueBorder = new Color32(0x23, 0x5E, 0x84, 0xFF);
        private static readonly Color RedButton = new Color32(0x9A, 0x4D, 0x4C, 0xFF);

        // Game scripts that would drive the cloned prefabs with world data; our views replace them.
        private static readonly HashSet<string> GameScripts = new HashSet<string>
        {
            "WorldDirectoriesList", "WorldDirectorySelectButton", "StartGameDetails", "CreateNewWorldButton",
            "SaveScreenshotPreview", "DeleteWorldModal", "RenameWorldModal", "SaveButton", "GameFilesList", "SaveSelectButton"
        };

        private static GameObject _root;

        internal static GameObject WorldsSelector { get; private set; }
        internal static GameObject BackupsSelector { get; private set; }
        internal static GameObject RowTemplate { get; private set; }
        internal static GameObject DetailsTemplate { get; private set; }
        private static GameObject _confirmTemplate;
        private static GameObject _promptTemplate;

        internal static bool Ready => _root != null && RowTemplate != null && DetailsTemplate != null && _confirmTemplate != null && _promptTemplate != null;

        // ------------------------------------------------------------------ templates

        internal static void CaptureTemplates(MelonLogger.Instance log)
        {
            if (Ready)
                return;

            var nav = UnityEngine.Object.FindFirstObjectByType<MenuNavigation>();
            var startButton = nav?.GetComponentsInChildren<MenuButtonElement>(true)
                .FirstOrDefault(b => b.submenu != null && b.submenu.GetComponentInChildren<WorldDirectoriesList>(true) != null);
            var worldList = startButton?.submenu.GetComponentInChildren<WorldDirectoriesList>(true);
            var rowPrefab = worldList != null ? Field<WorldDirectorySelectButton>(worldList, "worldDirectoryButtonPrefab") : null;
            var detailsPrefab = rowPrefab?.GetComponent<MenuButtonElement>()?.submenu;
            var deleteModal = Field<GameObject>(UnityEngine.Object.FindFirstObjectByType<DeleteWorldHandler>(FindObjectsInactive.Include), "modal");
            var renameModal = Field<GameObject>(UnityEngine.Object.FindFirstObjectByType<RenameWorldHandler>(FindObjectsInactive.Include), "modal");

            if (startButton == null || rowPrefab == null || detailsPrefab == null || deleteModal == null || renameModal == null)
            {
                log.Warning("Main menu layout not recognised; the backup menus are unavailable.");
                return;
            }

            _root = new GameObject("WorldBackups.Templates");
            UnityEngine.Object.DontDestroyOnLoad(_root);
            _root.SetActive(false); // templates never Awake

            // List column: the world selector without its world list behaviour and rows.
            GameObject selector = Clone(startButton.submenu, "WorldBackups.Selector");
            Transform content = selector.GetComponent<ScrollRect>().content;
            foreach (Transform child in content.Cast<Transform>().ToList())
                if (child.name != "CreateButtonWrapper")
                    UnityEngine.Object.DestroyImmediate(child.gameObject);

            BackupsSelector = selector;
            BackupsSelector.AddComponent<BackupsListView>();
            WorldsSelector = Clone(selector, "WorldBackups.WorldSelector");
            UnityEngine.Object.DestroyImmediate(WorldsSelector.GetComponent<BackupsListView>());
            WorldsSelector.AddComponent<WorldsListView>();

            RowTemplate = Clone(rowPrefab.gameObject, "WorldBackups.Row");
            RowTemplate.AddComponent<BackupRow>();

            DetailsTemplate = Clone(detailsPrefab, "WorldBackups.Details", "Wrapper/StarServerCheckbox/Toggle (1)/Label");
            BuildDetailsTemplate(DetailsTemplate, RowTemplate);

            _confirmTemplate = Clone(deleteModal, "WorldBackups.Confirm", "CreateNewWorldModal/CancelBtn");
            _promptTemplate = Clone(renameModal, "WorldBackups.Prompt", "CreateNewWorldModal/CancelBtn");

            log.Msg("Backup menus ready.");
        }

        private static GameObject Clone(GameObject source, string name, params string[] keepLocalized)
        {
            GameObject clone = UnityEngine.Object.Instantiate(source, _root.transform);
            clone.name = name;
            clone.SetActive(true);

            var keep = new HashSet<Transform>(keepLocalized.Select(p => clone.transform.Find(p)).Where(t => t != null));
            foreach (var behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null)
                    continue;
                string type = behaviour.GetType().Name;
                if (GameScripts.Contains(type) || (type.StartsWith("Localize", StringComparison.Ordinal) && !keep.Contains(behaviour.transform)))
                    UnityEngine.Object.DestroyImmediate(behaviour);
            }
            return clone;
        }

        private static void BuildDetailsTemplate(GameObject details, GameObject row)
        {
            Transform wrapper = details.transform.Find("Wrapper");

            // Second text line under the title (kind, size, note), styled like the world list dates.
            var title = wrapper.Find("FileName").GetComponent<TMP_Text>();
            var dateStyle = row.transform.Find("Content/Date").GetComponent<TMP_Text>();
            var info = UnityEngine.Object.Instantiate(title.gameObject, wrapper);
            info.name = "Info";
            info.transform.SetSiblingIndex(title.transform.GetSiblingIndex() + 1);
            var infoText = info.GetComponent<TMP_Text>();
            infoText.font = dateStyle.font;
            infoText.fontStyle = FontStyles.Bold;
            infoText.enableAutoSizing = true;
            infoText.fontSizeMin = 24;
            infoText.fontSizeMax = 40;
            infoText.color = new Color32(0xB9, 0xC9, 0xD3, 0xFF);
            ((RectTransform)info.transform).sizeDelta = new Vector2(800, 100);

            // Two rows of small buttons: [Restore][Delete] then [New world][Note].
            Transform smallButtons = wrapper.Find("Buttons/GameObject");
            var second = UnityEngine.Object.Instantiate(smallButtons.gameObject, smallButtons.parent);
            second.name = "GameObject2";
            second.transform.SetSiblingIndex(smallButtons.GetSiblingIndex() + 1);
            second.transform.Find("Rename").name = "NewWorld";
            Transform note = second.transform.Find("Delete");
            note.name = "Note";
            SetDanger(note, false);

            details.AddComponent<BackupDetailsView>();
        }

        // ------------------------------------------------------------------ menu entries

        internal static void AddMainMenuEntry(MelonLogger.Instance log)
        {
            if (!Ready)
                return;
            var nav = UnityEngine.Object.FindFirstObjectByType<MenuNavigation>();
            if (nav == null || nav.GetComponentsInChildren<Transform>(true).Any(t => t.name == MainMenuButtonName))
                return;

            // Goes right under "Join game", with the same look.
            var mainButtons = nav.GetComponentsInChildren<MenuButtonElement>(true).Where(b => b.transform.parent.name == "MainButtons").ToList();
            var start = mainButtons.FirstOrDefault(b => b.submenu != null && b.submenu.GetComponentInChildren<WorldDirectoriesList>(true) != null);
            int startIndex = start != null ? mainButtons.IndexOf(start) : -1;
            MenuButtonElement template = startIndex >= 0 && startIndex + 1 < mainButtons.Count ? mainButtons[startIndex + 1] : start;
            if (template == null)
            {
                log.Warning("Main menu buttons not found; Backups entry not added.");
                return;
            }

            MenuButtonElement entry = AddMenuButton(nav, template.gameObject, MainMenuButtonName, WorldsSelector);
            entry.GetComponent<Button>().onClick.AddListener(entry.OpenMenu);
        }

        /// <summary>Clones a side menu button, turns it into a "Backups" entry opening <paramref name="submenu"/>.</summary>
        internal static MenuButtonElement AddMenuButton(MenuNavigation nav, GameObject template, string name, GameObject submenu)
        {
            GameObject clone = UnityEngine.Object.Instantiate(template, template.transform.parent);
            clone.name = name;
            clone.transform.SetSiblingIndex(template.transform.GetSiblingIndex() + 1);
            foreach (var behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour != null && (GameScripts.Contains(behaviour.GetType().Name) || behaviour.GetType().Name.StartsWith("Localize", StringComparison.Ordinal)))
                    UnityEngine.Object.DestroyImmediate(behaviour);
            }
            clone.GetComponentInChildren<TMP_Text>(true).text = Loc.T("MenuButton");
            clone.GetComponent<Button>().onClick = new Button.ButtonClickedEvent();
            clone.SetActive(true);

            var element = clone.GetComponent<MenuButtonElement>();
            element.submenu = submenu;

            // MenuNavigation only accepts buttons it collected in Awake.
            FieldInfo field = AccessTools.Field(typeof(MenuNavigation), "_mainButtons");
            var buttons = (MenuButtonElement[])field.GetValue(nav);
            if (!buttons.Contains(element))
                field.SetValue(nav, buttons.Concat(new[] { element }).ToArray());
            return element;
        }

        /// <summary>Re-opens the Backups entry from scratch so every column shows fresh data.</summary>
        internal static void ReopenBackupsMenu(Component context)
        {
            var nav = context != null ? context.GetComponentInParent<MenuNavigation>() : null;
            var entry = nav?.GetComponentsInChildren<MenuButtonElement>(true)
                .FirstOrDefault(b => b.name == MainMenuButtonName || b.name == WorldBackupsMod.PauseButtonName);
            if (entry == null)
                return;
            entry.OpenMenu(); // closes (it is the selected entry)
            entry.OpenMenu(); // opens again
        }

        internal static void ShowMessageInMainMenu(string message)
        {
            var nav = UnityEngine.Object.FindFirstObjectByType<MenuNavigation>();
            if (nav == null)
                return;
            nav.GetComponentsInChildren<MenuButtonElement>(true).FirstOrDefault(b => b.name == MainMenuButtonName)?.OpenMenu();
            Message(nav, message);
        }

        // ------------------------------------------------------------------ modals

        private static GameObject OpenModal(GameObject template, Component context)
        {
            Transform host = context.GetComponentInParent<Canvas>().rootCanvas.transform;
            GameObject modal = UnityEngine.Object.Instantiate(template, host);
            modal.transform.SetAsLastSibling();
            var rt = (RectTransform)modal.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            modal.SetActive(true);
            return modal;
        }

        /// <summary>The game's delete-world modal: warning icon, message, [Cancel] [action].</summary>
        internal static void Confirm(Component context, string message, string action, bool danger, Action onConfirm)
        {
            GameObject modal = OpenModal(_confirmTemplate, context);
            Transform box = modal.transform.Find("CreateNewWorldModal");
            box.Find("NameLabel").GetComponent<TMP_Text>().text = message;
            SetButton(box.Find("CancelBtn"), null, () => UnityEngine.Object.Destroy(modal));
            Transform done = box.Find("DoneBtn");
            SetDanger(done, danger);
            SetButton(done, action, () =>
            {
                UnityEngine.Object.Destroy(modal);
                onConfirm();
            });
        }

        /// <summary>Same modal with a single OK button, for results and errors.</summary>
        internal static void Message(Component context, string message, bool warning = false)
        {
            GameObject modal = OpenModal(_confirmTemplate, context);
            Transform box = modal.transform.Find("CreateNewWorldModal");
            box.Find("NameLabel").GetComponent<TMP_Text>().text = message;
            box.Find("GameObject")?.gameObject.SetActive(warning);
            box.Find("CancelBtn").gameObject.SetActive(false);
            Transform done = box.Find("DoneBtn");
            SetDanger(done, false);
            SetButton(done, Loc.T("OK"), () => UnityEngine.Object.Destroy(modal));
        }

        /// <summary>The game's rename-world modal: title, label + text field, [Cancel] [action].</summary>
        internal static void Prompt(Component context, string title, string label, string initial, string action,
            Func<string, bool> isValid, Action<string> onConfirm)
        {
            GameObject modal = OpenModal(_promptTemplate, context);
            Transform box = modal.transform.Find("CreateNewWorldModal");
            box.Find("Title").GetComponent<TMP_Text>().text = title;
            box.Find("NameLabel").GetComponent<TMP_Text>().text = label;

            var input = box.GetComponentInChildren<TMP_InputField>(true);
            input.text = initial ?? "";
            if (input.placeholder is TMP_Text placeholder)
                placeholder.text = "";
            input.onSubmit = new TMP_InputField.SubmitEvent();

            Transform done = box.Find("DoneBtn");
            var doneButton = done.GetComponent<Button>();
            void Submit()
            {
                string value = input.text.Trim();
                if (isValid != null && !isValid(value))
                    return;
                UnityEngine.Object.Destroy(modal);
                onConfirm(value);
            }

            SetButton(box.Find("CancelBtn"), null, () => UnityEngine.Object.Destroy(modal));
            SetButton(done, action, Submit);
            input.onSubmit.AddListener(_ => Submit());
            modal.AddComponent<PromptValidator>().Init(input, doneButton, isValid);

            input.Select();
            input.ActivateInputField();
        }

        // ------------------------------------------------------------------ helpers

        internal static void SetButton(Transform button, string text, UnityAction onClick)
        {
            if (button == null)
                return;
            if (text != null)
            {
                var label = button.GetComponentInChildren<TMP_Text>(true);
                if (label != null)
                    label.text = text;
            }
            var b = button.GetComponent<Button>();
            b.onClick = new Button.ButtonClickedEvent();
            b.onClick.AddListener(onClick);
        }

        internal static void SetDanger(Transform button, bool danger)
        {
            var style = button.GetComponent<ActionButtonStylization>();
            if (style != null)
                style.isBadButton = danger;
            var background = button.Find("Background")?.GetComponent<Image>();
            var border = button.Find("Border")?.GetComponent<Image>();
            if (background != null)
                background.color = danger ? RedButton : BlueBackground;
            if (border != null)
                border.color = danger ? RedButton : BlueBorder;
        }

        internal static Texture2D LoadTexture(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return null;
            var tex = new Texture2D(2, 2);
            if (tex.LoadImage(File.ReadAllBytes(path)))
                return tex;
            UnityEngine.Object.Destroy(tex);
            return null;
        }

        /// <summary>Column width: the selector's own value, plus the column (SelectorA/BCanvas) MenuNavigation already sized.</summary>
        internal static void SetMinWidth(SelectorMenu selector, float width)
        {
            AccessTools.Field(typeof(SelectorMenu), "minWidth")?.SetValue(selector, width);
            var column = selector.transform.parent?.parent?.GetComponent<LayoutElement>();
            if (column != null)
                column.minWidth = width;
        }

        internal static string KindName(BackupKind kind) => Loc.T("Kind" + kind);

        internal static string FormatSize(long bytes)
        {
            if (bytes >= 1024 * 1024)
                return (bytes / (1024f * 1024f)).ToString("0.0", Loc.Culture) + " MB";
            return Math.Max(1, bytes / 1024) + " KB";
        }

        internal static string FormatDate(DateTime date) =>
            date.ToString("D", Loc.Culture) + " " + date.ToString("T", Loc.Culture);

        private static T Field<T>(object target, string name) where T : class
        {
            if (target == null)
                return null;
            return AccessTools.Field(target.GetType(), name)?.GetValue(target) as T;
        }
    }

    /// <summary>Which world a menu button refers to (pause entry, world rows).</summary>
    internal class WorldRef : MonoBehaviour
    {
        public string World;
    }

    /// <summary>A world-list row reused for one backup or one world.</summary>
    internal class BackupRow : MonoBehaviour
    {
        public BackupEntry Backup;
        private Texture2D _thumbnail;

        public void Bind(string title, string subtitle, string thumbnailPath, GameObject submenu)
        {
            transform.Find("Content/WorldName").GetComponent<TMP_Text>().text = title;
            transform.Find("Content/Date").GetComponent<TMP_Text>().text = subtitle;
            _thumbnail = NativeUi.LoadTexture(thumbnailPath);
            var image = transform.Find("GameImage/Image")?.GetComponent<RawImage>();
            if (image != null)
            {
                image.texture = _thumbnail;
                image.color = _thumbnail != null ? Color.white : new Color(1f, 1f, 1f, 0.08f);
            }
            GetComponent<MenuButtonElement>().submenu = submenu;
        }

        public void MakeLabelOnly()
        {
            Destroy(GetComponent<MenuButtonElement>());
            Destroy(GetComponent<Button>());
            transform.Find("GameImage")?.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_thumbnail != null)
                Destroy(_thumbnail);
        }
    }

    /// <summary>Column listing the worlds that have saves; each opens its backups.</summary>
    internal class WorldsListView : MonoBehaviour
    {
        private void Awake()
        {
            NativeUi.SetMinWidth(GetComponent<SelectorMenu>(), 1000f);
        }

        private void Start()
        {
            NativeUi.SetMinWidth(GetComponent<SelectorMenu>(), 1000f);
            var mod = WorldBackupsMod.Instance;
            Transform content = GetComponent<ScrollRect>().content;
            content.Find("CreateButtonWrapper")?.gameObject.SetActive(false);

            string current = WorldBackupsMod.InGame ? mod.CurrentWorldName() : null;
            foreach (string world in mod.Store.ListWorlds())
            {
                GameObject row = Instantiate(NativeUi.RowTemplate, content);
                row.SetActive(true);
                row.AddComponent<WorldRef>().World = world;
                string title = world == current ? $"{world}  ({Loc.T("Current")})" : world;
                DateTime updated = File.GetLastWriteTime(mod.Store.WorldSaveFile(world));
                row.GetComponent<BackupRow>().Bind(title, NativeUi.FormatDate(updated),
                    Path.Combine(mod.Store.WorldDir(world), BackupStore.WorldFileName + ".jpg"), NativeUi.BackupsSelector);
            }
        }
    }

    /// <summary>Column listing one world's backups, with a "+ Create a backup" button on top.</summary>
    internal class BackupsListView : MonoBehaviour
    {
        private string _world;

        private void Awake()
        {
            // Narrower when it is the second column (main menu), wider as the first one (pause menu).
            bool second = transform.parent != null && transform.parent.parent != null && transform.parent.parent.name.StartsWith("SelectorB", StringComparison.Ordinal);
            NativeUi.SetMinWidth(GetComponent<SelectorMenu>(), second ? 1100f : 1350f);
        }

        private void Start()
        {
            Awake();
            _world = GetComponent<SelectorMenu>().ParentNavigationButton?.GetComponent<WorldRef>()?.World;

            Transform create = GetComponent<ScrollRect>().content.Find("CreateButtonWrapper/CreateWorldButton");
            NativeUi.SetButton(create, Loc.T("CreateBackupButton"), OnCreateClicked);
            Rebuild(false, null);
        }

        private void OnCreateClicked()
        {
            var mod = WorldBackupsMod.Instance;
            if (_world == null || mod.IsBusy)
                return;
            NativeUi.Prompt(this, Loc.T("CreateBackupTitle"), Loc.T("NoteLabel"), "", Loc.T("Create"), null, note =>
            {
                mod.CreateBackup(_world, note,
                    entry => { if (this != null) Rebuild(true, entry.Path); },
                    error => { if (this != null) NativeUi.Message(this, Loc.T("Error", error), true); });
            });
        }

        internal void Rebuild(bool notifyNavigation, string selectPath)
        {
            var mod = WorldBackupsMod.Instance;
            Transform content = GetComponent<ScrollRect>().content;
            foreach (var old in content.GetComponentsInChildren<BackupRow>(true))
                DestroyImmediate(old.gameObject);

            BackupRow toSelect = null;
            var backups = _world != null ? mod.Store.ListBackups(_world) : new List<BackupEntry>();
            foreach (BackupEntry backup in backups)
            {
                GameObject rowObject = Instantiate(NativeUi.RowTemplate, content);
                rowObject.SetActive(true);
                var row = rowObject.GetComponent<BackupRow>();
                row.Backup = backup;
                string subtitle = NativeUi.KindName(backup.Kind) + "  ·  " + NativeUi.FormatSize(backup.SizeBytes);
                if (!string.IsNullOrEmpty(backup.Label))
                    subtitle += "  ·  " + backup.Label;
                row.Bind(NativeUi.FormatDate(backup.Created), subtitle, backup.ScreenshotPath, NativeUi.DetailsTemplate);
                if (selectPath != null && backup.Path == selectPath)
                    toSelect = row;
            }

            if (backups.Count == 0)
            {
                GameObject empty = Instantiate(NativeUi.RowTemplate, content);
                empty.SetActive(true);
                var row = empty.GetComponent<BackupRow>();
                row.Bind(Loc.T("NoBackups"), "", null, null);
                row.MakeLabelOnly();
            }

            if (notifyNavigation)
                GetComponentInParent<ISelectorMenuChangeAware>()?.OnSelectorListChange();
            if (toSelect != null)
                toSelect.GetComponent<MenuButtonElement>().OpenMenu();
        }
    }

    /// <summary>Right panel for one backup: screenshot, date, info, [Load], [Restore][Delete], [New world][Note].</summary>
    internal class BackupDetailsView : MonoBehaviour, IButtonNavigationAwareMenu
    {
        private BackupEntry _backup;
        private BackupsListView _list;
        private Texture2D _screenshot;

        public void Initialize(MenuButtonElement selectedButton)
        {
            var mod = WorldBackupsMod.Instance;
            _backup = selectedButton.GetComponent<BackupRow>()?.Backup;
            _list = selectedButton.GetComponentInParent<BackupsListView>();
            if (_backup == null)
                return;

            Transform wrapper = transform.Find("Wrapper");
            _screenshot = NativeUi.LoadTexture(_backup.ScreenshotPath);
            var screenshot = wrapper.Find("SaveFileScreenshot").GetComponent<RawImage>();
            screenshot.texture = _screenshot;
            screenshot.color = _screenshot != null ? Color.white : new Color(1f, 1f, 1f, 0.05f);

            wrapper.Find("FileName").GetComponent<TMP_Text>().text = _backup.Created.ToString("g", Loc.Culture);
            string info = NativeUi.KindName(_backup.Kind) + "  ·  " + NativeUi.FormatSize(_backup.SizeBytes);
            if (!string.IsNullOrEmpty(_backup.Label))
                info += "\n" + _backup.Label;
            wrapper.Find("Info").GetComponent<TMP_Text>().text = info;

            var toggle = wrapper.Find("StarServerCheckbox")?.GetComponentInChildren<Toggle>(true);
            if (toggle != null)
            {
                toggle.onValueChanged = new Toggle.ToggleEvent();
                toggle.isOn = PlayerPrefs.GetInt(WorldBackupsMod.StartServerPrefKey, 0) > 0;
                toggle.onValueChanged.AddListener(on => PlayerPrefs.SetInt(WorldBackupsMod.StartServerPrefKey, on ? 1 : 0));
            }

            Transform buttons = wrapper.Find("Buttons");
            NativeUi.SetButton(buttons.Find("ActionButton"), Loc.T("Load"), OnLoad);
            NativeUi.SetButton(buttons.Find("GameObject/Rename"), Loc.T("Restore"), OnRestore);
            NativeUi.SetButton(buttons.Find("GameObject/Delete"), Loc.T("Delete"), OnDelete);
            NativeUi.SetButton(buttons.Find("GameObject2/NewWorld"), Loc.T("AsNewWorld"), OnNewWorld);
            NativeUi.SetButton(buttons.Find("GameObject2/Note"), Loc.T("EditNote"), OnEditNote);
            buttons.Find("GameObject2/Note").GetComponent<Button>().interactable = !_backup.IsGameBackup;
        }

        private static string LeaveWarning(bool leaves) =>
            leaves ? Loc.T(WorldBackupsMod.IsClient ? "LeaveWarning" : "LeaveWarningSaved") + "\n" : "";

        private void OnLoad()
        {
            var mod = WorldBackupsMod.Instance;
            if (mod.IsBusy)
                return;
            NativeUi.Confirm(this, LeaveWarning(WorldBackupsMod.InGame) + Loc.T("ConfirmLoad", _backup.World), Loc.T("Load"), false,
                () => mod.Load(_backup, error => NativeUi.Message(this, Loc.T("Error", error), true)));
        }

        private void OnRestore()
        {
            var mod = WorldBackupsMod.Instance;
            if (mod.IsBusy)
                return;
            NativeUi.Confirm(this, LeaveWarning(mod.RestoreLeavesGame(_backup)) + Loc.T("ConfirmRestore", _backup.World), Loc.T("Restore"), false,
                () => mod.Restore(_backup,
                    () =>
                    {
                        var list = _list;
                        list?.Rebuild(true, null);
                        if (list != null)
                            NativeUi.Message(list, Loc.T("Restored", _backup.World));
                    },
                    error => NativeUi.Message(this, Loc.T("Error", error), true)));
        }

        private void OnDelete()
        {
            var mod = WorldBackupsMod.Instance;
            NativeUi.Confirm(this, Loc.T("ConfirmDelete"), Loc.T("Delete"), true, () =>
            {
                if (mod.Delete(_backup, out string error))
                    _list?.Rebuild(true, null);
                else
                    NativeUi.Message(this, Loc.T("Error", error), true);
            });
        }

        private void OnNewWorld()
        {
            var mod = WorldBackupsMod.Instance;
            string suggested = $"{_backup.World} {_backup.Created.ToString("yyyy-MM-dd HH-mm", CultureInfo.InvariantCulture)}";
            NativeUi.Prompt(this, Loc.T("NewWorldTitle"), Loc.T("NameLabel"), suggested, Loc.T("Create"),
                name => BackupStore.ValidateNewWorldName(name, mod.Store) == null,
                name =>
                {
                    Component context = _list != null ? (Component)_list : this;
                    if (mod.RestoreAsNewWorld(_backup, name, out string error))
                    {
                        NativeUi.ReopenBackupsMenu(context);
                        NativeUi.Message(context, Loc.T("CreatedWorld", name));
                    }
                    else
                    {
                        NativeUi.Message(context, Loc.T("Error", error), true);
                    }
                });
        }

        private void OnEditNote()
        {
            var mod = WorldBackupsMod.Instance;
            NativeUi.Prompt(this, Loc.T("EditNoteTitle"), Loc.T("NoteLabel"), _backup.Label, Loc.T("Save"), null, note =>
            {
                try
                {
                    mod.Store.SetLabel(_backup, note);
                    _list?.Rebuild(true, _backup.Path);
                }
                catch (Exception ex)
                {
                    NativeUi.Message(this, Loc.T("Error", ex.Message), true);
                }
            });
        }

        private void OnDestroy()
        {
            if (_screenshot != null)
                Destroy(_screenshot);
        }
    }

    /// <summary>Greys out the modal's action button while the text is not acceptable.</summary>
    internal class PromptValidator : MonoBehaviour
    {
        private TMP_InputField _input;
        private Button _button;
        private Func<string, bool> _isValid;

        public void Init(TMP_InputField input, Button button, Func<string, bool> isValid)
        {
            _input = input;
            _button = button;
            _isValid = isValid;
        }

        private void Update()
        {
            if (_button != null && _input != null)
                _button.interactable = _isValid == null || _isValid(_input.text.Trim());
        }
    }
}
