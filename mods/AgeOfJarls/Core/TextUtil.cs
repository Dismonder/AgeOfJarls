using System.Text;

namespace AgeOfJarls.Core
{
    internal static class TextUtil
    {
        /// <summary>Localizes tokens once the game's localization service is available.</summary>
        internal static string Localize(string text, params string[] words) =>
            Localization.instance != null ? Localization.instance.Localize(text ?? "", words) : text ?? "";

        /// <summary>
        /// For names typed by players and shown in other players' HUDs: no rich text, no localization tokens,
        /// no control characters, trimmed to a length limit.
        /// </summary>
        internal static string SanitizeName(string name, int maxLength)
        {
            var clean = new StringBuilder();
            foreach (char c in name ?? "")
            {
                if (c != '<' && c != '>' && c != '$' && !char.IsControl(c))
                {
                    clean.Append(c);
                }
            }
            string result = clean.ToString().Trim();
            return result.Length > maxLength ? result.Substring(0, maxLength).Trim() : result;
        }
    }
}
