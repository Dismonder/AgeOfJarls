using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AgeOfJarls.Core;
using UnityEngine;

namespace AgeOfJarls.Settlement
{
    /// <summary>
    /// Ranks in a settlement; Guest is anyone not listed. Order matters (higher = more rights, see
    /// <see cref="Permissions"/>) and the values are saved (format 5; format 4 had Hersir = 1, Jarl = 2).
    /// </summary>
    internal enum SettlementRole
    {
        Guest = 0,
        /// <summary>A free member: brings its settlers in, gives them everyday orders, holds feasts.</summary>
        Karl = 1,
        /// <summary>The troop's officer: alarm, combat roles, war banners, battle orders.</summary>
        Huskarl = 2,
        /// <summary>A steward: work and totems, dismissals, the name and tier, and the Karl and Huskarl ranks.</summary>
        Hersir = 3,
        /// <summary>A ruler: everything, the Hersirs included; up to Settlement/MaxJarls of them (two by default).</summary>
        Jarl = 4,
    }

    internal sealed class SettlementMember
    {
        internal long PlayerId;
        /// <summary>Name at the time the role was given; only for display.</summary>
        internal string Name = "";
        internal SettlementRole Role;
    }

    /// <summary>
    /// A settler on the roster. The game gives every ZDO a new ZDOID each time the world loads, so a settler is known by
    /// the stable id in its own ZDO (<see cref="Keys.ZdoSettlerUid"/>) and a bed by its position (beds never move).
    /// </summary>
    internal sealed class RosterEntry
    {
        internal long Uid;
        /// <summary>Name when the settler joined, so the roster reads well while the settler is not loaded.</summary>
        internal string Name = "";
        /// <summary>Whether a vanilla bed is assigned, and where it stands.</summary>
        internal bool HasBed;
        internal Vector3 BedPosition;
    }

    /// <summary>
    /// A named area of the settlement, marked on the map: a circle that may lie outside the table's radius (a
    /// warehouse across the river, reached through a portal). Chests inside count as the settlement's chests.
    /// </summary>
    internal sealed class SettlementZone
    {
        internal const string KindWarehouse = "warehouse";
        internal const string KindOther = "other";
        internal const float MinRadius = 5f;
        internal const float MaxRadius = 80f;
        internal const int MaxNameLength = 24;

        internal long Id;
        internal string Name = "";
        internal string Kind = KindWarehouse;
        internal Vector3 Center;
        internal float Radius = 15f;

        internal bool Contains(Vector3 point) => Utils.DistanceXZ(Center, point) <= Radius;

        internal SettlementZone Clone() => new SettlementZone { Id = Id, Name = Name, Kind = Kind, Center = Center, Radius = Radius };
    }

    /// <summary>Everything a settlement knows, stored as one versioned blob in the Jarl's Table ZDO.</summary>
    internal sealed class SettlementData
    {
        /// <summary>Bump on format changes; <see cref="Deserialize"/> keeps reading older versions.</summary>
        private const int FormatVersion = 6;
        /// <summary>Version 6 added the areas marked on the map.</summary>
        private const int FirstZonesVersion = 6;
        internal const int MaxZones = 16;
        /// <summary>Versions 2 and 3 listed settlers and beds by ZDOID, which does not survive a world reload.</summary>
        private const int FirstStableRosterVersion = 4;
        /// <summary>Version 5 added the Karl and Huskarl ranks below the Hersir, which moved the saved values.</summary>
        private const int FirstRankLadderVersion = 5;
        private const int MaxMembers = 64;
        private const int MaxRoster = 256;
        private const int MaxNameLength = 32;
        private const string Module = "Settlement";

        /// <summary>Empty = display the default name built from the Jarl's name.</summary>
        internal string Name = "";
        internal int Tier;
        internal List<SettlementMember> Members = new List<SettlementMember>();
        internal List<RosterEntry> Settlers = new List<RosterEntry>();
        /// <summary>Areas marked on the map (<see cref="SettlementZone"/>), at most <see cref="MaxZones"/>.</summary>
        internal List<SettlementZone> Zones = new List<SettlementZone>();

