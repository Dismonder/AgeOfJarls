using System;
using AgeOfJarls.Core;
using Jotunn.Managers;
using UnityEngine;

namespace AgeOfJarls.Family
{
    /// <summary>
    /// The hearts and the birth puff of vanilla breeding, borrowed from the Boar's Procreation (the effect prefabs are
    /// asset data, not names in code). Local VFX: whoever sees the moment plays them. Both calls are safe when the
    /// effects could not be found.
    /// </summary>
    internal static class LoveEffects
    {
        private const string Module = "Family";
        private const string SourcePrefab = "Boar";

        private static EffectList s_love;
        private static EffectList s_birth;
        private static bool s_retried;

        /// <summary>At PrefabManager.OnVanillaPrefabsAvailable (next to the settler prefab build).</summary>
        internal static void Capture()
        {
            try
            {
                GameObject boar = PrefabManager.Instance != null ? PrefabManager.Instance.GetPrefab(SourcePrefab) : null;
                Take(boar);
            }
            catch (Exception e)
            {
                Log.Warning(Module, $"Love effects not captured: {e.Message}");
            }
        }

        private static void Take(GameObject source)
        {
            Procreation procreation = source != null ? source.GetComponent<Procreation>() : null;
            if (procreation == null)
            {
                return;
            }
            s_love = procreation.m_loveEffects;
            s_birth = procreation.m_birthEffects;
            Log.Debug(Module, $"Love effects taken from {SourcePrefab}");
        }

        // The main-menu capture can miss (prefab not loaded yet): one more try from the live scene.
        private static void Retry()
        {
            if (s_retried || ZNetScene.instance == null)
            {
                return;
            }
            s_retried = true;
            try
            {
                Take(ZNetScene.instance.GetPrefab(SourcePrefab));
            }
            catch (Exception e)
            {
                Log.Warning(Module, $"Love effects not captured: {e.Message}");
            }
        }

        /// <summary>Hearts at this point (chest height of the settler is the caller's business).</summary>
        internal static void Hearts(Vector3 position)
        {
            if (s_love == null)
            {
                Retry();
            }
            Play(s_love, position);
        }

        /// <summary>The birth puff at this point.</summary>
        internal static void Birth(Vector3 position)
        {
            if (s_birth == null)
            {
                Retry();
            }
            Play(s_birth, position);
        }

        private static void Play(EffectList effects, Vector3 position)
        {
            if (effects == null || !effects.HasEffects())
            {
                return;
            }
            try
            {
                effects.Create(position, Quaternion.identity);
            }
            catch (Exception e)
            {
                Log.Warning(Module, $"Effect failed: {e.Message}");
            }
        }
    }
}
