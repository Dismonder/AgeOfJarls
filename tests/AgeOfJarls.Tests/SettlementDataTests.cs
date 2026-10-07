using System.Linq;
using AgeOfJarls.Family;
using AgeOfJarls.Settlement;
using UnityEngine;
using Xunit;

namespace AgeOfJarls.Tests
{
    /// <summary>The settlement blob in the Jarl's Table ZDO: what worlds already hold must keep loading.</summary>
    public class SettlementDataTests
    {
        private const long Founder = 101;
        private const long Friend = 202;

        private static SettlementData Sample()
        {
            var data = new SettlementData { Name = "Birka", Tier = 3 };
            data.Members.Add(new SettlementMember { PlayerId = Founder, Name = "Ragnar", Role = SettlementRole.Jarl });
            data.Members.Add(new SettlementMember { PlayerId = Friend, Name = "Lagertha", Role = SettlementRole.Hersir });
            data.Settlers.Add(new RosterEntry { Uid = 11, Name = "Tove", HasBed = true, BedPosition = new Vector3(1.5f, 2f, -3.25f) });
            data.Settlers.Add(new RosterEntry { Uid = 12, Name = "Eir" });
            data.Zones.Add(new SettlementZone { Id = 77, Name = "Magazyn", Kind = SettlementZone.KindWarehouse, Center = new Vector3(120f, 30f, -40f), Radius = 20f });
            return data;
        }

        [Fact]
        public void ZonesRoundTripAndAreClamped()
        {
            SettlementData copy = SettlementData.Deserialize(Sample().Serialize());

            Assert.NotNull(copy);
            SettlementZone zone = Assert.Single(copy.Zones);
            Assert.Equal(77, zone.Id);
            Assert.Equal("Magazyn", zone.Name);
            Assert.Equal(SettlementZone.KindWarehouse, zone.Kind);
            Assert.Equal(new Vector3(120f, 30f, -40f), zone.Center);
            Assert.Equal(20f, zone.Radius);
            Assert.True(zone.Contains(new Vector3(130f, 0f, -45f)));
            Assert.False(zone.Contains(new Vector3(150f, 0f, -45f)));

            Assert.True(copy.SetZone(new SettlementZone { Id = 77, Name = "<b>Big</b>", Kind = "weird", Radius = 500f }));
            zone = Assert.Single(copy.Zones);
            Assert.Equal("bBig/b", zone.Name);
            Assert.Equal(SettlementZone.KindOther, zone.Kind);
            Assert.Equal(SettlementZone.MaxRadius, zone.Radius);
            Assert.True(copy.RemoveZone(77));
            Assert.Empty(copy.Zones);
        }

        [Fact]
        public void RoundTripKeepsEverything()
        {
            SettlementData copy = SettlementData.Deserialize(Sample().Serialize());

            Assert.NotNull(copy);
            Assert.False(copy.NeedsRewrite);
            Assert.Equal("Birka", copy.Name);
            Assert.Equal(3, copy.Tier);
            Assert.Equal(new[] { "Ragnar", "Lagertha" }, copy.Members.Select(m => m.Name));
            Assert.Equal(new[] { SettlementRole.Jarl, SettlementRole.Hersir }, copy.Members.Select(m => m.Role));
            Assert.Equal(new long[] { 11, 12 }, copy.Settlers.Select(s => s.Uid));
            Assert.True(copy.Settlers[0].HasBed);
            Assert.Equal(new Vector3(1.5f, 2f, -3.25f), copy.Settlers[0].BedPosition);
            Assert.False(copy.Settlers[1].HasBed);
        }

        // Format 4 (mod 0.2) saved Hersir = 1 and Jarl = 2; format 5 added Karl and Huskarl below the Hersir.
        [Fact]
        public void Format4RanksAreMappedAndRewritten()
        {
            var package = new ZPackage();
            package.Write(4);
            package.Write("Old");
            package.Write(1);
            package.Write(2);
            WriteMember(package, Founder, "Ragnar", 2);
            WriteMember(package, Friend, "Lagertha", 1);
            package.Write(1);
            package.Write(11L);
            package.Write("Tove");
            package.Write(true);
            package.Write(new Vector3(1f, 2f, 3f));

            SettlementData data = SettlementData.Deserialize(package.GetArray());

            Assert.NotNull(data);
            Assert.True(data.NeedsRewrite);
            Assert.Equal(SettlementRole.Jarl, data.RoleOf(Founder));
            Assert.Equal(SettlementRole.Hersir, data.RoleOf(Friend));
            Assert.Single(data.Settlers);
            SettlementData again = SettlementData.Deserialize(data.Serialize());
            Assert.False(again.NeedsRewrite);
            Assert.Equal(SettlementRole.Hersir, again.RoleOf(Friend));
        }

