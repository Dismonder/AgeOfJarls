using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AgeOfJarls.Core;
using AgeOfJarls.Family;
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
        private const int FormatVersion = 7;
        /// <summary>Version 7 added the families: couples, courtships, children, moods and the family flags.</summary>
        private const int FirstFamiliesVersion = 7;
        internal const int MaxCouples = 128;
        internal const int MaxCourtships = 64;
        internal const int MaxChildren = 128;
        internal const int MaxMoods = 256;
        /// <summary>Version 6 added the areas marked on the map.</summary>
        private const int FirstZonesVersion = 6;
        internal const int MaxZones = 16;
        /// <summary>Versions 2 and 3 listed settlers and beds by ZDOID, which does not survive a world reload.</summary>
        private const int FirstStableRosterVersion = 4;
        /// <summary>Version 5 added the Karl and Huskarl ranks below the Hersir, which moved the saved values.</summary>
        private const int FirstRankLadderVersion = 5;
        private const int MaxMembers = 64;
        internal const int MaxRoster = 256;
        private const int MaxNameLength = 32;
        private const string Module = "Settlement";

        /// <summary>Empty = display the default name built from the Jarl's name.</summary>
        internal string Name = "";
        internal int Tier;
        internal List<SettlementMember> Members = new List<SettlementMember>();
        internal List<RosterEntry> Settlers = new List<RosterEntry>();
        /// <summary>Areas marked on the map (<see cref="SettlementZone"/>), at most <see cref="MaxZones"/>.</summary>
        internal List<SettlementZone> Zones = new List<SettlementZone>();
        /// <summary>Family records (format 7), written only by the table's owner; see <see cref="Couple"/>.</summary>
        internal List<Couple> Couples = new List<Couple>();
        internal List<Courtship> Courtships = new List<Courtship>();
        internal List<ChildRecord> Children = new List<ChildRecord>();
        internal List<Mood> Moods = new List<Mood>();
        /// <summary>Bits of <see cref="Family.FamilyFlags"/> (no new couples, no births), toggled by Manage.</summary>
        internal byte FamilyFlags;

        /// <summary>Read from an older format: the table's owner saves it again in the current one.</summary>
        internal bool NeedsRewrite;

        /// <summary>True when the pairs {a, b} and {x, y} are the same two settlers, in either order.</summary>
        internal static bool SameUids(long a, long b, long x, long y) => (a == x && b == y) || (a == y && b == x);

        internal bool HasFamilyFlag(byte flag) => (FamilyFlags & flag) != 0;

        /// <summary>Changes one supported policy bit, preserving concurrent changes to the others.</summary>
        internal bool SetFamilyFlag(byte bit, bool enabled)
        {
            if (bit != Family.FamilyFlags.NoNewCouples && bit != Family.FamilyFlags.NoBirths) return false;
            byte flags = enabled ? (byte)(FamilyFlags | bit) : (byte)(FamilyFlags & ~bit);
            if (FamilyFlags == flags) return false;
            FamilyFlags = flags;
            return true;
        }

        /// <summary>The settler's couple; null when single. A record with one side 0 is a widowed carrier still expecting.</summary>
        internal Couple FindCouple(long uid)
        {
            if (uid == 0L)
            {
                return null;
            }
            foreach (Couple couple in Couples)
            {
                if (couple.Has(uid))
                {
                    return couple;
                }
            }
            return null;
        }

        internal Couple FindCouple(long a, long b)
        {
            foreach (Couple couple in Couples)
            {
                if (SameUids(couple.A, couple.B, a, b))
                {
                    return couple;
                }
            }
            return null;
        }

        /// <summary>The settler's spouse; 0 when single.</summary>
        internal long PartnerOf(long uid) => FindCouple(uid)?.Other(uid) ?? 0L;

        internal Courtship FindCourtship(long uid)
        {
            if (uid == 0L)
            {
                return null;
            }
            foreach (Courtship courtship in Courtships)
            {
                if (courtship.Has(uid))
                {
                    return courtship;
                }
            }
            return null;
        }

        internal ChildRecord FindChild(long uid)
        {
            foreach (ChildRecord child in Children)
            {
                if (child.Uid == uid)
                {
                    return child;
                }
            }
            return null;
        }

        /// <summary>Children born to this settler (grown-up ones included), in birth order.</summary>
        internal IEnumerable<ChildRecord> ChildrenOf(long parentUid)
        {
            if (parentUid == 0L)
            {
                yield break;
            }
            foreach (ChildRecord child in Children)
            {
                if (child.Mother == parentUid || child.Father == parentUid)
                {
                    yield return child;
                }
            }
        }

        /// <summary>Parent and child, or siblings (a parent in common): such two never court.</summary>
        internal bool AreRelated(long a, long b)
        {
            if (a == 0L || b == 0L || a == b)
            {
                return false;
            }
            ChildRecord childA = FindChild(a);
            ChildRecord childB = FindChild(b);
            if (childA != null && (childA.Mother == b || childA.Father == b))
            {
                return true;
            }
            if (childB != null && (childB.Mother == a || childB.Father == a))
            {
                return true;
            }
            if (childA == null || childB == null)
            {
                return false;
            }
            return (childA.Mother != 0L && (childA.Mother == childB.Mother || childA.Mother == childB.Father))
                || (childA.Father != 0L && (childA.Father == childB.Father || childA.Father == childB.Mother));
        }

        /// <summary>A settler born here that has not come of age yet; settlers without a child record arrived as adults.</summary>
        internal bool IsMinor(long uid, double now, double dayLength, FamilyConfig cfg)
        {
            ChildRecord child = FindChild(uid);
            return child != null && FamilyRules.StageAt(child.Born, now, dayLength, cfg) != LifeStage.Adult;
        }

        /// <summary>Settlers on the roster that are minors (<see cref="IsMinor"/>).</summary>
        internal int MinorCount(double now, double dayLength, FamilyConfig cfg)
        {
            int count = 0;
            foreach (RosterEntry entry in Settlers)
            {
                if (IsMinor(entry.Uid, now, dayLength, cfg))
                {
                    count++;
                }
            }
            return count;
        }

        internal int AdultCount(double now, double dayLength, FamilyConfig cfg) => Settlers.Count - MinorCount(now, dayLength, cfg);

        /// <summary>Couples expecting a child.</summary>
        internal int PregnancyCount()
        {
            int count = 0;
            foreach (Couple couple in Couples)
            {
                if (couple.IsExpecting)
                {
                    count++;
                }
            }
            return count;
        }

        internal bool HasMood(long uid, MoodKind kind, double now)
        {
            foreach (Mood mood in Moods)
            {
                if (mood.Uid == uid && mood.Kind == kind && mood.Until > now)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Gives the settler a mood until the given time, replacing one of the same kind; the oldest goes when the list is full.</summary>
        internal void AddMood(long uid, MoodKind kind, double until)
        {
            foreach (Mood existing in Moods)
            {
                if (existing.Uid == uid && existing.Kind == kind)
                {
                    existing.Until = until;
                    return;
                }
            }
            while (Moods.Count >= MaxMoods)
            {
                int oldest = 0;
                for (int i = 1; i < Moods.Count; i++)
                {
                    if (Moods[i].Until < Moods[oldest].Until)
                    {
                        oldest = i;
                    }
                }
                Moods.RemoveAt(oldest);
            }
            Moods.Add(new Mood { Uid = uid, Kind = kind, Until = until });
        }

        /// <summary>Drops the moods that have ended; true when any did.</summary>
        internal bool PruneMoods(double now) => Moods.RemoveAll(m => m.Until <= now) > 0;

        /// <summary>
        /// Undoes the family records of a settler that left the roster (died, was dismissed, vanished): its couple is
        /// dissolved (the partner is widowed), a pregnancy it carried is cancelled (one its partner carries goes on, on
        /// a couple record with the lost side set to 0), its courtship is dropped, its children retain biological
        /// ancestry (minors are reported as orphaned), its own child record goes (the parents' couple gets the child
        /// slot back and the parents are reported as bereaved) and its moods end. The caller (the table) adds the Grief
        /// moods and the chronicle lines. Pure, so the tests cover it; call it beside <see cref="RemoveSettler"/>.
        /// </summary>
        internal FamilyCleanupResult FamilyCleanup(long uid, double now, double dayLength, FamilyConfig cfg)
        {
            var result = new FamilyCleanupResult();
            if (uid == 0L)
            {
                return result;
            }
            Couple couple = FindCouple(uid);
            if (couple != null)
            {
                long partner = couple.Other(uid);
                if (partner != 0L)
                {
                    result.WidowedUids.Add(partner);
                }
                if (couple.IsExpecting && couple.Carrier == uid)
                {
                    result.PregnancyCancelled = true;
                }
                if (couple.IsExpecting && couple.Carrier != uid && partner != 0L)
                {
                    // The widow still gives birth: the record stays until then, with the lost side gone.
                    if (couple.A == uid)
                    {
                        couple.A = 0L;
                    }
                    else
                    {
                        couple.B = 0L;
                    }
                }
                else
                {
                    Couples.Remove(couple);
                }
                result.Changed = true;
            }
            if (Courtships.RemoveAll(c => c.Has(uid)) > 0)
            {
                result.Changed = true;
            }
            foreach (ChildRecord child in Children)
            {
                if (child.Uid == uid || (child.Mother != uid && child.Father != uid))
                {
                    continue;
                }
                // Ancestry survives death; live parent availability comes from the roster.
                if (FamilyRules.StageAt(child.Born, now, dayLength, cfg) != LifeStage.Adult)
                {
                    result.OrphanedUids.Add(child.Uid);
                }
                result.Changed = true;
            }
            ChildRecord own = FindChild(uid);
            if (own != null)
            {
                if (HasSettler(own.Mother))
                {
                    result.BereavedUids.Add(own.Mother);
                }
                if (HasSettler(own.Father))
                {
                    result.BereavedUids.Add(own.Father);
                }
                Couple parents = own.Mother != 0L && own.Father != 0L ? FindCouple(own.Mother, own.Father) : null;
                if (parents != null && parents.ChildrenBorn > 0)
                {
                    parents.ChildrenBorn--;
                }
                Children.Remove(own);
                result.Changed = true;
            }
            if (Moods.RemoveAll(m => m.Uid == uid) > 0)
            {
                result.Changed = true;
            }
            return result;
        }

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

        /// <summary>Only youths and adults need their own beds; shared beds count both occupants.</summary>
        internal int SettlersWithBed => CountBedResidents(true, WorldClock.Now, WorldClock.DayLength, FamilyConfig.Live);

        /// <summary>Bed demand excludes infants and children even before the next reconciliation.</summary>
        internal int SettlersNeedingBed => CountBedResidents(false, WorldClock.Now, WorldClock.DayLength, FamilyConfig.Live);

        /// <summary>Pure bed count for UI demand and coverage, including unloaded residents.</summary>
        internal int CountBedResidents(bool onlyAssigned, double now, double dayLength, FamilyConfig cfg)
        {
            int count = 0;
            foreach (RosterEntry entry in Settlers)
            {
                if ((!onlyAssigned || entry.HasBed) && FamilyRules.NeedsOwnBed(FamilyRules.StageAt(FindChild(entry.Uid)?.Born ?? 0, now, dayLength, cfg))) count++;
            }
            return count;
        }

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

        /// <summary>Restores the reproductive limits of this unordered pair from retained children, including adults.</summary>
        internal Couple NewCouple(long a, long b, double now)
        {
            var couple = new Couple { A = a, B = b, Since = now };
            foreach (ChildRecord child in Children)
            {
                if (SameUids(child.Mother, child.Father, a, b))
                {
                    couple.ChildrenBorn++;
                    couple.LastBirth = Math.Max(couple.LastBirth, child.Born);
                }
            }
            return couple;
        }

        /// <summary>Accepts verified birth facts and counts an incoming minor under the same policy as residents.</summary>
        internal bool TryAcceptSettler(long uid, string name, double born, long mother, long father, bool female,
            int capacity, double now, double dayLength, FamilyConfig cfg)
        {
            LifeStage stage = FamilyRules.StageAt(born, now, dayLength, cfg);
            bool minor = stage != LifeStage.Adult;
            bool import = born > 0 && minor && FindChild(uid) == null;
            if ((cfg.ChildrenCountTowardLimit ? Settlers.Count >= capacity : !minor && AdultCount(now, dayLength, cfg) >= capacity) ||
                (import && Children.Count >= MaxChildren) || !TryAddSettler(uid, name, MaxRoster))
            {
                return false;
            }
            if (import)
            {
                Children.Add(new ChildRecord { Uid = uid, Name = FindSettler(uid).Name, Born = born,
                    Mother = mother, Father = father, Female = female, LastStage = (int)stage });
            }
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

        /// <summary>Takes the settler off the roster only; the table also calls <see cref="FamilyCleanup"/>.</summary>
        internal bool RemoveSettler(long uid) => Settlers.RemoveAll(s => s.Uid == uid) > 0;

        /// <summary>Reads the saved settlement blob without requiring a loaded table component.</summary>
        internal static SettlementData Read(ZDO zdo)
        {
            byte[] blob = zdo?.GetByteArray(Keys.ZdoSettlement);
            return blob != null ? Deserialize(blob) : null;
        }

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
            package.Write(FamilyFlags);
            package.Write(Couples.Count);
            foreach (Couple couple in Couples)
            {
                package.Write(couple.A);
                package.Write(couple.B);
                package.Write(couple.Since);
                package.Write(couple.ChildrenBorn);
                package.Write(couple.LastBirth);
                package.Write(couple.DueAt);
                package.Write(couple.Carrier);
                package.Write(couple.ConceivedAt);
                package.Write(couple.ConceptionMother);
                package.Write(couple.ConceptionFather);
            }
            package.Write(Courtships.Count);
            foreach (Courtship courtship in Courtships)
            {
                package.Write(courtship.A);
                package.Write(courtship.B);
                package.Write(courtship.StartedAt);
            }
            package.Write(Children.Count);
            foreach (ChildRecord child in Children)
            {
                package.Write(child.Uid);
                package.Write(child.Mother);
                package.Write(child.Father);
                package.Write(child.Born);
                package.Write(child.LastStage);
                package.Write(child.Female);
                package.Write(child.Name ?? "");
            }
            package.Write(Moods.Count);
            foreach (Mood mood in Moods)
            {
                package.Write(mood.Uid);
                package.Write((int)mood.Kind);
                package.Write(mood.Until);
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
                if (version >= FirstFamiliesVersion)
                {
                    settlement.FamilyFlags = package.ReadByte();
                    int couples = ReadCount(package, MaxCouples, "couple");
                    for (int i = 0; i < couples; i++)
                    {
                        settlement.Couples.Add(new Couple
                        {
                            A = package.ReadLong(),
                            B = package.ReadLong(),
                            Since = package.ReadDouble(),
                            ChildrenBorn = package.ReadInt(),
                            LastBirth = package.ReadDouble(),
                            DueAt = package.ReadDouble(),
                            Carrier = package.ReadLong(),
                            ConceivedAt = package.ReadDouble(),
                            ConceptionMother = package.ReadLong(),
                            ConceptionFather = package.ReadLong(),
                        });
                    }
                    int courtships = ReadCount(package, MaxCourtships, "courtship");
                    for (int i = 0; i < courtships; i++)
                    {
                        settlement.Courtships.Add(new Courtship
                        {
                            A = package.ReadLong(),
                            B = package.ReadLong(),
                            StartedAt = package.ReadDouble(),
                        });
                    }
                    int children = ReadCount(package, MaxChildren, "child");
                    for (int i = 0; i < children; i++)
                    {
                        settlement.Children.Add(new ChildRecord
                        {
                            Uid = package.ReadLong(),
                            Mother = package.ReadLong(),
                            Father = package.ReadLong(),
                            Born = package.ReadDouble(),
                            LastStage = package.ReadInt(),
                            Female = package.ReadBool(),
                            Name = package.ReadString(),
                        });
                    }
                    int moods = ReadCount(package, MaxMoods, "mood");
                    for (int i = 0; i < moods; i++)
                    {
                        settlement.Moods.Add(new Mood
                        {
                            Uid = package.ReadLong(),
                            Kind = (MoodKind)package.ReadInt(),
                            Until = package.ReadDouble(),
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
