using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AgeOfJarls.Core;
using UnityEngine;

namespace AgeOfJarls.Commands
{
    /// <summary>
    /// <c>aoj_dump &lt;prefab&gt;</c>: the hierarchy of a registered prefab - transforms, components, meshes with their
    /// bounds, colliders and the placement flags of its Piece - written to the BepInEx log. For building models out
    /// of vanilla parts; read only, so not a cheat.
    /// </summary>
    internal sealed class DumpCommand : AojCommand
    {
        private const int MaxDepth = 6;

        public override string Name => "aoj_dump";

        public override string Help => "Age of Jarls: write a prefab's hierarchy, meshes, colliders and piece flags to the BepInEx log";

        public override void Run(string[] args, Terminal context)
        {
            GameObject prefab = args.Length > 0 && ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(args[0]) : null;
            if (prefab == null)
            {
                context?.AddString(args.Length == 0 ? "usage: aoj_dump <prefab>" : $"No prefab '{args[0]}'.");
                return;
            }
            var lines = new List<string> { $"== {prefab.name}" };
            Describe(prefab.transform, 0, lines);
            if (prefab.GetComponent<Piece>() is Piece piece)
            {
                lines.Add($"   Piece: notOnFloor={piece.m_notOnFloor} notOnWood={piece.m_notOnWood} groundPiece={piece.m_groundPiece} groundOnly={piece.m_groundOnly} " +
                          $"notOnTilting={piece.m_notOnTiltingSurface} inCeilingOnly={piece.m_inCeilingOnly} clipGround={piece.m_clipGround} " +
                          $"clipEverything={piece.m_clipEverything} noClipping={piece.m_noClipping} category={piece.m_category} " +
                          FormattableString.Invariant($"spaceRequirement={piece.m_spaceRequirement:0.##}"));
            }
            if (prefab.GetComponent<WearNTear>() is WearNTear wear)
            {
                lines.Add(FormattableString.Invariant(
                    $"   WearNTear: supports={wear.m_supports} noSupportWear={wear.m_noSupportWear} material={wear.m_materialType} health={wear.m_health}"));
            }
            foreach (string line in lines)
            {
                Log.Info("Dump", line);
            }
            context?.AddString($"{prefab.name}: {lines.Count} line(s) written to the BepInEx log.");
        }

        private static void Describe(Transform node, int depth, List<string> lines)
        {
            string indent = new string(' ', depth * 2);
            Vector3 p = node.localPosition;
            Vector3 r = node.localEulerAngles;
            Vector3 s = node.localScale;
            string components = string.Join(", ", node.GetComponents<Component>().Where(c => c != null && !(c is Transform)).Select(c => c.GetType().Name));
            lines.Add($"{indent}{node.name} [{LayerMask.LayerToName(node.gameObject.layer)}{(node.gameObject.activeSelf ? "" : ", inactive")}] " +
                      FormattableString.Invariant($"pos {p.x:0.###},{p.y:0.###},{p.z:0.###} rot {r.x:0.#},{r.y:0.#},{r.z:0.#} scale {s.x:0.###},{s.y:0.###},{s.z:0.###}") +
                      $" | {components}");

            if (node.GetComponent<MeshFilter>() is MeshFilter filter && filter.sharedMesh != null)
            {
                lines.Add(indent + "  mesh " + filter.sharedMesh.name + " " + Bounds(filter.sharedMesh.bounds));
            }
            if (node.GetComponent<SkinnedMeshRenderer>() is SkinnedMeshRenderer skinned && skinned.sharedMesh != null)
            {
                lines.Add(indent + "  skinned " + skinned.sharedMesh.name + " " + Bounds(skinned.sharedMesh.bounds));
            }
            if (node.GetComponent<Renderer>() is Renderer renderer)
            {
                lines.Add(indent + "  materials " + string.Join(", ", renderer.sharedMaterials.Select(m => m != null ? m.name + "/" + m.shader.name : "-")));
            }
            foreach (Collider collider in node.GetComponents<Collider>())
            {
                lines.Add(indent + "  collider " + ColliderText(collider));
            }
            if (depth >= MaxDepth)
            {
                return;
            }
            foreach (Transform child in node)
            {
                Describe(child, depth + 1, lines);
            }
        }

        private static string Bounds(Bounds b) =>
            FormattableString.Invariant($"center {b.center.x:0.###},{b.center.y:0.###},{b.center.z:0.###} size {b.size.x:0.###},{b.size.y:0.###},{b.size.z:0.###}");

        private static string ColliderText(Collider collider)
        {
            var text = new StringBuilder(collider.GetType().Name);
            if (collider.isTrigger)
            {
                text.Append(" (trigger)");
            }
            switch (collider)
            {
                case BoxCollider box:
                    text.Append(FormattableString.Invariant($" center {box.center.x:0.###},{box.center.y:0.###},{box.center.z:0.###} size {box.size.x:0.###},{box.size.y:0.###},{box.size.z:0.###}"));
                    break;
                case CapsuleCollider capsule:
                    text.Append(FormattableString.Invariant($" center {capsule.center.x:0.###},{capsule.center.y:0.###},{capsule.center.z:0.###} r {capsule.radius:0.###} h {capsule.height:0.###} dir {capsule.direction}"));
                    break;
                case MeshCollider mesh:
                    text.Append(" mesh " + (mesh.sharedMesh != null ? mesh.sharedMesh.name + " " + Bounds(mesh.sharedMesh.bounds) : "-"));
                    break;
            }
            return text.ToString();
        }
    }
}