        // Formats 2-3 listed settlers by ZDOID, which the game renumbers on every load: the members stay, the roster goes.
        [Fact]
        public void Format3RosterIsDroppedButMembersStay()
        {
            var package = new ZPackage();
            package.Write(3);
            package.Write("Older");
            package.Write(0);
            package.Write(1);
            WriteMember(package, Founder, "Ragnar", 2);
            package.Write(2);

            SettlementData data = SettlementData.Deserialize(package.GetArray());

            Assert.NotNull(data);
            Assert.Equal(SettlementRole.Jarl, data.RoleOf(Founder));
            Assert.Empty(data.Settlers);
        }

        [Fact]
        public void NewerFormatIsLeftAlone()
        {
            var package = new ZPackage();
            package.Write(99);
            package.Write("Future");

            Assert.Null(SettlementData.Deserialize(package.GetArray()));
        }

        [Fact]
        public void TruncatedDataIsLeftAlone()
        {
            byte[] blob = Sample().Serialize();

            Assert.Null(SettlementData.Deserialize(blob.Take(blob.Length / 2).ToArray()));
        }

        [Fact]
        public void UnknownRankIsDroppedAndRewritten()
        {
            var package = new ZPackage();
            package.Write(5);
            package.Write("Odd");
            package.Write(0);
            package.Write(2);
            WriteMember(package, Founder, "Ragnar", (int)SettlementRole.Jarl);
            WriteMember(package, Friend, "Nobody", 9);
            package.Write(0);

            SettlementData data = SettlementData.Deserialize(package.GetArray());

            Assert.NotNull(data);
            Assert.True(data.NeedsRewrite);
            Assert.Single(data.Members);
            Assert.Equal(SettlementRole.Guest, data.RoleOf(Friend));
        }

        [Fact]
        public void RosterRespectsCapacityAndDuplicates()
        {
            var data = new SettlementData();

            Assert.True(data.TryAddSettler(11, "Tove", 2));
            Assert.True(data.TryAddSettler(12, "Eir", 2));
            Assert.False(data.TryAddSettler(13, "Ulf", 2));
            Assert.False(data.TryAddSettler(11, "Tove again", 3));
            Assert.False(data.TryAddSettler(0, "No id", 3));
            Assert.True(data.RemoveSettler(11));
            Assert.False(data.HasSettler(11));
        }

        // The name arrives in a client's request and is shown to every player.
        [Fact]
        public void RosterNamesAreCleaned()
        {
            var data = new SettlementData();

            Assert.True(data.TryAddSettler(11, "<color=red>$aoj_evil</color>", 3));

            Assert.DoesNotContain("<", data.FindSettler(11).Name);
            Assert.DoesNotContain("$", data.FindSettler(11).Name);
        }

        private const double Day = 1200.0;
        private const long Tove = 11;
        private const long Eir = 12;
        private const long Ulf = 13;
        private const long Runa = 14;
        private const long Baby = 21;
        private const long Lad = 22;

        private static FamilyConfig Cfg() => new FamilyConfig { InfantDays = 2f, ChildDays = 4f, YouthDays = 4f };

        /// <summary>Tove and Ulf are wed with a newborn (Baby, born at 15000 s) and Tove expects another; Eir courts Runa; Lad (born at 100 s) is grown up.</summary>
        private static SettlementData FamilySample()
        {
            SettlementData data = Sample();
            data.Settlers.Add(new RosterEntry { Uid = Ulf, Name = "Ulf" });
            data.Settlers.Add(new RosterEntry { Uid = Runa, Name = "Runa" });
            data.Settlers.Add(new RosterEntry { Uid = Baby, Name = "Leif" });
            data.Settlers.Add(new RosterEntry { Uid = Lad, Name = "Knut" });
            data.FamilyFlags = FamilyFlags.NoBirths;
            data.Couples.Add(new Couple { A = Tove, B = Ulf, Since = 1000.5, ChildrenBorn = 1, LastBirth = 15000.0, DueAt = 19000.25, Carrier = Tove, ConceivedAt = 17000.0, ConceptionMother = Tove, ConceptionFather = Ulf });
            data.Courtships.Add(new Courtship { A = Eir, B = Runa, StartedAt = 8000.0 });
            data.Children.Add(new ChildRecord { Uid = Baby, Mother = Tove, Father = Ulf, Born = 15000.0, LastStage = 0, Female = false, Name = "Leif" });
            data.Children.Add(new ChildRecord { Uid = Lad, Mother = Tove, Father = 0, Born = 100.0, LastStage = 3, Female = false, Name = "Knut" });
            data.Moods.Add(new Mood { Uid = Tove, Kind = MoodKind.NewParent, Until = 16000.0 });
            data.Moods.Add(new Mood { Uid = Runa, Kind = MoodKind.Grief, Until = 9999.0 });
            return data;
        }

