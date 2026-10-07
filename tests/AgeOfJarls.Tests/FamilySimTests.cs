using AgeOfJarls.Family;
using AgeOfJarls.Settlement;
using UnityEngine;
using Xunit;

namespace AgeOfJarls.Tests
{
    /// <summary>Pure pairing, separation and bed decisions, without a running Unity world.</summary>
    public class FamilySimTests
    {
        private const double Day = 1200;
        private const double Now = 20 * Day;

        private static SettlementData Residents()
        {
            var data = new SettlementData();
            for (long uid = 1; uid <= 4; uid++) data.TryAddSettler(uid, "Resident " + uid, int.MaxValue);
            return data;
        }

        private static string Problem(SettlementData data, bool sameSex = false, FamilyConfig cfg = null) =>
            FamilySim.PairProblem(data, 1, 2, Now, Day, cfg ?? new FamilyConfig(), true, sameSex);

        [Fact]
        public void EligibleSinglesMayPairInEitherOrder()
        {
            SettlementData data = Residents();
            Assert.Null(Problem(data));
            Assert.Null(FamilySim.PairProblem(data, 2, 1, Now, Day, new FamilyConfig(), false, true));
            Assert.Equal("$aoj_match_not_here", FamilySim.PairProblem(data, 1, 1, Now, Day, new FamilyConfig(), true, false));
            data.RemoveSettler(2);
            Assert.Equal("$aoj_match_not_here", Problem(data));
        }

        [Theory]
        [InlineData(2.0)]
        [InlineData(6.0)]
        [InlineData(9.99)]
        public void MinorsCannotBeMatched(double age)
        {
            SettlementData data = Residents();
            data.Children.Add(new ChildRecord { Uid = 2, Born = Now - age * Day });
            Assert.Equal("$aoj_match_not_adult", Problem(data));
            data.Children[0].Born = Now - 10 * Day;
            Assert.Null(Problem(data));
        }

