using System;
using System.Collections.Generic;
using AgeOfJarls.Core;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace AgeOfJarls.UI
{
    /// <summary>The shared rich-text colours of settlement windows.</summary>
    internal static class Palette
    {
        internal const string Accent = "#e0c080";
        internal const string Warn = "#ff9060";
        internal const string Muted = "#b0b0b0";
        internal const string Danger = "#ff6040";
        internal const string Good = "#a0e080";
        internal const string Love = "#ff8fb0";
        internal static string Colored(string text, string hex) => "<color=" + hex + ">" + text + "</color>";
    }

    /// <summary>Small Unity UI factories; every rectangle lives inside the scaled window.</summary>
    internal static class UiKit
    {
        internal static ScrollList List(Transform parent, Vector2 position, float width, float height, float rowHeight) =>
            new ScrollList(parent, position, width, height, rowHeight);

        internal static TextArea TextArea(Transform parent, Vector2 position, float width, float height) =>
            new TextArea(parent, position, width, height);

        internal static RectTransform Rect(string name, Transform parent)
        {
            var obj = new GameObject(name, typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            return (RectTransform)obj.transform;
        }

        internal static ScrollRect Pane(Transform parent, Vector2 position, float width, float height, out RectTransform content)
        {
            RectTransform root = Rect("ScrollPane", parent);
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 0.5f);
            root.anchoredPosition = position;
            root.sizeDelta = new Vector2(width, height);
            ScrollRect scroll = root.gameObject.AddComponent<ScrollRect>();
            RectTransform view = Rect("Viewport", root);
            view.anchorMin = Vector2.zero;
            view.anchorMax = Vector2.one;
            view.offsetMin = Vector2.zero;
            view.offsetMax = new Vector2(-16f, 0f);
            view.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.25f);
            view.gameObject.AddComponent<RectMask2D>();
            content = Rect("Content", view);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;

            RectTransform bar = Rect("Scrollbar", root);
            bar.anchorMin = new Vector2(1f, 0f);
            bar.anchorMax = Vector2.one;
            bar.pivot = new Vector2(1f, 0.5f);
            bar.sizeDelta = new Vector2(10f, 0f);
            bar.gameObject.AddComponent<Image>();
            Scrollbar scrollbar = bar.gameObject.AddComponent<Scrollbar>();
            RectTransform handle = Rect("Handle", bar);
            handle.anchorMin = Vector2.zero;
            handle.anchorMax = Vector2.one;
            handle.offsetMin = handle.offsetMax = Vector2.zero;
            scrollbar.handleRect = handle;
            scrollbar.targetGraphic = handle.gameObject.AddComponent<Image>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            GUIManager.Instance.ApplyScrollbarStyle(scrollbar);
            scroll.content = content;
            scroll.viewport = view;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.inertia = true;
            scroll.scrollSensitivity = 30f;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            return scroll;
        }

        internal static Text MakeText(Transform parent, float width, int size, TextAnchor align)
        {
            GameObject obj = GUIManager.Instance.CreateText("", parent, Vector2.zero, Vector2.zero, Vector2.zero,
                GUIManager.Instance.AveriaSerifBold, size, GUIManager.Instance.ValheimBeige, true, Color.black, width, 30f, false);
            Text text = obj.GetComponent<Text>();
            text.alignment = align;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }
    }

    /// <summary>Pooled rows and cells. Callers keep their row cells, and rebind them rather than rebuilding objects.</summary>
    internal sealed class ScrollList
    {
        private readonly RectTransform _content;
        private readonly float _rowHeight;
        private readonly List<RectTransform> _rows = new List<RectTransform>();
        private readonly List<List<Component>> _cells = new List<List<Component>>();
        internal ScrollRect Scroll { get; }

        internal ScrollList(Transform parent, Vector2 position, float width, float height, float rowHeight)
        {
            _rowHeight = rowHeight;
            Scroll = UiKit.Pane(parent, position, width, height, out _content);
            VerticalLayoutGroup layout = _content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 4f;
            layout.padding = new RectOffset(4, 4, 4, 4);
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = layout.childControlHeight = true;
            _content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        internal RectTransform Row(int i)
        {
            while (_rows.Count <= i)
            {
                RectTransform row = UiKit.Rect("Row" + _rows.Count, _content);
                HorizontalLayoutGroup layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
                layout.spacing = 6f;
                layout.childAlignment = TextAnchor.MiddleLeft;
                layout.childForceExpandWidth = layout.childForceExpandHeight = false;
                layout.childControlWidth = layout.childControlHeight = true;
                row.gameObject.AddComponent<LayoutElement>().preferredHeight = _rowHeight;
                _rows.Add(row);
                _cells.Add(new List<Component>());
            }
            _rows[i].gameObject.SetActive(true);
            return _rows[i];
        }

        internal T Cell<T>(RectTransform row, int index) where T : Component => (T)_cells[_rows.IndexOf(row)][index];

        private void Add(RectTransform row, Component cell, float width, float height = -1f)
        {
            GameObject obj = cell is Text text && text.GetComponentInParent<Button>() != null
                ? text.GetComponentInParent<Button>().gameObject : cell.gameObject;
            LayoutElement layout = obj.AddComponent<LayoutElement>();
            layout.minWidth = layout.preferredWidth = width;
            if (height > 0f) layout.minHeight = layout.preferredHeight = height;
            _cells[_rows.IndexOf(row)].Add(cell);
        }

        internal Text Text(RectTransform row, float width, int size = 16, TextAnchor align = TextAnchor.MiddleLeft)
        {
            Text text = UiKit.MakeText(row, width, size, align);
            Add(row, text, width);
            return text;
        }

        internal Text Button(RectTransform row, string token, float width, Action onClick, float height = 30f)
        {
            GameObject obj = GUIManager.Instance.CreateButton(TextUtil.Localize(token), row, Vector2.zero, Vector2.zero, Vector2.zero, width, height);
            obj.GetComponent<Button>().onClick.AddListener(() => onClick());
            Text text = obj.GetComponentInChildren<Text>();
            text.fontSize = 14;
            Add(row, text, width, height);
            return text;
        }

        internal Image Icon(RectTransform row, float size)
        {
            Image icon = UiKit.Rect("Icon", row).gameObject.AddComponent<Image>();
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            Add(row, icon, size, size);
            return icon;
        }

        internal ConfirmButton Confirm(RectTransform row, string token, float width, Action onConfirm)
        {
            Text text = Button(row, token, width, () => { });
            Button button = text.GetComponentInParent<Button>();
            button.onClick.RemoveAllListeners();
            ConfirmButton confirm = button.gameObject.AddComponent<ConfirmButton>();
            confirm.Initialize(text, token, onConfirm);
            _cells[_rows.IndexOf(row)][_cells[_rows.IndexOf(row)].Count - 1] = confirm;
            return confirm;
        }

        internal void SetCount(int n)
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                _rows[i].gameObject.SetActive(i < n);
                if (i >= n) continue;
                float height = _rowHeight;
                foreach (Component cell in _cells[i])
                {
                    if (!(cell is Text text) || !text.gameObject.activeInHierarchy || text.GetComponentInParent<Button>() != null) continue;
                    float width = text.GetComponent<LayoutElement>().preferredWidth;
                    height = Mathf.Max(height, text.cachedTextGeneratorForLayout.GetPreferredHeight(text.text,
                        text.GetGenerationSettings(new Vector2(width, 0f))) / text.pixelsPerUnit + 4f);
                }
                LayoutElement element = _rows[i].GetComponent<LayoutElement>();
                if (element.preferredHeight != height) element.preferredHeight = height;
            }
        }
    }

    /// <summary>A long text pane that keeps its scroll position when the live description changes.</summary>
    internal sealed class TextArea
    {
        private readonly RectTransform _content;
        private readonly Text _text;
        private readonly float _height;
        internal ScrollRect Scroll { get; }

        internal TextArea(Transform parent, Vector2 position, float width, float height)
        {
            _height = height;
            Scroll = UiKit.Pane(parent, position, width, height, out _content);
            _text = UiKit.MakeText(_content, width - 32f, 16, TextAnchor.UpperLeft);
            RectTransform rect = _text.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -8f);
            rect.sizeDelta = new Vector2(-16f, height);
        }

        internal void SetText(string value)
        {
            if (_text.text == (value ?? "")) return;
            Vector2 position = Scroll.normalizedPosition;
            AojWindow.SetText(_text, value);
            float height = Mathf.Max(_height, _text.preferredHeight + 16f);
            _text.rectTransform.sizeDelta = new Vector2(-16f, height - 16f);
            _content.sizeDelta = new Vector2(0f, height);
            Scroll.normalizedPosition = position;
        }
    }

    /// <summary>Two clicks within three seconds; rebinding a pooled row cancels any armed action.</summary>
    internal sealed class ConfirmButton : MonoBehaviour
    {
        private Text _label;
        private string _token;
        private Action _action;
        private long _key;
        private float _until;
        private bool _required = true;

        internal void Initialize(Text label, string token, Action onConfirm)
        {
            _label = label;
            _action = onConfirm;
            Bind(token, 0L);
            GetComponent<Button>().onClick.AddListener(Click);
        }

        internal void Bind(string token, long key, bool required = true)
        {
            if (_key != key || _token != token || _required != required) _until = 0f;
            _key = key;
            _token = token;
            _required = required;
            UpdateLabel();
        }

        private void Click()
        {
            if (!_required || (_until > 0f && Time.unscaledTime < _until))
            {
                _until = 0f;
                UpdateLabel();
                _action?.Invoke();
            }
            else
            {
                _until = Time.unscaledTime + 3f;
                UpdateLabel();
            }
        }

        private void Update()
        {
            if (_until > 0f && Time.unscaledTime >= _until)
            {
                _until = 0f;
                UpdateLabel();
            }
        }

        private void OnDisable()
        {
            _until = 0f;
            UpdateLabel();
        }

        private void UpdateLabel() => AojWindow.SetText(_label, TextUtil.Localize(_until > 0f ? "$aoj_btn_sure" : _token));
    }
}