        [Fact]
        public void FamiliesRoundTrip()
        {
            SettlementData copy = SettlementData.Deserialize(FamilySample().Serialize());

            Assert.NotNull(copy);
            Assert.False(copy.NeedsRewrite);
            Assert.Equal(FamilyFlags.NoBirths, copy.FamilyFlags);
            Assert.True(copy.HasFamilyFlag(FamilyFlags.NoBirths));
            Assert.False(copy.HasFamilyFlag(FamilyFlags.NoNewCouples));

            Couple couple = Assert.Single(copy.Couples);
            Assert.Equal(Tove, couple.ConceptionMother);
            Assert.Equal(Ulf, couple.ConceptionFather);
            Assert.Equal(Tove, couple.A);
            Assert.Equal(Ulf, couple.B);
            Assert.Equal(1000.5, couple.Since);
            Assert.Equal(1, couple.ChildrenBorn);
            Assert.Equal(15000.0, couple.LastBirth);
            Assert.Equal(19000.25, couple.DueAt);
            Assert.Equal(Tove, couple.Carrier);
            Assert.Equal(17000.0, couple.ConceivedAt);
            Assert.True(couple.IsExpecting);

            Courtship courtship = Assert.Single(copy.Courtships);
            Assert.Equal(Eir, courtship.A);
            Assert.Equal(Runa, courtship.B);
            Assert.Equal(8000.0, courtship.StartedAt);

            Assert.Equal(2, copy.Children.Count);
            ChildRecord baby = copy.FindChild(Baby);
            Assert.Equal(Tove, baby.Mother);
            Assert.Equal(Ulf, baby.Father);
            Assert.Equal(15000.0, baby.Born);
            Assert.Equal(0, baby.LastStage);
            Assert.False(baby.Female);
            Assert.Equal("Leif", baby.Name);
            Assert.Equal(3, copy.FindChild(Lad).LastStage);

            Assert.Equal(2, copy.Moods.Count);
            Assert.Equal(MoodKind.NewParent, copy.Moods[0].Kind);
            Assert.Equal(16000.0, copy.Moods[0].Until);
            Assert.Equal(MoodKind.Grief, copy.Moods[1].Kind);
        }

        [Fact]
        public void FamilyPolicyRequestsPreserveOtherBits()
        {
            var data = new SettlementData();
            // Both requests can come from clients that last saw flags = 0.
            Assert.True(data.SetFamilyFlag(FamilyFlags.NoNewCouples, true));
            Assert.True(data.SetFamilyFlag(FamilyFlags.NoBirths, true));
            Assert.True(data.HasFamilyFlag(FamilyFlags.NoNewCouples));
            Assert.True(data.HasFamilyFlag(FamilyFlags.NoBirths));
            Assert.False(data.SetFamilyFlag(FamilyFlags.NoBirths, true));
            Assert.True(data.SetFamilyFlag(FamilyFlags.NoNewCouples, false));
            Assert.False(data.HasFamilyFlag(FamilyFlags.NoNewCouples));
            Assert.True(data.HasFamilyFlag(FamilyFlags.NoBirths));
            Assert.False(data.SetFamilyFlag(FamilyFlags.NoNewCouples, false));
            Assert.True(data.SetFamilyFlag(FamilyFlags.NoBirths, false));
            Assert.Equal(0, data.FamilyFlags);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(128)]
        [InlineData(255)]
        public void FamilyPolicyRequestsRejectUnknownOrCombinedMasks(byte bit)
        {
            var data = new SettlementData { FamilyFlags = FamilyFlags.NoBirths };
            Assert.False(data.SetFamilyFlag(bit, true));
            Assert.False(data.SetFamilyFlag(bit, false));
            Assert.Equal(FamilyFlags.NoBirths, data.FamilyFlags);
        }

