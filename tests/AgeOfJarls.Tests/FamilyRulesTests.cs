using AgeOfJarls.Family;
using AgeOfJarls.Settlers;
using AgeOfJarls.Work;
using Xunit;

namespace AgeOfJarls.Tests
{
    /// <summary>The pure rules of growing up: every machine derives the same stage and abilities from the birth time.</summary>
    public class FamilyRulesTests
    {
        private const double Day = 1200.0;

        /// <summary>Infant 2 days, Child 4, Youth 4: adult after 10 days.</summary>
        private static FamilyConfig Cfg() => new FamilyConfig { InfantDays = 2f, ChildDays = 4f, YouthDays = 4f, YouthWorkPace = 0.7f };

        [Theory]
        [InlineData(0.0, LifeStage.Infant)]
        [InlineData(1.99, LifeStage.Infant)]
        [InlineData(2.0, LifeStage.Child)]
        [InlineData(5.99, LifeStage.Child)]
        [InlineData(6.0, LifeStage.Youth)]
        [InlineData(9.99, LifeStage.Youth)]
        [InlineData(10.0, LifeStage.Adult)]
        [InlineData(500.0, LifeStage.Adult)]
        public void StageThresholdsAreInclusiveAtTheStart(double days, LifeStage expected)
        {
            Assert.Equal(expected, FamilyRules.StageFor(days * Day, Day, Cfg()));
        }

        [Fact]
        public void SettlersWithoutBirthTimeAreAdults()
        {
            Assert.Equal(LifeStage.Adult, FamilyRules.StageAt(0.0, 100.0, Day, Cfg()));
            Assert.Equal(LifeStage.Adult, FamilyRules.StageAt(-5.0, 100.0, Day, Cfg()));
            Assert.Equal(LifeStage.Infant, FamilyRules.StageAt(100.0, 200.0, Day, Cfg()));
        }

        [Fact]
        public void ProgressRunsFromZeroToOneWithinEachStage()
        {
            FamilyConfig cfg = Cfg();
            Assert.Equal(0f, FamilyRules.StageProgress(0.0, Day, cfg), 3);
            Assert.Equal(0.5f, FamilyRules.StageProgress(1.0 * Day, Day, cfg), 3);
            Assert.Equal(0f, FamilyRules.StageProgress(2.0 * Day, Day, cfg), 3);
            Assert.Equal(0.25f, FamilyRules.StageProgress(3.0 * Day, Day, cfg), 3);
            Assert.Equal(0.5f, FamilyRules.StageProgress(8.0 * Day, Day, cfg), 3);
            Assert.Equal(1f, FamilyRules.StageProgress(10.0 * Day, Day, cfg), 3);
            Assert.Equal(4.0 * Day, FamilyRules.SecondsToNextStage(6.0 * Day, Day, cfg), 3);
            Assert.Equal(0.0, FamilyRules.SecondsToNextStage(12.0 * Day, Day, cfg), 3);
        }

        // The model grows without a jump: the end of one stage is the start of the next, from half size to full.
        [Fact]
        public void ScaleGrowsMonotonicallyFromHalfToFull()
        {
            FamilyConfig cfg = Cfg();
            float previous = 0f;
            for (double days = 0.0; days <= 11.0; days += 0.1)
            {
                double age = days * Day;
                LifeStage stage = FamilyRules.StageFor(age, Day, cfg);
                float scale = FamilyRules.ScaleFor(stage, FamilyRules.StageProgress(age, Day, cfg));
                Assert.True(scale >= previous - 0.0001f, $"scale fell at day {days}");
                Assert.InRange(scale, 0.5f, 1f);
                previous = scale;
            }
            Assert.Equal(0.5f, FamilyRules.ScaleFor(LifeStage.Infant, 0f), 3);
            Assert.Equal(0.65f, FamilyRules.ScaleFor(LifeStage.Infant, 1f), 3);
            Assert.Equal(0.65f, FamilyRules.ScaleFor(LifeStage.Child, 0f), 3);
            Assert.Equal(0.85f, FamilyRules.ScaleFor(LifeStage.Youth, 0f), 3);
            Assert.Equal(1f, FamilyRules.ScaleFor(LifeStage.Youth, 1f), 3);
            Assert.Equal(1f, FamilyRules.ScaleFor(LifeStage.Adult, 0.3f), 3);
        }

        [Fact]
        public void SpeedAndHealthFollowTheScale()
        {
            Assert.Equal(0.55f, FamilyRules.SpeedFactor(0.5f), 3);
            Assert.Equal(0.8f, FamilyRules.SpeedFactor(0.8f), 3);
            Assert.Equal(1f, FamilyRules.SpeedFactor(1.2f), 3);
            Assert.Equal(0.7f, FamilyRules.HealthFactor(0.7f), 3);
        }

        [Fact]
        public void AdulthoodRefreshesEffectsEvenWhenRemainingGrowthIsBelowTolerance()
        {
            FamilyConfig cfg = Cfg();
            double age = cfg.DaysToAdult * Day - 96.0;
            LifeStage stage = FamilyRules.StageFor(age, Day, cfg);
            float scale = FamilyRules.ScaleFor(stage, FamilyRules.StageProgress(age, Day, cfg));
            Assert.Equal(LifeStage.Youth, stage);
            Assert.Equal(0.997f, scale, 3);
            Assert.False(Settler.NeedsStageRefresh(stage, stage, 1f, scale));
            Assert.True(Settler.NeedsStageRefresh(LifeStage.Adult, stage, 1f, scale));
            Assert.False(Settler.NeedsStageRefresh(LifeStage.Adult, LifeStage.Adult, 1f, 1f));
        }

