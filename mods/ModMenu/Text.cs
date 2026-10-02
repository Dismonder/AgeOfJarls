using System.Collections.Generic;

namespace ModMenu
{
    /// <summary>The menu's own strings, Polish when the game is in Polish, English otherwise.</summary>
    internal static class T
    {
        private static readonly Dictionary<string, (string en, string pl)> Strings = new Dictionary<string, (string, string)>
        {
            ["menu_button"] = ("Mods", "Mody"),
            ["title"] = ("Mods ({0})", "Mody ({0})"),
            ["close"] = ("Close", "Zamknij"),
            ["back"] = ("Back", "Wróć"),
            ["profiles"] = ("Profiles", "Profile"),
            ["check_updates"] = ("Check updates", "Sprawdź aktualizacje"),
            ["checking"] = ("Checking... {0}/{1}", "Sprawdzam... {0}/{1}"),
            ["restart"] = ("Restart game", "Uruchom ponownie"),
            ["restart_needed"] = ("Changes apply after a restart.", "Zmiany zadziałają po ponownym uruchomieniu gry."),
            ["no_patcher"] = ("ModMenu.Patcher is missing from BepInEx/patchers - switching mods off will not work.",
                "Brak ModMenu.Patcher w BepInEx/patchers - wyłączanie modów nie zadziała."),
            ["search"] = ("Search...", "Szukaj..."),
            ["settings"] = ("Settings", "Ustawienia"),
            ["page"] = ("Page", "Strona"),
            ["state_loaded"] = ("Loaded", "Działa"),
            ["state_disabled"] = ("Switched off", "Wyłączony"),
            ["only_for"] = ("Not for this program (only {0})", "Nie dla tego programu (tylko {0})"),
            ["state_failed"] = ("Failed to load", "Błąd ładowania"),
            ["state_off_next"] = ("Off after restart", "Wyłączy się po restarcie"),
            ["state_on_next"] = ("On after restart", "Włączy się po restarcie"),
            ["protected"] = ("Required by Mod Menu", "Wymagany przez Mod Menu"),
            ["update"] = ("Update: {0}", "Aktualizacja: {0}"),
            ["up_to_date"] = ("Up to date", "Aktualny"),
            ["needs"] = ("Needs: {0}", "Wymaga: {0}"),
            ["also_stops"] = ("Also stops: {0}", "Przestaną działać też: {0}"),
            ["update_error"] = ("Update check failed: {0}", "Nie udało się sprawdzić aktualizacji: {0}"),
            ["updates_found"] = ("Updates available: {0}", "Dostępne aktualizacje: {0}"),
            ["no_updates"] = ("All checked mods are up to date.", "Wszystkie sprawdzone mody są aktualne."),
            ["config_title"] = ("{0} - settings", "{0} - ustawienia"),
            ["config_none"] = ("This mod has no settings.", "Ten mod nie ma ustawień."),
            ["config_off"] = ("Switch the mod on and restart the game to edit its settings.",
                "Włącz moda i uruchom ponownie grę, żeby zmienić jego ustawienia."),
            ["config_hint"] = ("Saved at once. Some mods read a setting only at startup.",
                "Zapisuje się od razu. Niektóre mody czytają ustawienie tylko przy starcie gry."),
            ["reset"] = ("Default", "Domyślne"),
            ["invalid"] = ("Invalid value", "Nieprawidłowa wartość"),
            ["profile_name"] = ("New profile name...", "Nazwa nowego profilu..."),
            ["profile_save"] = ("Save current", "Zapisz obecny"),
            ["profile_load"] = ("Load", "Wczytaj"),
            ["profile_overwrite"] = ("Overwrite", "Nadpisz"),
            ["profile_delete"] = ("Delete", "Usuń"),
            ["profile_active"] = ("(current)", "(obecny)"),
            ["profile_none"] = ("No profiles yet. A profile remembers which mods are switched off.",
                "Brak profili. Profil zapamiętuje, które mody są wyłączone."),
            ["profile_count"] = ("{0} off", "wyłączonych: {0}"),
            ["all_on"] = ("Switch all on", "Włącz wszystkie"),
        };

        public static string Get(string key, params object[] args)
        {
            if (!Strings.TryGetValue(key, out (string en, string pl) text))
            {
                return key;
            }
            bool polish = Localization.instance != null && Localization.instance.GetSelectedLanguage() == "Polish";
            string value = polish ? text.pl : text.en;
            return args.Length > 0 ? string.Format(value, args) : value;
        }
    }
}