        [Fact]
        public void CouplesPromisesAndWidowedCarriersAreTaken()
        {
            SettlementData data = Residents();
            data.Couples.Add(new Couple { A = 1, B = 3 });
            Assert.Equal("$aoj_match_taken", Problem(data));
            data.Couples.Clear();
            data.Courtships.Add(new Courtship { A = 2, B = 4 });
            Assert.Equal("$aoj_match_taken", Problem(data));
            data.Courtships.Clear();
            data.Couples.Add(new Couple { A = 0, B = 2, Carrier = 2, DueAt = Now + Day });
            Assert.Equal("$aoj_match_taken", Problem(data));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void SharedParentsAndParentChildPairsAreRefused(bool siblings)
        {
            SettlementData data = Residents();
            data.Children.Add(new ChildRecord { Uid = 2, Born = Day, Mother = siblings ? 3 : 1 });
            if (siblings) data.Children.Add(new ChildRecord { Uid = 1, Born = Day, Mother = 3 });
            Assert.Equal("$aoj_match_related", Problem(data));
        }

        [Fact]
        public void PairingAndGriefUseCurrentRulesAndExpiry()
        {
            SettlementData data = Residents();
            Assert.Equal("$aoj_match_same_sex", Problem(data, true));
            Assert.Null(Problem(data, true, new FamilyConfig { Pairing = FamilyPairing.Any }));
            data.AddMood(2, MoodKind.Grief, Now + 1);
            Assert.Equal("$aoj_match_grieving", Problem(data));
            data.Moods[0].Until = Now;
            Assert.Null(Problem(data));
        }

        [Fact]
        public void StartingCourtshipIsIdempotentAndReservesBothResidents()
        {
            SettlementData data = Residents();
            Assert.True(FamilySim.StartCourtship(data, 1, 2, Now));
            Assert.False(FamilySim.StartCourtship(data, 2, 1, Now + 10));
            Assert.False(FamilySim.StartCourtship(data, 3, 2, Now + 10));
            Assert.Equal(Now, Assert.Single(data.Courtships).StartedAt);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public void SeparationRetainsPregnancyAndChildrenForEitherCarrier(long carrier)
        {
            SettlementData data = Residents();
            data.Children.Add(new ChildRecord { Uid = 3, Mother = 1, Father = 2, Born = Day });
            var couple = new Couple { A = 1, B = 2, Carrier = carrier, DueAt = Now + Day, ConceivedAt = Now, ChildrenBorn = 1 };
            couple.SetPregnancy(carrier, Now, Now + Day);
            data.Couples.Add(couple);
            Assert.True(FamilySim.SeparateRecord(data, 2));
            Assert.Same(couple, Assert.Single(data.Couples));
            Assert.Equal(0, data.PartnerOf(carrier));
            Assert.Equal(0, couple.Other(carrier));
            Assert.Equal(Now + Day, couple.DueAt);
            Assert.Equal(carrier, couple.Carrier);
            Assert.Equal(1, data.Children[0].Mother);
            Assert.Equal(2, data.Children[0].Father);
            Assert.False(FamilySim.SeparateRecord(data, carrier));
            SettlementData restored = SettlementData.Deserialize(data.Serialize());
            Assert.Equal(couple.DueAt, Assert.Single(restored.Couples).DueAt);
            Assert.Equal(carrier, restored.Couples[0].Carrier);
            Assert.Equal(carrier, restored.Couples[0].ConceptionMother);
            Assert.Equal(carrier == 1 ? 2 : 1, restored.Couples[0].ConceptionFather);
        }

        [Fact]
        public void SeparationWithoutPregnancyDropsOnlyTheCouple()
        {
            SettlementData data = Residents();
            data.Couples.Add(new Couple { A = 1, B = 2 });
            data.Children.Add(new ChildRecord { Uid = 3, Mother = 1, Father = 2, Born = Day });
            Assert.True(FamilySim.SeparateRecord(data, 1));
            Assert.Empty(data.Couples);
            Assert.Equal(1, Assert.Single(data.Children).Mother);
            Assert.Equal(4, data.Settlers.Count);
        }

        [Fact]
        public void BedDemandCountsYouthsAndAdultsAndBothSharers()
        {
            SettlementData data = Residents();
            data.Children.Add(new ChildRecord { Uid = 1, Born = Now - Day });
            data.Children.Add(new ChildRecord { Uid = 2, Born = Now - 3 * Day });
            data.Children.Add(new ChildRecord { Uid = 3, Born = Now - 6 * Day });
            foreach (RosterEntry entry in data.Settlers) entry.HasBed = true;
            Assert.Equal(2, data.CountBedResidents(false, Now, Day, new FamilyConfig()));
            Assert.Equal(2, data.CountBedResidents(true, Now, Day, new FamilyConfig()));
            data.FindSettler(3).HasBed = false;
            Assert.Equal(1, data.CountBedResidents(true, Now, Day, new FamilyConfig()));
        }

        [Fact]
        public void SharedBedSidesAndParentBedPriorityAreStable()
        {
            SettlementData data = Residents();
            RosterEntry mother = data.FindSettler(1);
            RosterEntry father = data.FindSettler(2);
            mother.HasBed = father.HasBed = true;
            mother.BedPosition = father.BedPosition = new Vector3(5, 2, 8);
            data.Children.Add(new ChildRecord { Uid = 3, Mother = 1, Father = 2 });
            Assert.Equal(-0.45f, JarlTable.BedSideOffset(data, 1));
            Assert.Equal(0.45f, JarlTable.BedSideOffset(data, 2));
            father.BedPosition = new Vector3(9, 2, 8);
            Assert.True(JarlTable.TryGetParentBed(data, 3, out Vector3 position));
            Assert.Equal(mother.BedPosition, position);
            mother.HasBed = false;
            Assert.Equal(0f, JarlTable.BedSideOffset(data, 2));
            Assert.True(JarlTable.TryGetParentBed(data, 3, out position));
            Assert.Equal(father.BedPosition, position);
            father.HasBed = false;
            Assert.False(JarlTable.TryGetParentBed(data, 3, out position));
        }
    }
}