        // Format 6 (mod 0.8) ended with the zones; format 7 appended the families.
        [Fact]
        public void Format6HasNoFamiliesAndIsRewritten()
        {
            var package = new ZPackage();
            package.Write(6);
            package.Write("Birka");
            package.Write(2);
            package.Write(1);
            WriteMember(package, Founder, "Ragnar", (int)SettlementRole.Jarl);
            package.Write(1);
            package.Write(Tove);
            package.Write("Tove");
            package.Write(true);
            package.Write(new Vector3(1f, 2f, 3f));
            package.Write(1);
            package.Write(77L);
            package.Write("Magazyn");
            package.Write(SettlementZone.KindWarehouse);
            package.Write(new Vector3(120f, 30f, -40f));
            package.Write(20f);

            SettlementData data = SettlementData.Deserialize(package.GetArray());

            Assert.NotNull(data);
            Assert.True(data.NeedsRewrite);
            Assert.Single(data.Settlers);
            Assert.Single(data.Zones);
            Assert.Equal(0, data.FamilyFlags);
            Assert.Empty(data.Couples);
            Assert.Empty(data.Courtships);
            Assert.Empty(data.Children);
            Assert.Empty(data.Moods);
            SettlementData again = SettlementData.Deserialize(data.Serialize());
            Assert.False(again.NeedsRewrite);
            Assert.Single(again.Zones);
        }

        [Fact]
        public void FamilyLookupsFindPartnersParentsAndChildren()
        {
            SettlementData data = FamilySample();

            Assert.Equal(Ulf, data.PartnerOf(Tove));
            Assert.Equal(Tove, data.PartnerOf(Ulf));
            Assert.Equal(0, data.PartnerOf(Eir));
            Assert.Same(data.FindCouple(Tove), data.FindCouple(Ulf, Tove));
            Assert.Null(data.FindCouple(0));
            Assert.Equal(Runa, data.FindCourtship(Eir).Other(Eir));
            Assert.Null(data.FindCourtship(Tove));
            Assert.Equal(new long[] { Baby, Lad }, data.ChildrenOf(Tove).Select(c => c.Uid));
            Assert.Equal(new long[] { Baby }, data.ChildrenOf(Ulf).Select(c => c.Uid));
            Assert.Empty(data.ChildrenOf(0));
            Assert.True(SettlementData.SameUids(1, 2, 2, 1));
            Assert.False(SettlementData.SameUids(1, 2, 1, 3));
            Assert.Equal(1, data.PregnancyCount());
        }

        [Fact]
        public void RelativesAreParentsChildrenAndSiblings()
        {
            SettlementData data = FamilySample();

            Assert.True(data.AreRelated(Tove, Baby));
            Assert.True(data.AreRelated(Baby, Ulf));
            Assert.True(data.AreRelated(Baby, Lad));
            Assert.True(data.AreRelated(Lad, Tove));
            Assert.False(data.AreRelated(Lad, Ulf));
            Assert.False(data.AreRelated(Tove, Ulf));
            Assert.False(data.AreRelated(Eir, Runa));
            Assert.False(data.AreRelated(Baby, Baby));
            Assert.False(data.AreRelated(0, Baby));
        }

        // Minors are derived from the birth time, never from the record: Lad has a record but came of age long ago.
        [Fact]
        public void MinorsAreCountedByStage()
        {
            SettlementData data = FamilySample();
            FamilyConfig cfg = Cfg();
            double now = 15000.0 + 3.0 * Day;

            Assert.True(data.IsMinor(Baby, now, Day, cfg));
            Assert.False(data.IsMinor(Lad, now, Day, cfg));
            Assert.False(data.IsMinor(Tove, now, Day, cfg));
            Assert.Equal(1, data.MinorCount(now, Day, cfg));
            Assert.Equal(5, data.AdultCount(now, Day, cfg));
            Assert.Equal(0, data.MinorCount(15000.0 + 11.0 * Day, Day, cfg));
        }

        [Fact]
        public void LosingTheCarrierCancelsThePregnancyAndWidowsThePartner()
        {
            SettlementData data = FamilySample();
            double now = 15000.0 + 1.0 * Day;

            Assert.True(data.RemoveSettler(Tove));
            FamilyCleanupResult result = data.FamilyCleanup(Tove, now, Day, Cfg());

            Assert.True(result.Changed);
            Assert.True(result.PregnancyCancelled);
            Assert.Equal(new long[] { Ulf }, result.WidowedUids);
            Assert.Equal(new long[] { Baby }, result.OrphanedUids);
            Assert.Empty(result.BereavedUids);
            Assert.Empty(data.Couples);
            Assert.Equal(0, data.PartnerOf(Ulf));
            Assert.Equal(Tove, data.FindChild(Baby).Mother);
            Assert.Equal(Ulf, data.FindChild(Baby).Father);
            Assert.Equal(Tove, data.FindChild(Lad).Mother);
            Assert.False(data.HasMood(Tove, MoodKind.NewParent, now - 1.0));
            Assert.Single(data.Courtships);
        }

