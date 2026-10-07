using System;
using System.Collections.Generic;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace AgeOfJarls.UI
{
    /// <summary>
    /// Shared behaviour of the mod's windows: a wooden Jotunn panel in CustomGUIFront (rebuilt on every scene change,
    /// so windows are created lazily), blocked player input while open, closing with Esc / B / Use, and a periodic
    /// refresh that only runs while the window is visible.
    /// </summary>
    internal abstract class AojWindow : MonoBehaviour
    {
        protected const float ButtonHeight = 40f;
        protected static readonly Vector2 Center = new Vector2(0.5f, 0.5f);
        private const float RefreshSeconds = 0.5f;

        private bool _blockingInput;
        private float _refreshTimer;
        private float _windowWidth;

        /// <summary>While this field is being typed in, the Use key does not close the window.</summary>
        protected InputField TextEntry;

        protected static T CreateWindow<T>(string name, float width, float height) where T : AojWindow
        {
            GameObject panel = GUIManager.Instance.CreateWoodpanel(GUIManager.CustomGUIFront.transform, Center, Center, Vector2.zero, width, height, false);
            panel.name = name;
            // Bigger windows and text (UI/Scale), scaled as a whole so every layout keeps its proportions; never past
            // the edge of the screen (the canvas is in reference units, like the window).
            float scale = Core.AoJConfig.UiScale.Value;
            RectTransform canvas = GUIManager.CustomGUIFront.GetComponent<RectTransform>();
            if (canvas != null && canvas.rect.width > 0f && canvas.rect.height > 0f)
            {
                scale = Mathf.Min(scale, (canvas.rect.width - ScreenMargin) / width, (canvas.rect.height - ScreenMargin) / height);
            }
            panel.GetComponent<RectTransform>().localScale = Vector3.one * Mathf.Max(0.5f, scale);
            T window = panel.AddComponent<T>();
            window._windowWidth = width;
            window.Build(panel.transform);
            panel.SetActive(false);
            return window;
        }

        private const float ScreenMargin = 40f;

        protected abstract void Build(Transform root);

        /// <summary>False closes the window (target gone, player too far, dead...).</summary>
        protected abstract bool IsTargetValid();

        protected abstract void Refresh();

        protected virtual void OnHidden()
        {
        }

        protected void ShowWindow()
        {
            gameObject.SetActive(true);
            if (!_blockingInput)
            {
                GUIManager.BlockInput(true);
                _blockingInput = true;
            }
            RefreshNow();
        }

        internal void Hide()
        {
            gameObject.SetActive(false);
            ReleaseInput();
            OnHidden();
        }

        protected void RefreshNow()
        {
            _refreshTimer = RefreshSeconds;
            long perf = Core.Perf.Start();
            Refresh();
            Core.Perf.Stop(Core.Perf.Section.Windows, perf);
        }

        // Unity only calls this while the panel is active.
        protected void Update()
        {
            if (!IsTargetValid())
            {
                Hide();
                return;
            }
            bool typing = TextEntry != null && TextEntry.isFocused;
            if (_tabs.Count > 0 && !typing)
            {
                bool mouse = ZInput.IsGamepadMouseActive();
                if (ZInput.GetButtonDown(mouse ? "JoyLBumper" : "JoyTabLeft")) ShowTab((ActiveTab + _tabs.Count - 1) % _tabs.Count);
                if (ZInput.GetButtonDown(mouse ? "JoyRBumper" : "JoyTabRight")) ShowTab((ActiveTab + 1) % _tabs.Count);
            }
            if (ZInput.GetKeyDown(KeyCode.Escape) || ZInput.GetButtonDown("JoyButtonB") || (!typing && ZInput.GetButtonDown("Use")))
            {
                Hide();
                return;
            }

            _refreshTimer -= Time.deltaTime;
            if (_refreshTimer <= 0f)
            {
                RefreshNow();
            }
        }

        protected void OnDestroy()
        {
            // Scene change while open: never leave the player's controls blocked.
            ReleaseInput();
        }

        private void ReleaseInput()
        {
            if (_blockingInput)
            {
                GUIManager.BlockInput(false);
                _blockingInput = false;
            }
        }

        protected static Text Label(Transform parent, Vector2 position, int size, Color color, float width, float height, TextAnchor alignment)
        {
            GameObject label = GUIManager.Instance.CreateText("", parent, Center, Center, position,
                GUIManager.Instance.AveriaSerifBold, size, color, true, Color.black, width, height, false);
            Text text = label.GetComponent<Text>();
            text.alignment = alignment;
            return text;
        }

        /// <summary>Returns the button's label, for buttons whose text changes.</summary>
        protected static Text Button(Transform parent, string token, Vector2 position, float width, Action onClick, float height = ButtonHeight)
        {
            GameObject button = GUIManager.Instance.CreateButton(Localize(token), parent, Center, Center, position, width, height);
            button.GetComponent<Button>().onClick.AddListener(() => onClick());
            return button.GetComponentInChildren<Text>();
        }

        /// <summary>An empty, full-size container for one tab's controls.</summary>
        protected static Transform Page(Transform root, string name)
        {
            var page = new GameObject(name, typeof(RectTransform));
            page.transform.SetParent(root, false);
            return page.transform;
        }

        // ---------------------------------------------------------------- tabs

        private readonly List<(Text label, GameObject page)> _tabs = new List<(Text, GameObject)>();

        protected int ActiveTab { get; private set; }

        /// <summary>A row of tab buttons along the top; each shows its own page and hides the others.</summary>
        protected void Tabs(Transform root, float y, float tabWidth, params (string token, Transform page)[] tabs)
        {
            if (tabWidth == 0f) tabWidth = (_windowWidth - 40f - 8f * (tabs.Length - 1)) / tabs.Length;
            float left = -(tabs.Length - 1) * (tabWidth + 8f) / 2f;
            for (int i = 0; i < tabs.Length; i++)
            {
                int index = i;
                Text label = Button(root, tabs[i].token, new Vector2(left + i * (tabWidth + 8f), y), tabWidth, () => ShowTab(index), TabHeight);
                if (tabs.Length >= 8) label.fontSize = 14;
                _tabs.Add((label, tabs[i].page.gameObject));
            }
            ShowTab(0);
        }

        protected void ShowTab(int index)
        {
            ActiveTab = index;
            for (int i = 0; i < _tabs.Count; i++)
            {
                _tabs[i].page.SetActive(i == index);
                _tabs[i].label.color = i == index ? GUIManager.Instance.ValheimOrange : GUIManager.Instance.ValheimBeige;
            }
            if (gameObject.activeSelf && IsTargetValid())
            {
                RefreshNow();
            }
        }

        protected const float TabHeight = 34f;
        protected const float RowButtonHeight = 30f;

        protected static InputField TextInput(Transform parent, string placeholderToken, Vector2 position, float width)
        {
            GameObject input = GUIManager.Instance.CreateInputField(parent, Center, Center, position,
                InputField.ContentType.Standard, Localize(placeholderToken), 16, width, ButtonHeight);
            return input.GetComponent<InputField>();
        }

        protected static string Localize(string text, params string[] words) =>
            Core.TextUtil.Localize(text, words);

        /// <summary>Enter submits; losing focus alone must not rename anything.</summary>
        protected static InputField TextInput(Transform parent, string placeholderToken, Vector2 position, float width, Action onSubmit)
        {
            InputField input = TextInput(parent, placeholderToken, position, width);
            input.onEndEdit.AddListener(_ =>
            {
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) onSubmit();
            });
            return input;
        }

        /// <summary>Avoid rebuilding Unity's text mesh when a periodic refresh has no changes.</summary>
        internal static void SetText(Text text, string value)
        {
            if (text != null && text.text != (value ?? "")) text.text = value ?? "";
        }
    }
}
