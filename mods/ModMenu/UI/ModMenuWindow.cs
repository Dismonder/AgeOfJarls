using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace ModMenu.UI
{
    /// <summary>
    /// The window opened from the main menu: the mod list (switch on/off, update state, links), one mod's settings, and
    /// the profiles. Lives in Jotunn's CustomGUIFront, which is rebuilt with the scene, so it is created on demand.
    /// </summary>
    internal sealed class ModMenuWindow : MonoBehaviour
    {
        private enum View
        {
            Mods,
            Config,
            Profiles,
        }

        private const float Width = 1060f;
        private const float Height = 740f;
        private const float Margin = 30f;
        private const float ListTop = 158f;
        // Usable row width: window minus margins, scrollbar and list padding.
        private const float RowWidth = Width - 2f * Margin - 12f - 14f;

        private static ModMenuWindow _instance;
        private static bool _checkedThisSession;

        private View _view = View.Mods;
        private ModEntry _configMod;
        private Action _onClosed;
        // Rows are rebuilt on the next frame, never inside the click handler of a control that the rebuild destroys.
        private bool _dirty;
        private bool _typingLastFrame;

        private Text _title;
        private Text _status;
        private Button _back;
        private Button _profilesButton;
        private Button _updatesButton;
        private Text _updatesLabel;
        private Button _restartButton;
        private InputField _search;
        private Button _allOnButton;
        private InputField _profileName;
        private Button _profileSave;
        private InputField _configSearch;
        private readonly HashSet<string> _collapsed = new HashSet<string>();
        private ModEntry _collapsedFor;
        private const int ExpandedLimit = 40;
        private RectTransform _list;

        public static bool IsOpen => _instance != null && _instance.gameObject.activeSelf;

        public static void Open(Action onClosed)
        {
            if (_instance == null)
            {
                _instance = Create();
            }
            _instance._onClosed = onClosed;
            _instance._view = View.Mods;
            _instance.gameObject.SetActive(true);
            _instance.transform.SetAsLastSibling();
            _instance.Rebuild();

            if (!_checkedThisSession && Plugin.CheckUpdatesOnOpen.Value)
            {
                _checkedThisSession = true;
                _instance.StartUpdateCheck();
            }
        }

        public void Close()
        {
            gameObject.SetActive(false);
            Action onClosed = _onClosed;
            _onClosed = null;
            onClosed?.Invoke();
        }

        private void Update()
        {
            if (_dirty)
            {
                _dirty = false;
                Rebuild();
            }
            // Esc while typing (search, profile name, a setting) only leaves the field.
            GameObject selected = UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
            InputField field = selected != null ? selected.GetComponent<InputField>() : null;
            // The field may already have handled Esc this frame (EventSystem runs first), so last frame counts too.
            bool typing = field != null && field.isFocused;
            bool wasTyping = _typingLastFrame;
            _typingLastFrame = typing;
            if (ZInput.GetKeyDown(KeyCode.Escape) && !typing && !wasTyping)
            {
                if (_view == View.Mods)
                {
                    Close();
                }
                else
                {
                    ShowView(View.Mods);
                }
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        // ------------------------------------------------------------------ building

        private static ModMenuWindow Create()
        {
            GameObject panel = GUIManager.Instance.CreateWoodpanel(GUIManager.CustomGUIFront.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Width, Height, false);
            panel.name = "ModMenuWindow";
            RectTransform canvas = GUIManager.CustomGUIFront.GetComponent<RectTransform>();
            if (canvas != null && canvas.rect.width > 0f && canvas.rect.height > 0f)
            {
                float scale = Mathf.Min(1f, (canvas.rect.width - 40f) / Width, (canvas.rect.height - 40f) / Height);
                panel.transform.localScale = Vector3.one * Mathf.Max(0.5f, scale);
            }
            var window = panel.AddComponent<ModMenuWindow>();
            window.Build(panel.transform);
            return window;
        }

        private void Build(Transform root)
        {
            GUIManager gui = GUIManager.Instance;

            _back = UiKit.Button(root, "< " + T.Get("back"), () => ShowView(View.Mods));
            UiKit.Place(_back.gameObject, Margin, 26f, 120f, 40f);

            _title = UiKit.Label(root, "", 28, gui.ValheimOrange);
            UiKit.Place(_title.gameObject, Margin, 26f, 520f, 40f);

            Button close = UiKit.Button(root, T.Get("close"), Close);
            UiKit.Place(close.gameObject, Width - Margin - 140f, 26f, 140f, 40f);

            _updatesButton = UiKit.Button(root, T.Get("check_updates"), StartUpdateCheck);
            _updatesLabel = _updatesButton.GetComponentInChildren<Text>();
            UiKit.Place(_updatesButton.gameObject, Width - Margin - 140f - 10f - 240f, 26f, 240f, 40f);

            _profilesButton = UiKit.Button(root, T.Get("profiles"), () => ShowView(View.Profiles));
            UiKit.Place(_profilesButton.gameObject, Width - Margin - 140f - 10f - 240f - 10f - 150f, 26f, 150f, 40f);

            _status = UiKit.Label(root, "", 16, gui.ValheimBeige, false);
            UiKit.Place(_status.gameObject, Margin, 72f, Width - 2f * Margin - 250f, 40f);

            _restartButton = UiKit.Button(root, T.Get("restart"), GameRestart.Restart);
            UiKit.Place(_restartButton.gameObject, Width - Margin - 230f, 72f, 230f, 40f);

            _search = UiKit.Input(root, T.Get("search"));
            UiKit.Place(_search.gameObject, Margin, 116f, 360f, 34f);
            _search.onValueChanged.AddListener(_ => RebuildList());

            _allOnButton = UiKit.Button(root, T.Get("all_on"), () =>
            {
                ModCatalog.ReplaceWanted(new string[0]);
                _dirty = true;
            });
            UiKit.Place(_allOnButton.gameObject, Margin + 370f, 114f, 220f, 38f);

            _profileName = UiKit.Input(root, T.Get("profile_name"));
            UiKit.Place(_profileName.gameObject, Margin, 116f, 360f, 34f);

            _profileSave = UiKit.Button(root, T.Get("profile_save"), () =>
            {
                if (Profiles.Save(_profileName.text, ModCatalog.WantedDisabled) != null)
                {
                    _profileName.text = "";
                }
                _dirty = true;
            });
            UiKit.Place(_profileSave.gameObject, Margin + 370f, 114f, 220f, 38f);

            _configSearch = UiKit.Input(root, T.Get("config_search"));
            UiKit.Place(_configSearch.gameObject, Margin, 116f, 360f, 34f);
            _configSearch.onValueChanged.AddListener(_ => _dirty = true);

            _list = UiKit.ScrollList(root, Margin, ListTop, Margin, Margin);
        }

        private void ShowView(View view, ModEntry configMod = null)
        {
            _view = view;
            if (configMod != null && configMod != _configMod)
            {
                _configSearch.SetTextWithoutNotify("");
            }
            _configMod = configMod ?? _configMod;
            _list.anchoredPosition = Vector2.zero;
            _dirty = true;
        }

        private void Rebuild()
        {
            bool mods = _view == View.Mods;
            _back.gameObject.SetActive(!mods);
            // In the list view the title must stop before the Profiles button; sub-views only have Back and Close.
            _title.rectTransform.anchoredPosition = new Vector2(mods ? Margin : Margin + 135f, -26f);
            _title.rectTransform.sizeDelta = new Vector2(mods ? 420f : Width - 2f * Margin - 135f - 160f, 40f);
            _profilesButton.gameObject.SetActive(mods);
            _updatesButton.gameObject.SetActive(mods);
            _search.gameObject.SetActive(mods);
            _allOnButton.gameObject.SetActive(mods && ModCatalog.PatcherActive);
            _profileName.gameObject.SetActive(_view == View.Profiles);
            _profileSave.gameObject.SetActive(_view == View.Profiles);
            _configSearch.gameObject.SetActive(_view == View.Config);
            RefreshHeader();
            RebuildList();
        }

        private void RefreshHeader()
        {
            GUIManager gui = GUIManager.Instance;
            switch (_view)
            {
                case View.Mods:
                    _title.text = T.Get("title", ModCatalog.Mods.Count);
                    break;
                case View.Config:
                    _title.text = T.Get("config_title", _configMod?.Name);
                    break;
                case View.Profiles:
                    _title.text = T.Get("profiles");
                    break;
            }

            _updatesLabel.text = UpdateChecker.Running ? T.Get("checking", UpdateChecker.Progress, UpdateChecker.Total) : T.Get("check_updates");
            UiKit.SetEnabled(_updatesButton, !UpdateChecker.Running);

            bool restart = ModCatalog.RestartNeeded;
            _restartButton.gameObject.SetActive(restart);
            if (!ModCatalog.PatcherActive)
            {
                SetStatus(T.Get("no_patcher"), Color.red);
            }
            else if (restart)
            {
                SetStatus(T.Get("restart_needed"), gui.ValheimYellow);
            }
            // Update news belongs to the mod list; the settings and profile views keep the line for their own notices.
            else if (_view == View.Config)
            {
                SetStatus(T.Get("config_hint"), gui.ValheimBeige);
            }
            else if (_view != View.Mods)
            {
                SetStatus("", gui.ValheimBeige);
            }
            else if (UpdateChecker.LastError != null)
            {
                SetStatus(T.Get("update_error", UpdateChecker.LastError), new Color(1f, 0.5f, 0.4f));
            }
            else if (UpdateChecker.Done)
            {
                int updates = ModCatalog.Mods.Count(m => m.UpdateAvailable);
                SetStatus(updates > 0 ? T.Get("updates_found", updates) : T.Get("no_updates"), updates > 0 ? gui.ValheimOrange : gui.ValheimBeige);
            }
            else
            {
                SetStatus("", gui.ValheimBeige);
            }
        }

        private void SetStatus(string text, Color color)
        {
            _status.text = text;
            _status.color = color;
        }

        private void StartUpdateCheck()
        {
            if (UpdateChecker.Running)
            {
                return;
            }
            // Runs on the plugin object, so closing the window does not cut the check short.
            Plugin.Instance.StartCoroutine(UpdateChecker.Run(ModCatalog.Mods, () =>
            {
                if (this == null || !gameObject.activeSelf)
                {
                    return;
                }
                RefreshHeader();
                if (!UpdateChecker.Running && _view == View.Mods)
                {
                    RebuildList();
                }
            }));
        }

        private void RebuildList()
        {
            UiKit.Clear(_list);
            switch (_view)
            {
                case View.Mods:
                    BuildModRows();
                    break;
                case View.Config:
                    BuildConfigRows();
                    break;
                case View.Profiles:
                    BuildProfileRows();
                    break;
            }
        }

        // ------------------------------------------------------------------ mods

        private void BuildModRows()
        {
            GUIManager gui = GUIManager.Instance;
            string filter = _search.text.Trim();
            int index = 0;
            foreach (ModEntry mod in ModCatalog.Mods)
            {
                if (filter.Length > 0 && mod.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0
                    && mod.Guid.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }
                GameObject row = UiKit.Row(_list, 60f, index++ % 2 == 0);
                bool wantedOff = ModCatalog.IsWantedDisabled(mod.Guid);

                Toggle toggle = UiKit.Toggle(row.transform, !wantedOff, on =>
                {
                    ModCatalog.SetWantedDisabled(mod.Guid, !on);
                    _dirty = true;
                });
                UiKit.PlaceLeft(toggle.gameObject, 12f, 28f, 28f);
                toggle.interactable = ModCatalog.PatcherActive && !mod.Protected;

                Text name = UiKit.Label(row.transform, $"{mod.Name}  <size=14><color=#a89a80>v{mod.Version}</color></size>", 19,
                    wantedOff ? new Color(0.6f, 0.6f, 0.6f) : gui.ValheimBeige);
                name.supportRichText = true;
                UiKit.PlaceLeft(name.gameObject, 52f, RowWidth - 52f - 420f, 28f, 12f);

                Text details = UiKit.Label(row.transform, "", 14, gui.ValheimBeige, false);
                UiKit.PlaceLeft(details.gameObject, 52f, RowWidth - 52f - 420f, 24f, -14f);
                (details.text, details.color) = StateLine(mod, wantedOff);

                Button settings = UiKit.Button(row.transform, T.Get("settings"), () => ShowView(View.Config, mod), 15);
                UiKit.PlaceRight(settings.gameObject, 10f, 130f, 36f);
                UiKit.SetEnabled(settings, mod.Instance != null);

                string url = mod.Source?.PageUrl;
                if (url != null)
                {
                    Button page = UiKit.Button(row.transform, T.Get("page"), () => Application.OpenURL(url), 15);
                    UiKit.PlaceRight(page.gameObject, 150f, 100f, 36f);
                }

                if (mod.LatestVersion != null)
                {
                    Text update = UiKit.Label(row.transform, mod.UpdateAvailable ? T.Get("update", mod.LatestVersion) : T.Get("up_to_date"), 15,
                        mod.UpdateAvailable ? gui.ValheimOrange : new Color(0.55f, 0.75f, 0.5f), true, TextAnchor.MiddleRight);
                    UiKit.PlaceRight(update.gameObject, 260f, 150f, 40f);
                }
            }
        }

        private static (string, Color) StateLine(ModEntry mod, bool wantedOff)
        {
            var green = new Color(0.55f, 0.75f, 0.5f);
            var grey = new Color(0.6f, 0.6f, 0.6f);
            var yellow = GUIManager.Instance.ValheimYellow;
            var red = new Color(1f, 0.45f, 0.4f);

            if (mod.Protected)
            {
                return (T.Get("protected"), grey);
            }
            bool offNow = mod.State == ModState.Disabled;
            if (wantedOff && !offNow)
            {
                List<ModEntry> dependents = ModCatalog.Dependents(mod.Guid).Where(d => !ModCatalog.IsWantedDisabled(d.Guid)).ToList();
                string line = T.Get("state_off_next");
                if (dependents.Count > 0)
                {
                    line += " - " + T.Get("also_stops", string.Join(", ", dependents.Select(d => d.Name).ToArray()));
                }
                return (line, yellow);
            }
            if (!wantedOff && offNow)
            {
                List<string> missing = ModCatalog.MissingDependencies(mod);
                return (T.Get("state_on_next") + (missing.Count > 0 ? " - " + T.Get("needs", string.Join(", ", missing.ToArray())) : ""), yellow);
            }
            switch (mod.State)
            {
                case ModState.Disabled:
                    return (T.Get("state_disabled"), grey);
                case ModState.Failed when mod.OnlyFor != null:
                    return (T.Get("only_for", mod.OnlyFor), grey);
                case ModState.Failed:
                    return (T.Get("state_failed") + (mod.Error != null ? ": " + mod.Error : ""), red);
                default:
                    return (T.Get("state_loaded"), green);
            }
        }

        // ------------------------------------------------------------------ config

        private void BuildConfigRows()
        {
            GUIManager gui = GUIManager.Instance;
            List<ConfigFile> files = _configMod?.Instance != null ? ConfigSources.For(_configMod.Instance) : new List<ConfigFile>();
            if (_configMod?.Instance == null)
            {
                Message(T.Get("config_off"));
                return;
            }

            // One group per section (per file and section when the mod has several files).
            var groups = new List<(string key, string title, List<ConfigEntryBase> entries)>();
            foreach (ConfigFile file in files)
            {
                string prefix = files.Count > 1 ? System.IO.Path.GetFileNameWithoutExtension(file.ConfigFilePath) + " / " : "";
                foreach (IGrouping<string, ConfigEntryBase> section in ConfigEditors.Visible(file).GroupBy(ConfigEditors.Category))
                {
                    groups.Add((file.ConfigFilePath + "|" + section.Key, prefix + section.Key, section.ToList()));
                }
            }
            if (groups.Count == 0)
            {
                Message(T.Get("config_none"));
                return;
            }

            // Big mods (hundreds of settings) open with every section folded: building them all at once stalls the menu.
            if (_collapsedFor != _configMod)
            {
                _collapsedFor = _configMod;
                _collapsed.Clear();
                if (groups.Sum(g => g.entries.Count) > ExpandedLimit)
                {
                    _collapsed.UnionWith(groups.Select(g => g.key));
                }
            }

            string filter = _configSearch.text.Trim();
            bool searching = filter.Length > 0;
            int shown = 0;
            foreach ((string key, string title, List<ConfigEntryBase> entries) in groups)
            {
                List<ConfigEntryBase> matching = entries.Where(e => ConfigEditors.Matches(e, filter)).ToList();
                if (matching.Count == 0)
                {
                    continue;
                }
                bool open = searching || !_collapsed.Contains(key);
                SectionHeader(title, matching.Count, open, searching ? null : (Action)(() =>
                {
                    if (!_collapsed.Remove(key))
                    {
                        _collapsed.Add(key);
                    }
                    _dirty = true;
                }));
                if (!open)
                {
                    continue;
                }
                foreach (ConfigEntryBase entry in matching)
                {
                    ConfigEditors.Row(_list, entry, RowWidth, () => _dirty = true);
                    shown++;
                }
            }
            if (searching && shown == 0)
            {
                Message(T.Get("config_no_match"));
            }
        }

        private void SectionHeader(string title, int count, bool open, Action toggle)
        {
            GUIManager gui = GUIManager.Instance;
            GameObject header = UiKit.Row(_list, 38f, false);
            string marker = toggle == null ? "" : open ? "[-]  " : "[+]  ";
            Text label = UiKit.Label(header.transform, $"{marker}{title}  <size=15><color=#a89a80>({count})</color></size>", 21, gui.ValheimOrange);
            label.supportRichText = true;
            UiKit.Stretch(label.gameObject);
            label.rectTransform.offsetMin = new Vector2(8f, 0f);
            if (toggle != null)
            {
                var image = header.GetComponent<Image>();
                image.raycastTarget = true;
                image.color = new Color(0f, 0f, 0f, 0.25f);
                var button = header.AddComponent<Button>();
                button.targetGraphic = image;
                button.onClick.AddListener(() => toggle());
            }
        }
        private void Message(string text)
        {
            GameObject row = UiKit.Row(_list, 60f, false);
            Text label = UiKit.Label(row.transform, text, 18, GUIManager.Instance.ValheimBeige, false, TextAnchor.MiddleCenter);
            UiKit.Stretch(label.gameObject);
        }

        // ------------------------------------------------------------------ profiles

        private void BuildProfileRows()
        {
            GUIManager gui = GUIManager.Instance;
            List<string> names = Profiles.Names();
            if (names.Count == 0)
            {
                Message(T.Get("profile_none"));
                return;
            }
            HashSet<string> current = ModCatalog.WantedDisabled;
            int index = 0;
            foreach (string profile in names)
            {
                HashSet<string> disabled = Profiles.Read(profile);
                bool active = disabled.SetEquals(current);
                GameObject row = UiKit.Row(_list, 54f, index++ % 2 == 0);

                Text name = UiKit.Label(row.transform, profile + (active ? "  " + T.Get("profile_active") : ""), 19, active ? gui.ValheimOrange : gui.ValheimBeige);
                UiKit.PlaceLeft(name.gameObject, 14f, RowWidth - 14f - 480f, 28f, 10f);
                Text count = UiKit.Label(row.transform, T.Get("profile_count", disabled.Count), 14, new Color(0.65f, 0.6f, 0.5f), false);
                UiKit.PlaceLeft(count.gameObject, 14f, RowWidth - 14f - 480f, 22f, -13f);

                Button delete = UiKit.Button(row.transform, T.Get("profile_delete"), () =>
                {
                    Profiles.Delete(profile);
                    _dirty = true;
                }, 15);
                UiKit.PlaceRight(delete.gameObject, 10f, 120f, 36f);

                Button overwrite = UiKit.Button(row.transform, T.Get("profile_overwrite"), () =>
                {
                    Profiles.Save(profile, ModCatalog.WantedDisabled);
                    _dirty = true;
                }, 15);
                UiKit.PlaceRight(overwrite.gameObject, 140f, 140f, 36f);

                Button load = UiKit.Button(row.transform, T.Get("profile_load"), () =>
                {
                    ModCatalog.ReplaceWanted(Profiles.Read(profile));
                    ShowView(View.Mods);
                }, 15);
                UiKit.PlaceRight(load.gameObject, 290f, 140f, 36f);
                UiKit.SetEnabled(load, ModCatalog.PatcherActive && !active);
            }
        }
    }
}