        /// <summary>Read from an older format: the table's owner saves it again in the current one.</summary>
        internal bool NeedsRewrite;

        internal SettlementZone FindZone(long id) => Zones.Find(z => z.Id == id);

        /// <summary>The first area that holds the point, if any.</summary>
        internal SettlementZone ZoneAt(Vector3 point) => Zones.Find(z => z.Contains(point));

        /// <summary>Adds or replaces an area (by id), with its values clamped; false when there is no room for a new one.</summary>
        internal bool SetZone(SettlementZone zone)
        {
            if (zone == null)
            {
                return false;
            }
            SettlementZone existing = FindZone(zone.Id);
            if (existing == null && Zones.Count >= MaxZones)
            {
                return false;
            }
            var clean = new SettlementZone
            {
                Id = zone.Id,
                Name = TextUtil.SanitizeName(zone.Name, SettlementZone.MaxNameLength),
                Kind = zone.Kind == SettlementZone.KindWarehouse ? SettlementZone.KindWarehouse : SettlementZone.KindOther,
                Center = zone.Center,
                Radius = Mathf.Clamp(zone.Radius, SettlementZone.MinRadius, SettlementZone.MaxRadius),
            };
            if (existing != null)
            {
                Zones[Zones.IndexOf(existing)] = clean;
            }
            else
            {
                Zones.Add(clean);
            }
            return true;
        }

        internal bool RemoveZone(long id) => Zones.RemoveAll(z => z.Id == id) > 0;

        /// <summary>The senior Jarl: the first one listed (the founder, or whoever the title was handed to).</summary>
        internal SettlementMember Jarl => Members.Find(m => m.Role == SettlementRole.Jarl);

        internal int JarlCount => Members.Count(m => m.Role == SettlementRole.Jarl);

        internal SettlementRole RoleOf(long playerId) =>
            Members.Find(m => m.PlayerId == playerId)?.Role ?? SettlementRole.Guest;

        /// <summary>Names of the members with this rank, in the order they were listed.</summary>
        internal List<string> NamesWith(SettlementRole role) => Members.Where(m => m.Role == role).Select(m => m.Name).ToList();

        internal bool HasSettler(long uid) => FindSettler(uid) != null;

        internal RosterEntry FindSettler(long uid)
        {
            foreach (RosterEntry entry in Settlers)
            {
                if (entry.Uid == uid)
                {
                    return entry;
                }
            }
            return null;
        }

        internal int SettlersWithBed => Settlers.FindAll(s => s.HasBed).Count;

        /// <summary>
        /// Why <paramref name="requester"/> may not give <paramref name="target"/> this rank (a localization token), or
        /// null when it may; the requester's side asks first for the message, the table's owner again before applying.
        /// Anybody may step down or leave, except the last Jarl. Otherwise a Hersir or a Jarl changes only members
        /// below itself and gives only ranks below its own; a Jarl also appoints co-Jarls while there is room, and only
        /// the senior Jarl demotes another Jarl.
        /// </summary>
        internal string RankChangeProblem(long requester, long target, SettlementRole rank, int maxJarls)
        {
            if (!Enum.IsDefined(typeof(SettlementRole), rank))
            {
                return "$aoj_msg_rank_denied";
            }
            SettlementRole mine = RoleOf(requester);
            SettlementRole current = RoleOf(target);
            if (rank == current)
            {
                return "$aoj_msg_rank_same";
            }
            if (current == SettlementRole.Jarl && JarlCount <= 1)
            {
                return "$aoj_msg_last_jarl";
            }
            if (target == requester)
            {
                return rank < current ? null : "$aoj_msg_rank_denied";
            }
            if (mine < SettlementRole.Hersir)
            {
                return "$aoj_msg_rank_denied";
            }
            if (current == SettlementRole.Jarl)
            {
                if (Jarl?.PlayerId != requester)
                {
                    return "$aoj_msg_rank_senior";
                }
            }
            else if (current >= mine)
            {
                return "$aoj_msg_rank_denied";
            }
            if (rank == SettlementRole.Jarl)
            {
                if (mine != SettlementRole.Jarl)
                {
                    return "$aoj_msg_rank_denied";
                }
                if (JarlCount >= maxJarls)
                {
                    return "$aoj_msg_jarls_full";
                }
            }
            else if (rank >= mine)
            {
                return "$aoj_msg_rank_denied";
            }
            if (current == SettlementRole.Guest && Members.Count >= MaxMembers)
            {
                return "$aoj_msg_rank_full";
            }
            return null;
        }

