using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace ModMenu.UI
{
    /// <summary>
    /// One row per config entry with an editor that fits its type: toggle (bool), dropdown (enum, list of allowed
    /// values), slider + field (number with a range), text field (anything BepInEx can convert from text, e.g.
    /// KeyboardShortcut, Color). Values are written through ConfigEntryBase.BoxedValue, so the mod gets its usual
    /// SettingChanged event and the .cfg file is saved at once.
    /// Honours the common ConfigurationManagerAttributes tags (Browsable, ReadOnly, Order, DispName).
    /// </summary>
    internal static class ConfigEditors
    {
        private const float EditorWidth = 300f;
        private const float ResetWidth = 100f;
        private const int MaxDropdownOptions = 60;

        public static List<ConfigEntryBase> Visible(ConfigFile config)
        {
            var entries = new List<ConfigEntryBase>();
            foreach (ConfigDefinition key in config.Keys.ToList())
            {
                ConfigEntryBase entry = config[key];
                if (entry != null && Attribute<bool?>(entry, "Browsable") != false)
                {
                    entries.Add(entry);
                }
            }
            return entries
                .OrderBy(e => e.Definition.Section, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(e => Attribute<int?>(e, "Order") ?? 0)
                .ThenBy(e => e.Definition.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static void Row(Transform content, ConfigEntryBase entry, float rowWidth, Action onReset)
        {
            GUIManager gui = GUIManager.Instance;
            float textWidth = rowWidth - EditorWidth - ResetWidth - 50f;

            GameObject row = UiKit.Row(content, 50f, true);
            Text name = UiKit.Label(row.transform, Attribute<string>(entry, "DispName") ?? entry.Definition.Key, 17, gui.ValheimBeige);
            UiKit.Place(name.gameObject, 12f, 8f, textWidth, 24f);

            float height = 50f;
            string description = entry.Description?.Description;
            if (!string.IsNullOrEmpty(description))
            {
                Text text = UiKit.Label(row.transform, description.Trim(), 14, new Color(0.72f, 0.68f, 0.6f), false, TextAnchor.UpperLeft);
                text.verticalOverflow = VerticalWrapMode.Overflow;
                UiKit.Place(text.gameObject, 12f, 32f, textWidth, 20f);
                float textHeight = Mathf.Ceil(text.preferredHeight) + 2f;
                text.rectTransform.sizeDelta = new Vector2(textWidth, textHeight);
                height = Mathf.Max(height, 32f + textHeight + 8f);
            }
            row.GetComponent<LayoutElement>().preferredHeight = height;
            row.GetComponent<LayoutElement>().minHeight = height;

            var editor = new GameObject("Editor", typeof(RectTransform));
            editor.transform.SetParent(row.transform, false);
            UiKit.PlaceRight(editor, ResetWidth + 20f, EditorWidth, 34f);
            bool readOnly = Attribute<bool?>(entry, "ReadOnly") == true;

            Button reset = UiKit.Button(row.transform, T.Get("reset"), () =>
            {
                Set(entry, entry.DefaultValue);
                onReset();
            }, 14);
            UiKit.PlaceRight(reset.gameObject, 10f, ResetWidth, 32f);
            Action changed = () => UiKit.SetEnabled(reset, !readOnly && !Equals(entry.BoxedValue, entry.DefaultValue));
            changed();

            BuildEditor(editor.transform, entry, readOnly, value =>
            {
                Set(entry, value);
                changed();
            });
        }

        private static void BuildEditor(Transform parent, ConfigEntryBase entry, bool readOnly, Action<object> set)
        {
            Type type = entry.SettingType;
            AcceptableValueBase acceptable = entry.Description?.AcceptableValues;
            Type acceptableType = acceptable?.GetType();
            bool isList = acceptableType != null && acceptableType.IsGenericType && acceptableType.GetGenericTypeDefinition() == typeof(AcceptableValueList<>);
            bool isRange = acceptableType != null && acceptableType.IsGenericType && acceptableType.GetGenericTypeDefinition() == typeof(AcceptableValueRange<>);

            if (type == typeof(bool))
            {
                Toggle toggle = UiKit.Toggle(parent, (bool)entry.BoxedValue, v => set(v));
                UiKit.PlaceLeft(toggle.gameObject, 0f, 28f, 28f);
                toggle.interactable = !readOnly;
                return;
            }

            // Huge enums (KeyCode has ~500 values) are typed in instead: a dropdown that long is unusable and slow.
            bool smallEnum = type.IsEnum && !type.IsDefined(typeof(FlagsAttribute), false) && Enum.GetValues(type).Length <= MaxDropdownOptions;
            if (isList || smallEnum)
            {
                object[] values = isList
                    ? ((Array)acceptableType.GetProperty("AcceptableValues").GetValue(acceptable, null)).Cast<object>().ToArray()
                    : Enum.GetValues(type).Cast<object>().ToArray();
                string[] labels = values.Select(v => ToText(v, type)).ToArray();
                int current = Array.FindIndex(values, v => Equals(v, entry.BoxedValue));
                Dropdown dropdown = UiKit.Dropdown(parent, labels, Math.Max(0, current), i => set(values[i]));
                UiKit.Stretch(dropdown.gameObject);
                dropdown.interactable = !readOnly;
                return;
            }

            if (!TomlTypeConverter.CanConvert(type))
            {
                Text text = UiKit.Label(parent, entry.BoxedValue?.ToString() ?? "", 15, GUIManager.Instance.ValheimBeige, false);
                UiKit.Stretch(text.gameObject);
                return;
            }

            InputField field = UiKit.Input(parent, "");
            field.SetTextWithoutNotify(ToText(entry.BoxedValue, type));
            field.interactable = !readOnly;

            if (isRange && IsNumber(type))
            {
                float min = Convert.ToSingle(acceptableType.GetProperty("MinValue").GetValue(acceptable, null));
                float max = Convert.ToSingle(acceptableType.GetProperty("MaxValue").GetValue(acceptable, null));
                bool whole = type != typeof(float) && type != typeof(double) && type != typeof(decimal);
                Slider slider = UiKit.Slider(parent, min, max, Convert.ToSingle(entry.BoxedValue), whole, v =>
                {
                    // Rounded in double, so a double setting gets 0.1 and not 0.100000001.
                    set(Convert.ChangeType(whole ? Math.Round((double)v) : Math.Round((double)v, 3), type));
                    field.SetTextWithoutNotify(ToText(entry.BoxedValue, type));
                });
                UiKit.PlaceLeft(slider.gameObject, 0f, EditorWidth - 100f, 24f);
                slider.interactable = !readOnly;
                UiKit.PlaceRight(field.gameObject, 0f, 90f, 34f);
                field.onEndEdit.AddListener(text =>
                {
                    if (TryParse(text, type, out object value))
                    {
                        set(value);
                    }
                    field.SetTextWithoutNotify(ToText(entry.BoxedValue, type));
                    slider.SetValueWithoutNotify(Convert.ToSingle(entry.BoxedValue));
                });
                return;
            }

            UiKit.Stretch(field.gameObject);
            field.onEndEdit.AddListener(text =>
            {
                if (TryParse(text, type, out object value))
                {
                    set(value);
                }
                // Shows the stored value: the parsed one, clamped one, or the old one after a typo.
                field.SetTextWithoutNotify(ToText(entry.BoxedValue, type));
            });
        }

        private static void Set(ConfigEntryBase entry, object value)
        {
            try
            {
                entry.BoxedValue = value;
            }
            catch (Exception e)
            {
                // The owning mod's SettingChanged handler threw; the value is stored anyway.
                Plugin.Log.LogWarning($"Setting {entry.Definition} raised an error in its mod: {e.Message}");
            }
        }

        private static bool TryParse(string text, Type type, out object value)
        {
            try
            {
                value = TomlTypeConverter.ConvertToValue(text, type);
                return true;
            }
            catch (Exception)
            {
                value = null;
                return false;
            }
        }

        private static string ToText(object value, Type type)
        {
            try
            {
                return TomlTypeConverter.ConvertToString(value, type);
            }
            catch (Exception)
            {
                return value?.ToString() ?? "";
            }
        }

        private static bool IsNumber(Type type) =>
            type == typeof(int) || type == typeof(float) || type == typeof(double) || type == typeof(long) || type == typeof(short)
            || type == typeof(byte) || type == typeof(uint) || type == typeof(ulong) || type == typeof(ushort) || type == typeof(sbyte) || type == typeof(decimal);

        /// <summary>A field of the ConfigurationManagerAttributes object a mod may put in the entry's tags (each mod ships its own copy).</summary>
        private static TValue Attribute<TValue>(ConfigEntryBase entry, string field)
        {
            object[] tags = entry.Description?.Tags;
            if (tags == null)
            {
                return default;
            }
            foreach (object tag in tags)
            {
                if (tag == null || tag.GetType().Name != "ConfigurationManagerAttributes")
                {
                    continue;
                }
                object raw = tag.GetType().GetField(field, BindingFlags.Public | BindingFlags.Instance)?.GetValue(tag)
                    ?? tag.GetType().GetProperty(field, BindingFlags.Public | BindingFlags.Instance)?.GetValue(tag, null);
                if (raw is TValue value)
                {
                    return value;
                }
            }
            return default;
        }
    }
}
