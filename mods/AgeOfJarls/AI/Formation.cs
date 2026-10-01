using System;
using UnityEngine;

namespace AgeOfJarls.AI
{
    /// <summary>
    /// The geometry of settlers moving among others, apart from the game so the tests can hold it: where the n-th
    /// guard walks behind a player, how a walker turns past somebody in its way, where a settler steps to let a player
    /// through, and whether somebody stands in an archer's line of fire. Plain vector arithmetic only (no engine
    /// calls), so it runs under the test runner.
    /// </summary>
    internal static class Formation
    {
        /// <summary>The first pair of guards walks this far behind the player, each pair after them this much farther.</summary>
        internal const float BehindDistance = 2.5f;
        internal const float RowGap = 2f;
        /// <summary>To the side of the player; every second pair a little farther out, so nobody walks in another's back.</summary>
        internal const float SideDistance = 2f;
        internal const float OuterRowExtra = 1f;

        /// <summary>
        /// The n-th follower's place behind its leader: pairs of rows, right then left (slot 0 right, 1 left, 2 right
        /// a row back, ...), in the leader's own frame: how far behind, and how far to the right (negative = left).
        /// </summary>
        internal static void EscortSlot(int slot, out float behind, out float right)
        {
            slot = Math.Max(0, slot);
            int row = slot / 2;
            float side = slot % 2 == 0 ? 1f : -1f;
            behind = BehindDistance + row * RowGap;
            right = side * (SideDistance + (row % 2 == 1 ? OuterRowExtra : 0f));
        }

        /// <summary>Somebody ahead within <paramref name="reach"/>, inside the cone the walker moves into.</summary>
        internal static bool Blocks(Vector3 direction, Vector3 toOther, float reach, float coneCos)
        {
            float sqr = toOther.sqrMagnitude;
            if (sqr < 0.0001f || sqr > reach * reach || direction.sqrMagnitude < 0.0001f)
            {
                return false;
            }
            return Vector3.Dot(direction.normalized, toOther / Mathf.Sqrt(sqr)) >= coneCos;
        }

        /// <summary>The way on, turned by <paramref name="degrees"/> away from the side the blocker is on (flat).</summary>
        internal static Vector3 SteerAround(Vector3 direction, Vector3 toBlocker, float degrees)
        {
            // Both on the walker's right: it turns left (a negative yaw), and the other way round. Two walkers meeting
            // head-on each see the other on their own right, and both turn left - past each other.
            float side = Vector3.Cross(direction, toBlocker).y >= 0f ? 1f : -1f;
            return Rotate(direction, -side * degrees);
        }

        /// <summary>
        /// Where a settler steps to let a walking player through: out of the player's line of walking, to the side it
        /// already stands on, <paramref name="distance"/> from where it is.
        /// </summary>
        internal static Vector3 StepAside(Vector3 me, Vector3 player, Vector3 playerVelocity, float distance)
        {
            Vector3 along = new Vector3(playerVelocity.x, 0f, playerVelocity.z);
            Vector3 offset = new Vector3(me.x - player.x, 0f, me.z - player.z);
            if (along.sqrMagnitude < 0.0001f)
            {
                along = offset;
            }
            along.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, along);
            float side = Vector3.Cross(along, offset).y >= 0f ? 1f : -1f;
            return me + right * (side * distance);
        }

        /// <summary>A player walking at the settler: towards it, fast enough to mean it, and close.</summary>
        internal static bool WalksInto(Vector3 me, Vector3 player, Vector3 playerVelocity, float range, float minSpeed, float approachCos)
        {
            Vector3 offset = new Vector3(me.x - player.x, 0f, me.z - player.z);
            Vector3 along = new Vector3(playerVelocity.x, 0f, playerVelocity.z);
            float distance = offset.magnitude;
            float speed = along.magnitude;
            if (distance < 0.01f || distance > range || speed < minSpeed)
            {
                return false;
            }
            return Vector3.Dot(along / speed, offset / distance) >= approachCos;
        }

        /// <summary>Somebody closer than the target standing within <paramref name="width"/> of the line from the shooter to it (flat).</summary>
        internal static bool InLineOfFire(Vector3 shooter, Vector3 target, Vector3 other, float width)
        {
            Vector3 line = new Vector3(target.x - shooter.x, 0f, target.z - shooter.z);
            Vector3 toOther = new Vector3(other.x - shooter.x, 0f, other.z - shooter.z);
            float length = line.magnitude;
            if (length < 0.01f)
            {
                return false;
            }
            Vector3 along = line / length;
            float ahead = Vector3.Dot(toOther, along);
            if (ahead <= 0.3f || ahead >= length - 0.3f)
            {
                return false;
            }
            Vector3 lateral = toOther - along * ahead;
            return lateral.magnitude <= width;
        }

        /// <summary>The side of the line of fire the other stands on: step the other way (+1 = step right).</summary>
        internal static float SidestepSide(Vector3 shooter, Vector3 target, Vector3 other)
        {
            Vector3 line = new Vector3(target.x - shooter.x, 0f, target.z - shooter.z);
            Vector3 toOther = new Vector3(other.x - shooter.x, 0f, other.z - shooter.z);
            return Vector3.Cross(line, toOther).y >= 0f ? -1f : 1f;
        }

        /// <summary>A flat vector turned by <paramref name="degrees"/> about the up axis (positive = clockwise seen from above, like Unity's yaw).</summary>
        internal static Vector3 Rotate(Vector3 flat, float degrees)
        {
            double radians = degrees * Math.PI / 180.0;
            float sin = (float)Math.Sin(radians);
            float cos = (float)Math.Cos(radians);
            return new Vector3(flat.x * cos + flat.z * sin, 0f, -flat.x * sin + flat.z * cos);
        }
    }
}
