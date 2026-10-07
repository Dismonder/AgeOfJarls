using System.Collections.Generic;
using AgeOfJarls.Family;
using AgeOfJarls.Settlement;
using AgeOfJarls.Work;
using UnityEngine;
using Xunit;

namespace AgeOfJarls.Tests
{
    /// <summary>Family persistence, acceptance, bed sharing and absence-window regression tests.</summary>
    public class FamilyRegressionTests
    {
        private const double Day = 1200;
        private const double Now = 30 * Day;

        [Fact]
        public void PregnancyAncestrySurvivesTheOtherParentsDeathAndReload()
        {
            var data = new SettlementData();
            data.TryAddSettler(1, "Mother", 10);
            data.TryAddSettler(2, "Father", 10);
            var couple = new Couple { A = 1, B = 2 };
            couple.SetPregnancy(1, Now, Now + Day);
            data.Couples.Add(couple);
            data.RemoveSettler(2);
            data.FamilyCleanup(2, Now, Day, new FamilyConfig());
            data = SettlementData.Deserialize(data.Serialize());
            Couple pregnancy = Assert.Single(data.Couples);
            Assert.Equal(0, pregnancy.Other(1));
            Assert.Equal(1, pregnancy.ConceptionMother);
            Assert.Equal(2, pregnancy.ConceptionFather);
            Assert.Equal(Now + Day, pregnancy.DueAt);
        }

        [Fact]
        public void RemarryingRestoresLimitsFromChildrenIncludingAdultsInEitherParentOrder()
        {
            var data = new SettlementData();
            data.TryAddSettler(1, "Mother", 10);
            data.TryAddSettler(2, "Father", 10);
            for (long uid = 3; uid <= 5; uid++)
            {
                data.TryAddSettler(uid, "Child", 10);
                data.Children.Add(new ChildRecord { Uid = uid, Mother = uid == 4 ? 2 : 1,
                    Father = uid == 4 ? 1 : 2, Born = uid * Day });
            }
            data.Children.Add(new ChildRecord { Uid = 6, Mother = 1, Father = 9, Born = Now });
            data.Couples.Add(data.NewCouple(1, 2, Now));
            Assert.True(FamilySim.SeparateRecord(data, 1));
            data = SettlementData.Deserialize(data.Serialize());
            Couple remarried = data.NewCouple(2, 1, Now + Day);
            Assert.Equal(3, remarried.ChildrenBorn);
            Assert.Equal(5 * Day, remarried.LastBirth);
            Assert.False(data.IsMinor(3, Now, Day, new FamilyConfig()));

            data.RemoveSettler(3);
            data.FamilyCleanup(3, Now, Day, new FamilyConfig());
            Assert.Equal(2, data.NewCouple(1, 2, Now).ChildrenBorn);
        }

        [Fact]
        public void BothParentsDeathsPreserveSiblingKinshipAfterReload()
        {
            var data = new SettlementData();
            for (long uid = 1; uid <= 4; uid++) data.TryAddSettler(uid, "Resident", 10);
            data.Children.Add(new ChildRecord { Uid = 3, Mother = 1, Father = 2, Born = Day });
            data.Children.Add(new ChildRecord { Uid = 4, Mother = 1, Father = 2, Born = 2 * Day });
            data.RemoveSettler(1);
            data.FamilyCleanup(1, Now, Day, new FamilyConfig());
            data.RemoveSettler(2);
            data.FamilyCleanup(2, Now, Day, new FamilyConfig());
            data = SettlementData.Deserialize(data.Serialize());
            Assert.True(data.AreRelated(3, 4));
            Assert.Equal(1, data.FindChild(3).Mother);
            Assert.Equal(2, data.FindChild(3).Father);
            Assert.Equal("$aoj_match_related", FamilySim.PairProblem(data, 3, 4, Now, Day, new FamilyConfig(), true, false));
            Assert.False(JarlTable.TryGetParentBed(data, 3, out _));
        }

        [Theory]
        [InlineData(false, true)]
        [InlineData(true, false)]
        public void IncomingYouthUsesItsBirthFactsBeforeCountingCapacity(bool childrenCount, bool accepted)
        {
            var cfg = new FamilyConfig { ChildrenCountTowardLimit = childrenCount };
            var data = new SettlementData();
            data.TryAddSettler(1, "Adult", 1);
            double born = Now - 7 * Day;
            Assert.Equal(accepted, data.TryAcceptSettler(2, "Youth", born, 7, 8, true, 1, Now, Day, cfg));
            if (!accepted)
            {
                Assert.Single(data.Settlers);
                Assert.Empty(data.Children);
                return;
            }
            data = SettlementData.Deserialize(data.Serialize());
            ChildRecord child = Assert.Single(data.Children);
            Assert.Equal(born, child.Born);
            Assert.Equal(7, child.Mother);
            Assert.Equal(8, child.Father);
            Assert.Equal((int)LifeStage.Youth, child.LastStage);
            Assert.True(child.Female);
            Assert.Equal(1, data.AdultCount(Now, Day, cfg));
            Assert.True(data.IsMinor(2, Now, Day, cfg));
            Assert.False(data.TryAcceptSettler(2, "Duplicate", born, 7, 8, true, 1, Now, Day, cfg));
            Assert.False(data.TryAcceptSettler(3, "Adult", 0, 0, 0, false, 1, Now, Day, cfg));
            Assert.Single(data.Children);
        }