        /// <summary>Applies a rank <see cref="RankChangeProblem"/> allowed; Guest takes the member off the list.</summary>
        internal void SetRank(long target, string name, SettlementRole rank)
        {
            SettlementMember member = Members.Find(m => m.PlayerId == target);
            string clean = TextUtil.SanitizeName(name, MaxNameLength);
            if (rank == SettlementRole.Guest)
            {
                if (member != null)
                {
                    Members.Remove(member);
                }
                return;
            }
            if (member == null)
            {
                Members.Add(new SettlementMember { PlayerId = target, Name = clean, Role = rank });
                return;
            }
            member.Role = rank;
            if (clean.Length > 0)
            {
                member.Name = clean;
            }
        }

        /// <summary>
        /// A Jarl hands its title to another player and stays as a Hersir; the new Jarl takes its place in the list,
        /// the senior seat included. False when nothing changed.
        /// </summary>
        internal bool HandOver(long requester, long target, string name)
        {
            SettlementMember giver = Members.Find(m => m.PlayerId == requester);
            if (giver == null || giver.Role != SettlementRole.Jarl || requester == target)
            {
                return false;
            }
            SettlementMember taker = Members.Find(m => m.PlayerId == target);
            if (taker == null)
            {
                if (Members.Count >= MaxMembers)
                {
                    return false;
                }
                taker = new SettlementMember { PlayerId = target, Name = TextUtil.SanitizeName(name, MaxNameLength) };
                Members.Add(taker);
            }
            taker.Role = SettlementRole.Jarl;
            giver.Role = SettlementRole.Hersir;
            int giverIndex = Members.IndexOf(giver);
            int takerIndex = Members.IndexOf(taker);
            Members[giverIndex] = taker;
            Members[takerIndex] = giver;
            return true;
        }

        /// <summary>An empty name brings back the default one built from the Jarl's name.</summary>
        internal bool Rename(string name)
        {
            string clean = TextUtil.SanitizeName(name, MaxNameLength);
            if (clean == Name)
            {
                return false;
            }
            Name = clean;
            return true;
        }

        /// <summary>False when the settlement is full or the settler is already listed.</summary>
        internal bool TryAddSettler(long uid, string name, int capacity)
        {
            if (uid == 0L || HasSettler(uid) || Settlers.Count >= Math.Min(capacity, MaxRoster))
            {
                return false;
            }
            // The name comes from a client's request: cleaned like every name shown to other players.
            Settlers.Add(new RosterEntry { Uid = uid, Name = TextUtil.SanitizeName(name, MaxNameLength) });
            return true;
        }

        internal bool RemoveSettler(long uid) => Settlers.RemoveAll(s => s.Uid == uid) > 0;

