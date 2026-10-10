using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace WorldBackups
{
    internal static class Loc
    {
        private static Dictionary<string, string> _table;

        private static readonly Dictionary<string, string> En = new Dictionary<string, string>
        {
            ["Title"] = "World Backups",
            ["OpenButton"] = "World Backups ({0})",
            ["Worlds"] = "Worlds",
            ["NoWorlds"] = "No world found.\nCreate a world in the game first.",
            ["SelectWorld"] = "Select a world on the left.",
            ["CreateBackup"] = "Back up now",
            ["SaveAndBackup"] = "Save + back up",
            ["LabelHint"] = "Note (optional)",
            ["Backups"] = "Backups of \"{0}\"",
            ["NoBackups"] = "No backup yet for this world.",
            ["Load"] = "Load",
            ["Restore"] = "Restore",
            ["AsNewWorld"] = "As new world",
            ["Rename"] = "Note",
            ["Delete"] = "Delete",
            ["Confirm"] = "Confirm",
            ["Cancel"] = "Cancel",
            ["OK"] = "OK",
            ["OpenFolder"] = "Open folder",
            ["Refresh"] = "Refresh",
            ["Close"] = "Close",
            ["HostToggle"] = "Host (Steam) when loading",
            ["KindManual"] = "Manual",
            ["KindAuto"] = "Auto",
            ["KindSafety"] = "Before restore",
            ["KindGame"] = "Game autosave",
            ["InGameNotice"] = "In game: return to the main menu to restore or load a backup.",
            ["ClientNotice"] = "You are a client in someone else's world: nothing to back up here.",
            ["ConfirmRestore"] = "Replace \"{0}\" with this backup? (a safety backup is made first)",
            ["ConfirmLoad"] = "Restore this backup into \"{0}\" and launch it? (a safety backup is made first)",
            ["ConfirmDelete"] = "Delete this backup permanently?",
            ["NewWorldName"] = "New world name:",
            ["Created"] = "Backup created.",
            ["Restored"] = "Backup restored into \"{0}\".",
            ["CreatedWorld"] = "World \"{0}\" created from backup.",
            ["Deleted"] = "Backup deleted.",
            ["Saving"] = "Saving the game...",
            ["Error"] = "Error: {0}",
            ["NameEmpty"] = "Name cannot be empty.",
            ["NameInvalid"] = "Name contains invalid characters.",
            ["NameExists"] = "A world with this name already exists.",
            ["Current"] = "current",
        };

        private static readonly Dictionary<string, string> Fr = new Dictionary<string, string>
        {
            ["Title"] = "Backups de mondes",
            ["OpenButton"] = "Backups de mondes ({0})",
            ["Worlds"] = "Mondes",
            ["NoWorlds"] = "Aucun monde trouvé.\nCréez d'abord un monde dans le jeu.",
            ["SelectWorld"] = "Sélectionnez un monde à gauche.",
            ["CreateBackup"] = "Faire une backup",
            ["SaveAndBackup"] = "Sauver + backup",
            ["LabelHint"] = "Note (optionnelle)",
            ["Backups"] = "Backups de \"{0}\"",
            ["NoBackups"] = "Aucune backup pour ce monde.",
            ["Load"] = "Charger",
            ["Restore"] = "Restaurer",
            ["AsNewWorld"] = "Nouveau monde",
            ["Rename"] = "Note",
            ["Delete"] = "Suppr.",
            ["Confirm"] = "Confirmer",
            ["Cancel"] = "Annuler",
            ["OK"] = "OK",
            ["OpenFolder"] = "Ouvrir le dossier",
            ["Refresh"] = "Actualiser",
            ["Close"] = "Fermer",
            ["HostToggle"] = "Héberger (Steam) au chargement",
            ["KindManual"] = "Manuelle",
            ["KindAuto"] = "Auto",
            ["KindSafety"] = "Avant restauration",
            ["KindGame"] = "Autosave du jeu",
            ["InGameNotice"] = "En jeu : retournez au menu principal pour restaurer ou charger une backup.",
            ["ClientNotice"] = "Vous êtes client dans le monde d'un autre joueur : rien à sauvegarder ici.",
            ["ConfirmRestore"] = "Remplacer \"{0}\" par cette backup ? (une backup de sécurité est faite avant)",
            ["ConfirmLoad"] = "Restaurer cette backup dans \"{0}\" et lancer le monde ? (une backup de sécurité est faite avant)",
            ["ConfirmDelete"] = "Supprimer définitivement cette backup ?",
            ["NewWorldName"] = "Nom du nouveau monde :",
            ["Created"] = "Backup créée.",
            ["Restored"] = "Backup restaurée dans \"{0}\".",
            ["CreatedWorld"] = "Monde \"{0}\" créé depuis la backup.",
            ["Deleted"] = "Backup supprimée.",
            ["Saving"] = "Sauvegarde du jeu en cours...",
            ["Error"] = "Erreur : {0}",
            ["NameEmpty"] = "Le nom ne peut pas être vide.",
            ["NameInvalid"] = "Le nom contient des caractères invalides.",
            ["NameExists"] = "Un monde porte déjà ce nom.",
            ["Current"] = "actuel",
        };

        public static void Init(string language)
        {
            string lang = (language ?? "auto").Trim().ToLowerInvariant();
            if (lang == "auto")
                lang = Application.systemLanguage == SystemLanguage.French ? "fr" : "en";
            _table = lang == "fr" ? Fr : En;
            Culture = CultureInfo.GetCultureInfo(lang == "fr" ? "fr-FR" : "en-US");
        }

        public static CultureInfo Culture { get; private set; } = CultureInfo.InvariantCulture;

        public static string T(string key)
        {
            if (_table == null)
                Init("auto");
            return _table.TryGetValue(key, out string value) ? value : En.TryGetValue(key, out value) ? value : key;
        }

        public static string T(string key, params object[] args) => string.Format(T(key), args);
    }
}
