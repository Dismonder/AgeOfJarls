using System.Linq;
using AgeOfJarls.Core;
using UnityEngine;

namespace AgeOfJarls.Settlers
{
    /// <summary>
    /// Skin and hair color ranges of the vanilla character creator, read once in the main menu so settlers look like
    /// players. The fallback values are only an approximation, used if the creator cannot be found.
    /// </summary>
    internal static class AppearancePalette
    {
        private const string Module = "Settlers";

        internal static Color SkinColor0 = new Color(1f, 1f, 1f);
        internal static Color SkinColor1 = new Color(0.33f, 0.24f, 0.19f);
        internal static Color HairColor0 = new Color(1f, 0.86f, 0.62f);
        internal static Color HairColor1 = new Color(0.25f, 0.16f, 0.11f);
        internal static float HairMinLevel = 0.1f;
        internal static float HairMaxLevel = 1f;

        internal static void Capture()
        {
            // One-off search at menu load, never per frame.
            PlayerCustomizaton creator = Resources.FindObjectsOfTypeAll<PlayerCustomizaton>().FirstOrDefault();
            if (creator == null)
            {
                Log.Warning(Module, "Character creator not found, settlers use approximate colors");
                return;
            }

            SkinColor0 = creator.m_skinColor0;
            SkinColor1 = creator.m_skinColor1;
            HairColor0 = creator.m_hairColor0;
            HairColor1 = creator.m_hairColor1;
            HairMinLevel = creator.m_hairMinLevel;
            HairMaxLevel = creator.m_hairMaxLevel;
            Log.Debug(Module, "Captured the character creator palette");
        }
    }
}
