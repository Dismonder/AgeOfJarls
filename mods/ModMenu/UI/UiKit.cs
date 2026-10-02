using System;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace ModMenu.UI
{
    /// <summary>Small helpers over Jotunn's Valheim-styled controls; positions are from the parent's top-left corner.</summary>
    internal static class UiKit
    {
        public static GUIManager Gui => GUIManager.Instance;

        public static RectTransform Place(GameObject go, float x, float y, float width, float height)
        {
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        /// <summary>Anchored to the parent's right edge: <paramref name="right"/> is the gap to that edge, centred vertically.</summary>
        public static RectTransform PlaceRight(GameObject go, float right, float width, float height)
        {
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = new Vector2(-right, 0f);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        public static RectTransform PlaceLeft(GameObject go, float left, float width, float height, float y = 0f)
        {
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(left, y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        public static void Stretch(GameObject go)
        {
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        public static Text Label(Transform parent, string text, int size, Color color, bool bold = true, TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            GameObject go = Gui.CreateText(text, parent, Vector2.zero, Vector2.zero, Vector2.zero,
                bold ? Gui.AveriaSerifBold : Gui.AveriaSerif, size, color, true, Color.black, 100f, 30f, false);
            Text label = go.GetComponent<Text>();
            label.alignment = alignment;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.raycastTarget = false;
            return label;
        }

        public static Button Button(Transform parent, string text, Action onClick, int fontSize = 16)
        {
            GameObject go = Gui.CreateButton(text, parent, Vector2.zero, Vector2.zero, Vector2.zero);
            var button = go.GetComponent<Button>();
            Text label = go.GetComponentInChildren<Text>();
            label.fontSize = fontSize;
            Stretch(label.gameObject);
            button.onClick.AddListener(() => onClick());
            return button;
        }

        /// <summary>Jotunn tints only the button image; the label must fade too, or a disabled button looks clickable.</summary>
        public static void SetEnabled(Button button, bool enabled)
        {
            button.interactable = enabled;
            Text label = button.GetComponentInChildren<Text>(true);
            if (label != null)
            {
                Color color = label.color;
                color.a = enabled ? 1f : 0.35f;
                label.color = color;
            }
        }

        public static InputField Input(Transform parent, string placeholder, int fontSize = 16)
        {
            GameObject go = Gui.CreateInputField(parent, Vector2.zero, Vector2.zero, Vector2.zero, InputField.ContentType.Standard, placeholder, fontSize);
            var field = go.GetComponent<InputField>();
            foreach (Graphic graphic in new[] { field.textComponent, field.placeholder })
            {
                var rect = graphic.rectTransform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = new Vector2(10f, 4f);
                rect.offsetMax = new Vector2(-10f, -4f);
                if (graphic is Text text)
                {
                    text.alignment = TextAnchor.MiddleLeft;
                }
            }
            return field;
        }

        public static Toggle Toggle(Transform parent, bool value, Action<bool> onChange, float size = 28f)
        {
            GameObject go = Gui.CreateToggle(parent, size, size);
            go.transform.localScale = Vector3.one;
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            var toggle = go.GetComponent<Toggle>();
            toggle.SetIsOnWithoutNotify(value);
            toggle.onValueChanged.AddListener(v => onChange(v));
            Transform label = go.transform.Find("Label");
            if (label != null)
            {
                UnityEngine.Object.Destroy(label.gameObject);
            }
            return toggle;
        }

        public static Dropdown Dropdown(Transform parent, string[] options, int value, Action<int> onChange)
        {
            GameObject go = Gui.CreateDropDown(parent, Vector2.zero, Vector2.zero, Vector2.zero, 16, 300f, 32f);
            var dropdown = go.GetComponent<Dropdown>();
            dropdown.AddOptions(new System.Collections.Generic.List<string>(options));
            dropdown.SetValueWithoutNotify(value);
            dropdown.onValueChanged.AddListener(v => onChange(v));
            return dropdown;
        }

        public static Slider Slider(Transform parent, float min, float max, float value, bool whole, Action<float> onChange)
        {
            GameObject go = DefaultControls.CreateSlider(Gui.ValheimControlResources);
            go.transform.SetParent(parent, false);
            var slider = go.GetComponent<Slider>();
            Gui.ApplySliderStyle(slider, new Vector2(16f, 24f));
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = whole;
            slider.SetValueWithoutNotify(value);
            slider.onValueChanged.AddListener(v => onChange(v));
            return slider;
        }

        /// <summary>A vertical scroll list filling the parent minus the given margins; returns the content to add rows to.</summary>
        public static RectTransform ScrollList(Transform parent, float left, float top, float right, float bottom)
        {
            GameObject view = DefaultControls.CreateScrollView(Gui.ValheimControlResources);
            view.name = "ModMenuList";
            view.transform.SetParent(parent, false);
            var rect = (RectTransform)view.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);

            var scroll = view.GetComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            if (scroll.horizontalScrollbar != null)
            {
                UnityEngine.Object.Destroy(scroll.horizontalScrollbar.gameObject);
                scroll.horizontalScrollbar = null;
            }
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            Gui.ApplyScrollRectStyle(scroll);

            RectTransform content = scroll.content;
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = content.offsetMax = Vector2.zero;
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(6, 6, 6, 6);
            layout.spacing = 4f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return content;
        }

        /// <summary>A list row of fixed height with a faint background; children are placed with Place*/Stretch.</summary>
        public static GameObject Row(Transform content, float height, bool shaded)
        {
            var row = new GameObject("Row", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            row.transform.SetParent(content, false);
            row.GetComponent<LayoutElement>().preferredHeight = height;
            row.GetComponent<LayoutElement>().minHeight = height;
            var image = row.GetComponent<Image>();
            image.color = shaded ? new Color(1f, 1f, 1f, 0.05f) : new Color(0f, 0f, 0f, 0f);
            image.raycastTarget = false;
            return row;
        }

        public static void Clear(Transform content)
        {
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                GameObject child = content.GetChild(i).gameObject;
                child.SetActive(false);
                UnityEngine.Object.Destroy(child);
            }
        }
    }
}