        [Theory]
        [InlineData(LifeStage.Infant, LifeStage.Child, 0.65f)]
        [InlineData(LifeStage.Child, LifeStage.Youth, 0.85f)]
        [InlineData(LifeStage.Youth, LifeStage.Adult, 1f)]
        public void EveryStageBoundaryRefreshesEffectsWithoutAScaleJump(LifeStage previous, LifeStage current, float scale)
        {
            Assert.True(Settler.NeedsStageRefresh(current, previous, scale, scale));
        }

        [Fact]
        public void UnappliedStageAndSignificantGrowthRefreshEffects()
        {
            Assert.True(Settler.NeedsStageRefresh(LifeStage.Infant, null, 0.5f, 0.5f));
            Assert.True(Settler.NeedsStageRefresh(LifeStage.Child, LifeStage.Child, 0.70f, 0.69f));
            Assert.False(Settler.NeedsStageRefresh(LifeStage.Child, LifeStage.Child, 0.70f, 0.699f));
        }

        [Theory]
        [InlineData(LifeStage.Infant, JobType.Woodcutter, false)]
        [InlineData(LifeStage.Child, JobType.Cook, false)]
        [InlineData(LifeStage.Youth, JobType.Woodcutter, true)]
        [InlineData(LifeStage.Youth, JobType.Hauler, true)]
        [InlineData(LifeStage.Youth, JobType.Farmer, true)]
        [InlineData(LifeStage.Youth, JobType.Cook, true)]
        [InlineData(LifeStage.Youth, JobType.Miner, false)]
        [InlineData(LifeStage.Youth, JobType.Smelter, false)]
        [InlineData(LifeStage.Youth, JobType.Builder, false)]
        [InlineData(LifeStage.Adult, JobType.Miner, true)]
        [InlineData(LifeStage.Adult, JobType.Builder, true)]
        public void YouthsDoOnlyLightJobs(LifeStage stage, JobType job, bool allowed)
        {
            Assert.Equal(allowed, FamilyRules.CanWorkAt(stage, job));
        }

        [Fact]
        public void WorkFightingPaceAndBedsDependOnTheStage()
        {
            Assert.False(FamilyRules.CanWork(LifeStage.Infant));
            Assert.False(FamilyRules.CanWork(LifeStage.Child));
            Assert.True(FamilyRules.CanWork(LifeStage.Youth));
            Assert.True(FamilyRules.CanWork(LifeStage.Adult));

            Assert.False(FamilyRules.CanFight(LifeStage.Youth));
            Assert.True(FamilyRules.CanFight(LifeStage.Adult));

            Assert.Equal(0.7f, FamilyRules.PaceFor(LifeStage.Youth, 0.7f), 3);
            Assert.Equal(1f, FamilyRules.PaceFor(LifeStage.Adult, 0.7f), 3);
            Assert.Equal(1f, FamilyRules.PaceFor(LifeStage.Child, 0.7f), 3);

            Assert.False(FamilyRules.NeedsOwnBed(LifeStage.Infant));
            Assert.False(FamilyRules.NeedsOwnBed(LifeStage.Child));
            Assert.True(FamilyRules.NeedsOwnBed(LifeStage.Youth));
            Assert.True(FamilyRules.NeedsOwnBed(LifeStage.Adult));

            Assert.Equal(2f, FamilyRules.WanderRangeFor(LifeStage.Infant, 12f));
            Assert.Equal(4f, FamilyRules.WanderRangeFor(LifeStage.Child, 12f));
            Assert.Equal(12f, FamilyRules.WanderRangeFor(LifeStage.Youth, 12f));
            Assert.Equal(12f, FamilyRules.WanderRangeFor(LifeStage.Adult, 12f));
        }

        [Fact]
        public void AppetiteGrowsWithAge()
        {
            Assert.Equal(0.3f, FamilyRules.AppetiteFor(LifeStage.Infant), 3);
            Assert.Equal(0.6f, FamilyRules.AppetiteFor(LifeStage.Child), 3);
            Assert.Equal(0.9f, FamilyRules.AppetiteFor(LifeStage.Youth), 3);
            Assert.Equal(1f, FamilyRules.AppetiteFor(LifeStage.Adult), 3);
        }

        [Theory]
        [InlineData("Ragnar", false, "Ragnarsson")]
        [InlineData("Ragnar", true, "Ragnarsdottir")]
        [InlineData("Astrid", true, "Astridsdottir")]
        [InlineData("", false, "")]
        [InlineData(null, true, "")]
        public void PatronymicComesFromTheParentsFirstName(string parent, bool female, string expected)
        {
            Assert.Equal(expected, FamilyRules.Patronymic(parent, female));
        }

        [Fact]
        public void StageTokensAreGendered()
        {
            Assert.Equal("$aoj_stage_infant", FamilyRules.StageToken(LifeStage.Infant, false));
            Assert.Equal("$aoj_stage_child_f", FamilyRules.StageToken(LifeStage.Child, true));
            Assert.Equal("$aoj_stage_youth", FamilyRules.StageToken(LifeStage.Youth, false));
            Assert.Equal("$aoj_stage_adult_f", FamilyRules.StageToken(LifeStage.Adult, true));
        }
    }
}