        internal byte[] Serialize()
        {
            var package = new ZPackage();
            package.Write(FormatVersion);
            package.Write(Name);
            package.Write(Tier);
            package.Write(Members.Count);
            foreach (SettlementMember member in Members)
            {
                package.Write(member.PlayerId);
                package.Write(member.Name);
                package.Write((int)member.Role);
            }
            package.Write(Settlers.Count);
            foreach (RosterEntry settler in Settlers)
            {
                package.Write(settler.Uid);
                package.Write(settler.Name);
                package.Write(settler.HasBed);
                package.Write(settler.BedPosition);
            }
            package.Write(Zones.Count);
            foreach (SettlementZone zone in Zones)
            {
                package.Write(zone.Id);
                package.Write(zone.Name ?? "");
                package.Write(zone.Kind ?? SettlementZone.KindOther);
                package.Write(zone.Center);
                package.Write(zone.Radius);
            }
            return package.GetArray();
        }

        /// <summary>Null when the data is from a newer mod version or corrupted; callers must then leave it untouched.</summary>
        internal static SettlementData Deserialize(byte[] data)
        {
            try
            {
                var package = new ZPackage(data);
                int version = package.ReadInt();
                if (version < 1 || version > FormatVersion)
                {
                    Log.Error(Module, $"Settlement format {version} is not supported by this mod version");
                    return null;
                }

                var settlement = new SettlementData
                {
                    Name = package.ReadString(),
                    Tier = package.ReadInt(),
                    NeedsRewrite = version < FormatVersion,
                };
                int members = ReadCount(package, MaxMembers, "member");
                for (int i = 0; i < members; i++)
                {
                    long playerId = package.ReadLong();
                    string name = package.ReadString();
                    int saved = package.ReadInt();
                    // Before version 5: 1 = Hersir, 2 = Jarl.
                    SettlementRole role = version >= FirstRankLadderVersion ? (SettlementRole)saved
                        : saved == 2 ? SettlementRole.Jarl
                        : saved == 1 ? SettlementRole.Hersir
                        : SettlementRole.Guest;
                    if (role == SettlementRole.Guest || !Enum.IsDefined(typeof(SettlementRole), role))
                    {
                        Log.Warning(Module, $"'{settlement.Name}': member {name} with unknown rank {saved} dropped");
                        settlement.NeedsRewrite = true;
                        continue;
                    }
                    settlement.Members.Add(new SettlementMember { PlayerId = playerId, Name = name, Role = role });
                }

                // Version 1 had no roster. Versions 2-3 used ZDOIDs, which the game renumbers on every world load:
                // those entries cannot be matched to anyone any more, so the settlers have to be accepted again.
                if (version >= FirstStableRosterVersion)
                {
                    int settlers = ReadCount(package, MaxRoster, "settler");
                    for (int i = 0; i < settlers; i++)
                    {
                        settlement.Settlers.Add(new RosterEntry
                        {
                            Uid = package.ReadLong(),
                            Name = package.ReadString(),
                            HasBed = package.ReadBool(),
                            BedPosition = package.ReadVector3(),
                        });
                    }
                }
                else if (version >= 2)
                {
                    int legacy = ReadCount(package, MaxRoster, "settler");
                    if (legacy > 0)
                    {
                        Log.Warning(Module, $"'{settlement.Name}': {legacy} settler(s) listed in the old format are dropped; accept them again at the table");
                    }
                }
                if (version >= FirstZonesVersion)
                {
                    int zones = ReadCount(package, MaxZones, "zone");
                    for (int i = 0; i < zones; i++)
                    {
                        settlement.Zones.Add(new SettlementZone
                        {
                            Id = package.ReadLong(),
                            Name = package.ReadString(),
                            Kind = package.ReadString(),
                            Center = package.ReadVector3(),
                            Radius = package.ReadSingle(),
                        });
                    }
                }
                return settlement;
            }
            catch (Exception e) when (e is IOException || e is ArgumentException)
            {
                Log.Error(Module, $"Corrupted settlement data: {e.Message}");
                return null;
            }
        }

        private static int ReadCount(ZPackage package, int max, string what)
        {
            int count = package.ReadInt();
            if (count < 0 || count > max)
            {
                throw new ArgumentException($"invalid {what} count {count}");
            }
            return count;
        }
    }
}
