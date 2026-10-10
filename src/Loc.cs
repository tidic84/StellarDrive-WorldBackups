using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Localization.Settings;

namespace WorldBackups
{
    internal static class Loc
    {
        private static Dictionary<string, string> _table;

        private static readonly Dictionary<string, string> En = new Dictionary<string, string>
        {
            ["MenuButton"] = "Backups",
            ["CreateBackupButton"] = "+ Create a backup",
            ["CreateBackupTitle"] = "Create a backup",
            ["NoteLabel"] = "Note",
            ["NameLabel"] = "Name",
            ["Create"] = "Create",
            ["Load"] = "Load",
            ["Restore"] = "Restore",
            ["Delete"] = "Delete",
            ["AsNewWorld"] = "New world",
            ["EditNote"] = "Note",
            ["NewWorldTitle"] = "Restore as a new world",
            ["EditNoteTitle"] = "Edit note",
            ["Save"] = "Save",
            ["OK"] = "OK",
            ["OpenFolder"] = "Open folder",
            ["NoBackups"] = "No backup yet",
            ["Current"] = "in progress",
            ["KindManual"] = "Manual backup",
            ["KindAuto"] = "Automatic backup",
            ["KindSafety"] = "Before restore",
            ["KindGame"] = "Game autosave",
            ["LeaveWarning"] = "You will leave the current game.",
            ["LeaveWarningSaved"] = "The current game will be saved and closed.",
            ["ConfirmRestore"] = "Replace \"{0}\" with this backup? A safety backup is made first.",
            ["ConfirmLoad"] = "Restore this backup into \"{0}\" and launch it? A safety backup is made first.",
            ["ConfirmDelete"] = "Delete this backup permanently?",
            ["Restored"] = "Backup restored into \"{0}\".",
            ["CreatedWorld"] = "World \"{0}\" created from the backup.",
            ["Error"] = "Error: {0}",
            ["NameEmpty"] = "The name cannot be empty.",
            ["NameInvalid"] = "The name contains invalid characters.",
            ["NameExists"] = "A world with this name already exists.",
        };

        private static readonly Dictionary<string, string> Fr = new Dictionary<string, string>
        {
            ["MenuButton"] = "Backups",
            ["CreateBackupButton"] = "+ Créer une backup",
            ["CreateBackupTitle"] = "Créer une backup",
            ["NoteLabel"] = "Note",
            ["NameLabel"] = "Nom",
            ["Create"] = "Créer",
            ["Load"] = "Charger",
            ["Restore"] = "Restaurer",
            ["Delete"] = "Supprimer",
            ["AsNewWorld"] = "Nouveau monde",
            ["EditNote"] = "Note",
            ["NewWorldTitle"] = "Restaurer en nouveau monde",
            ["EditNoteTitle"] = "Modifier la note",
            ["Save"] = "Enregistrer",
            ["OK"] = "OK",
            ["OpenFolder"] = "Ouvrir le dossier",
            ["NoBackups"] = "Aucune backup pour l'instant",
            ["Current"] = "en cours",
            ["KindManual"] = "Backup manuelle",
            ["KindAuto"] = "Backup automatique",
            ["KindSafety"] = "Avant restauration",
            ["KindGame"] = "Autosave du jeu",
            ["LeaveWarning"] = "Vous allez quitter la partie en cours.",
            ["LeaveWarningSaved"] = "La partie en cours va être sauvegardée puis fermée.",
            ["ConfirmRestore"] = "Remplacer \"{0}\" par cette backup ? Une backup de sécurité est faite avant.",
            ["ConfirmLoad"] = "Restaurer cette backup dans \"{0}\" et la lancer ? Une backup de sécurité est faite avant.",
            ["ConfirmDelete"] = "Supprimer cette backup de façon permanente ?",
            ["Restored"] = "Backup restaurée dans \"{0}\".",
            ["CreatedWorld"] = "Monde \"{0}\" créé depuis la backup.",
            ["Error"] = "Erreur : {0}",
            ["NameEmpty"] = "Le nom ne peut pas être vide.",
            ["NameInvalid"] = "Le nom contient des caractères invalides.",
            ["NameExists"] = "Un monde porte déjà ce nom.",
        };

        public static CultureInfo Culture { get; private set; } = CultureInfo.InvariantCulture;

        public static void Init(string language)
        {
            string lang = (language ?? "auto").Trim().ToLowerInvariant();
            if (lang == "auto")
                lang = GameLanguage();
            _table = lang == "fr" ? Fr : En;
            try
            {
                Culture = CultureInfo.GetCultureInfo(lang == "fr" ? "fr-FR" : lang);
            }
            catch
            {
                Culture = CultureInfo.GetCultureInfo("en-US");
            }
        }

        /// <summary>The language picked in the game's own options (falls back to the system language).</summary>
        private static string GameLanguage()
        {
            try
            {
                string code = LocalizationSettings.SelectedLocale?.Identifier.Code;
                if (!string.IsNullOrEmpty(code))
                    return code.Split('-')[0].ToLowerInvariant();
            }
            catch
            {
            }
            return Application.systemLanguage == SystemLanguage.French ? "fr" : "en";
        }

        public static string T(string key)
        {
            if (_table == null)
                Init("auto");
            return _table.TryGetValue(key, out string value) ? value : En.TryGetValue(key, out value) ? value : key;
        }

        public static string T(string key, params object[] args) => string.Format(T(key), args);
    }
}