        [Fact]
        public void LosingTheOtherPartnerKeepsThePregnancy()
        {
            SettlementData data = FamilySample();
            double now = 15000.0 + 1.0 * Day;

            FamilyCleanupResult result = data.FamilyCleanup(Ulf, now, Day, Cfg());

            Assert.False(result.PregnancyCancelled);
            Assert.Equal(new long[] { Tove }, result.WidowedUids);
            Assert.Equal(new long[] { Baby }, result.OrphanedUids);
            Couple couple = Assert.Single(data.Couples);
            Assert.True(couple.IsExpecting);
            Assert.Equal(Tove, couple.Carrier);
            Assert.Equal(0, couple.Other(Tove));
            Assert.Equal(0, data.PartnerOf(Tove));
            Assert.Null(data.FindCouple(Ulf));
            Assert.Equal(Ulf, data.FindChild(Baby).Father);
        }

        [Fact]
        public void LosingAChildBereavesTheParentsAndFreesTheChildSlot()
        {
            SettlementData data = FamilySample();

            FamilyCleanupResult result = data.FamilyCleanup(Baby, 6000.0, Day, Cfg());

            Assert.Equal(new long[] { Tove, Ulf }, result.BereavedUids);
            Assert.Empty(result.WidowedUids);
            Assert.Empty(result.OrphanedUids);
            Assert.Null(data.FindChild(Baby));
            Assert.Equal(0, data.FindCouple(Tove).ChildrenBorn);
            Assert.NotNull(data.FindChild(Lad));
        }

        [Fact]
        public void LosingACourtingSettlerDropsTheCourtship()
        {
            SettlementData data = FamilySample();

            FamilyCleanupResult result = data.FamilyCleanup(Runa, 9000.0, Day, Cfg());

            Assert.True(result.Changed);
            Assert.Empty(data.Courtships);
            Assert.Empty(result.WidowedUids);
            Assert.Single(data.Moods);
            Assert.False(data.FamilyCleanup(0, 9000.0, Day, Cfg()).Changed);
            Assert.False(data.FamilyCleanup(999, 9000.0, Day, Cfg()).Changed);
        }

        [Fact]
        public void MoodsReplacePruneAndStayBounded()
        {
            var data = new SettlementData();

            data.AddMood(Tove, MoodKind.InLove, 100.0);
            data.AddMood(Tove, MoodKind.InLove, 200.0);
            data.AddMood(Tove, MoodKind.Grief, 50.0);
            Assert.Equal(2, data.Moods.Count);
            Assert.True(data.HasMood(Tove, MoodKind.InLove, 150.0));
            Assert.False(data.HasMood(Tove, MoodKind.InLove, 200.0));
            Assert.False(data.HasMood(Eir, MoodKind.InLove, 150.0));

            Assert.True(data.PruneMoods(60.0));
            Assert.False(data.PruneMoods(60.0));
            Mood left = Assert.Single(data.Moods);
            Assert.Equal(MoodKind.InLove, left.Kind);

            for (int i = 0; i < 300; i++)
            {
                data.AddMood(1000 + i, MoodKind.Grief, 1000.0 + i);
            }
            Assert.Equal(SettlementData.MaxMoods, data.Moods.Count);
            // The oldest went first: Tove's mood (until 200) and the earliest grief ones.
            Assert.False(data.HasMood(Tove, MoodKind.InLove, 150.0));
            Assert.False(data.HasMood(1000, MoodKind.Grief, 0.0));
            Assert.True(data.HasMood(1299, MoodKind.Grief, 0.0));
            Assert.Equal(SettlementData.MaxMoods, SettlementData.Deserialize(data.Serialize()).Moods.Count);
        }

        [Fact]
        public void TooManyFamilyRecordsAreLeftAlone()
        {
            var package = new ZPackage();
            package.Write(7);
            package.Write("Flood");
            package.Write(0);
            package.Write(0);
            package.Write(0);
            package.Write(0);
            package.Write((byte)0);
            package.Write(SettlementData.MaxCouples + 1);

            Assert.Null(SettlementData.Deserialize(package.GetArray()));
        }

        private static void WriteMember(ZPackage package, long id, string name, int rank)
        {
            package.Write(id);
            package.Write(name);
            package.Write(rank);
        }
    }
}