        [Theory]
        [InlineData(1000, true, 1100, 1189, false)]
        [InlineData(1000, true, 1100, 1190, true)]
        [InlineData(1100, true, 1100, 1189, false)]
        [InlineData(1200, true, 1100, 1200, true)]
        [InlineData(1200, true, 1100, 1199, false)]
        [InlineData(1000, false, 1100, 1300, false)]
        public void OverdueBirthWaitsForPruningAndRequiresARosterCarrier(double due, bool present, double activated, double now, bool allowed)
        {
            Assert.Equal(allowed, FamilyRules.CanDeliver(due, present, activated, now));
        }

        [Theory]
        [InlineData(1000, 2000, 2200, 0, 600)]
        [InlineData(1600, 2000, 2200, 0, 0)]
        [InlineData(1000, 3500, 0, 1800, 700)]
        [InlineData(1800, 2500, 0, 1800, 0)]
        [InlineData(1000, 3500, 2200, 1800, 200)]
        [InlineData(2000, 1000, 0, 0, 0)]
        public void CatchUpSubtractsRestOverlapWithoutDoubleCounting(double from, double until, double due, double lastBirth, double expected)
        {
            Assert.Equal(expected, FamilyRules.WorkSeconds(0, from, until, JobType.Woodcutter,
                due, lastBirth, Day, new FamilyConfig(), false), 6);
        }

        [Theory]
        [InlineData(JobType.Woodcutter, true, 2040)]
        [InlineData(JobType.Woodcutter, false, 2400)]
        [InlineData(JobType.Miner, true, 1200)]
        [InlineData(JobType.Miner, false, 1200)]
        public void CatchUpSplitsAdulthoodAndWeightsOnlyFallbackRates(JobType job, bool fallback, double expected)
        {
            const double born = 100;
            Assert.Equal(expected, FamilyRules.WorkSeconds(born, born + 9 * Day, born + 11 * Day,
                job, 0, 0, Day, new FamilyConfig(), fallback), 3);
        }

        [Theory]
        [InlineData(1, 2, 0)]
        [InlineData(3, 5, 0)]
        [InlineData(5, 7, 840)]
        [InlineData(7, 8, 840)]
        public void CatchUpExcludesYoungMinorsAndStartsLightWorkAtYouth(double fromDays, double untilDays, double expected)
        {
            const double born = 100;
            Assert.Equal(expected, FamilyRules.WorkSeconds(born, born + fromDays * Day, born + untilDays * Day,
                JobType.Woodcutter, 0, 0, Day, new FamilyConfig(), true), 3);
        }

        [Theory]
        [InlineData(0, 0.1f, 55)]
        [InlineData(10, 0, 100)]
        [InlineData(8, 0, 92.5f)]
        public void SpawnHealthMatchesActualAgeAndInheritedTraits(double ageDays, float traits, float expected)
        {
            Assert.Equal(expected, FamilyRules.SpawnHealth(100, traits, Now - ageDays * Day, Now, Day, new FamilyConfig()), 3);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void BeddedPartnersConsolidateWithoutTakingAnotherResidentsDoubleBed(bool alreadyDouble)
        {
            var data = new SettlementData();
            for (long uid = 1; uid <= 3; uid++) data.TryAddSettler(uid, "Resident", 10);
            data.Couples.Add(new Couple { A = 1, B = 2 });
            Vector3 shared = new Vector3(10, 0, 0);
            Vector3 singleA = new Vector3(20, 0, 0);
            Vector3 singleB = new Vector3(30, 0, 0);
            Vector3 occupied = new Vector3(40, 0, 0);
            data.FindSettler(1).HasBed = data.FindSettler(2).HasBed = data.FindSettler(3).HasBed = true;
            data.FindSettler(1).BedPosition = alreadyDouble ? shared : singleA;
            data.FindSettler(2).BedPosition = singleB;
            data.FindSettler(3).BedPosition = occupied;
            var beds = new List<Vector3> { occupied, shared, singleA, singleB };
            var doubles = new HashSet<Vector3> { occupied, shared };
            Assert.True(JarlTable.ConsolidateBeds(data, beds, doubles, Now, Day, new FamilyConfig()));
            Assert.Equal(shared, data.FindSettler(1).BedPosition);
            Assert.Equal(shared, data.FindSettler(2).BedPosition);
            Assert.Equal(occupied, data.FindSettler(3).BedPosition);
            Assert.Contains(singleB, beds);
            Assert.False(JarlTable.ConsolidateBeds(data, beds, doubles, Now, Day, new FamilyConfig()));
        }
    }
}
