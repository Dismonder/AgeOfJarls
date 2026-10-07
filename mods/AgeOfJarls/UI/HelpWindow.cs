using System.Collections.Generic;
using AgeOfJarls.Core;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace AgeOfJarls.UI
{
    /// <summary>
    /// The mod's manual in the game, under UI/HelpKey (F6): chapters down the left, the chapter's text on the right in
    /// a scrolling pane (mouse wheel or drag). The texts are localization entries (aoj_help_*), with the current keys
    /// filled in. Opened and closed with the key, closed with Esc like every window of the mod.
    /// </summary>
    internal sealed class HelpWindow : AojWindow
    {
        private const float Width = 1000f;
        private const float Height = 680f;
        private const float ChapterWidth = 230f;
        private const float ChapterHeight = 30f;
        private const float PaneWidth = 700f;

        private static readonly string[] Chapters =
        {
            "start", "settlers", "orders", "work", "storage", "needs", "family", "army", "map", "portals", "coop", "keys", "console", "settings", "about",
        };

        private static HelpWindow s_instance;

        private readonly List<Text> _chapterLabels = new List<Text>();
        private Text _title;
        private TextArea _body;
        private int _chapter = -1;

        /// <summary>Every frame (Plugin): the help key opens the manual, or closes it when it is open.</summary>
        internal static void Poll()
        {
            if (AoJConfig.HelpKey == null || AoJConfig.HelpKey.Value == KeyCode.None || Player.m_localPlayer == null ||
                !ZInput.GetKeyDown(AoJConfig.HelpKey.Value))
            {
                return;
            }
            if (s_instance != null && s_instance.gameObject.activeSelf)
            {
                s_instance.Hide();
                return;
            }
            // Not while typing or in the game's own menus.
            if (global::Console.IsVisible() || (Chat.instance != null && Chat.instance.HasFocus()) || global::TextInput.IsVisible() ||
                Minimap.InTextInput() || Menu.IsVisible() || InventoryGui.IsVisible() || StoreGui.IsVisible())
            {
                return;
            }
            Open();
        }

        internal static void Open()
        {
            if (GUIManager.CustomGUIFront == null)
            {
                return;
            }
            if (s_instance == null)
            {
                s_instance = CreateWindow<HelpWindow>("AoJ_HelpWindow", Width, Height);
            }
            s_instance.ShowWindow();
            s_instance.ShowChapter(Mathf.Max(0, s_instance._chapter));
        }

        protected override void Build(Transform root)
        {
            Color beige = GUIManager.Instance.ValheimBeige;
            Color orange = GUIManager.Instance.ValheimOrange;
            Label(root, new Vector2(0f, Height / 2f - 34f), 26, orange, Width - 40f, 40f, TextAnchor.MiddleCenter).text =
                Localize("$aoj_help_title", PluginInfo.Version);

            float left = -Width / 2f + 30f + ChapterWidth / 2f;
            float top = Height / 2f - 90f;
            for (int i = 0; i < Chapters.Length; i++)
            {
                int index = i;
                Text label = Button(root, "$aoj_help_" + Chapters[i], new Vector2(left, top - i * (ChapterHeight + 5f)), ChapterWidth,
                    () => ShowChapter(index), ChapterHeight);
                _chapterLabels.Add(label);
            }

            float paneX = Width / 2f - 30f - PaneWidth / 2f;
            _title = Label(root, new Vector2(paneX, Height / 2f - 84f), 22, orange, PaneWidth, 36f, TextAnchor.MiddleLeft);

            _body = UiKit.TextArea(root, new Vector2(paneX, -15f), PaneWidth, 490f);

            Label(root, new Vector2(paneX, -Height / 2f + 42f), 13, beige, PaneWidth, 24f, TextAnchor.MiddleLeft).text = Localize("$aoj_help_scroll_hint");
            Button(root, "$aoj_btn_close", new Vector2(Width / 2f - 30f - 75f, -Height / 2f + 40f), 150f, Hide);
        }

        protected override bool IsTargetValid() => Player.m_localPlayer != null;

        protected override void Refresh()
        {
            // The text does not change while the window is open; refreshing it would reset the scroll.
        }

        private void ShowChapter(int index)
        {
            if (index < 0 || index >= Chapters.Length || _body == null)
            {
                return;
            }
            _chapter = index;
            string id = Chapters[index];
            SetText(_title, Localize("$aoj_help_" + id));
            string text = Localize("$aoj_help_" + id + "_text")
                .Replace("{wheel}", KeyName(AoJConfig.CommandWheelKey.Value))
                .Replace("{zone}", KeyName(AoJConfig.ZoneKey.Value))
                .Replace("{help}", KeyName(AoJConfig.HelpKey.Value))
                .Replace("{version}", PluginInfo.Version)
                .Replace("{config}", PluginInfo.Guid + ".cfg");
            _body.SetText(text);
            _body.Scroll.verticalNormalizedPosition = 1f;
            for (int i = 0; i < _chapterLabels.Count; i++)
            {
                _chapterLabels[i].color = i == index ? GUIManager.Instance.ValheimOrange : GUIManager.Instance.ValheimBeige;
            }
        }

        private static string KeyName(KeyCode key) => key == KeyCode.None ? "-" : key.ToString();
    }
}
