using AgeOfJarls.Settlement;
using Xunit;

namespace AgeOfJarls.Tests
{
    /// <summary>Who may give whom which rank (SettlementData.RankChangeProblem), and what each rank allows.</summary>
    public class RankTests
    {
        private const int MaxJarls = 2;
        private const long Founder = 1;
        private const long Friend = 2;
        private const long Steward = 3;
        private const long Guard = 4;
        private const long Free = 5;
        private const long Stranger = 6;

        private static SettlementData Settlement()
        {
            var data = new SettlementData();
            data.SetRank(Founder, "Ragnar", SettlementRole.Jarl);
            data.SetRank(Steward, "Floki", SettlementRole.Hersir);
            data.SetRank(Guard, "Torvi", SettlementRole.Huskarl);
            data.SetRank(Free, "Ubbe", SettlementRole.Karl);
            return data;
        }

        [Fact]
        public void JarlAppointsACoJarlWhileThereIsRoom()
        {
            SettlementData data = Settlement();

            Assert.Null(data.RankChangeProblem(Founder, Friend, SettlementRole.Jarl, MaxJarls));
            data.SetRank(Friend, "Lagertha", SettlementRole.Jarl);
            Assert.Equal(2, data.JarlCount);
            Assert.Equal("$aoj_msg_jarls_full", data.RankChangeProblem(Founder, Steward, SettlementRole.Jarl, MaxJarls));
        }

        [Fact]
        public void OnlyTheSeniorJarlDemotesTheOther()
        {
            SettlementData data = Settlement();
            data.SetRank(Friend, "Lagertha", SettlementRole.Jarl);

            Assert.Equal(Founder, data.Jarl.PlayerId);
            Assert.Equal("$aoj_msg_rank_senior", data.RankChangeProblem(Friend, Founder, SettlementRole.Hersir, MaxJarls));
            Assert.Null(data.RankChangeProblem(Founder, Friend, SettlementRole.Hersir, MaxJarls));
        }

        [Fact]
        public void TheLastJarlCannotStepDown()
        {
            SettlementData data = Settlement();

            Assert.Equal("$aoj_msg_last_jarl", data.RankChangeProblem(Founder, Founder, SettlementRole.Hersir, MaxJarls));
            data.SetRank(Friend, "Lagertha", SettlementRole.Jarl);
            Assert.Null(data.RankChangeProblem(Founder, Founder, SettlementRole.Hersir, MaxJarls));
        }

        [Fact]
        public void HersirManagesOnlyTheRanksBelowIt()
        {
            SettlementData data = Settlement();

            Assert.Null(data.RankChangeProblem(Steward, Stranger, SettlementRole.Karl, MaxJarls));
            Assert.Null(data.RankChangeProblem(Steward, Free, SettlementRole.Huskarl, MaxJarls));
            Assert.Null(data.RankChangeProblem(Steward, Guard, SettlementRole.Guest, MaxJarls));
            Assert.Equal("$aoj_msg_rank_denied", data.RankChangeProblem(Steward, Free, SettlementRole.Hersir, MaxJarls));
            Assert.NotNull(data.RankChangeProblem(Steward, Founder, SettlementRole.Karl, MaxJarls));
        }

        [Fact]
        public void HuskarlAndKarlHandOutNoRanks()
        {
            SettlementData data = Settlement();

            Assert.Equal("$aoj_msg_rank_denied", data.RankChangeProblem(Guard, Stranger, SettlementRole.Karl, MaxJarls));
            Assert.Equal("$aoj_msg_rank_denied", data.RankChangeProblem(Free, Stranger, SettlementRole.Karl, MaxJarls));
            Assert.Equal("$aoj_msg_rank_denied", data.RankChangeProblem(Stranger, Stranger, SettlementRole.Karl, MaxJarls));
        }

        [Fact]
        public void MembersMayLeaveButNotPromoteThemselves()
        {
            SettlementData data = Settlement();

            Assert.Equal("$aoj_msg_rank_denied", data.RankChangeProblem(Free, Free, SettlementRole.Huskarl, MaxJarls));
            Assert.Null(data.RankChangeProblem(Free, Free, SettlementRole.Guest, MaxJarls));
            data.SetRank(Free, "", SettlementRole.Guest);
            Assert.Equal(SettlementRole.Guest, data.RoleOf(Free));
            Assert.DoesNotContain(data.Members, m => m.PlayerId == Free);
        }

        [Fact]
        public void TheSameRankIsNoChange()
        {
            Assert.Equal("$aoj_msg_rank_same", Settlement().RankChangeProblem(Founder, Steward, SettlementRole.Hersir, MaxJarls));
        }

        [Fact]
        public void HandingOverPassesTheSeniorSeat()
        {
            SettlementData data = Settlement();

            Assert.True(data.HandOver(Founder, Stranger, "Bjorn"));
            Assert.Equal(Stranger, data.Jarl.PlayerId);
            Assert.Equal(0, data.Members.FindIndex(m => m.PlayerId == Stranger));
            Assert.Equal(SettlementRole.Hersir, data.RoleOf(Founder));
            Assert.Equal(1, data.JarlCount);
            Assert.False(data.HandOver(Founder, Guard, "Torvi"));
        }

        [Fact]
        public void TheMemberListHasALimit()
        {
            SettlementData data = Settlement();
            for (long id = 100; data.Members.Count < 64; id++)
            {
                data.SetRank(id, "Karl " + id, SettlementRole.Karl);
            }

            Assert.Equal("$aoj_msg_rank_full", data.RankChangeProblem(Founder, Stranger, SettlementRole.Karl, MaxJarls));
            Assert.Null(data.RankChangeProblem(Founder, 100, SettlementRole.Huskarl, MaxJarls));
        }

        [Fact]
        public void NamesFromTheNetworkAreCleaned()
        {
            SettlementData data = Settlement();

            data.SetRank(Stranger, "<color=red>$aoj_evil</color>", SettlementRole.Karl);

            string name = data.Members.Find(m => m.PlayerId == Stranger).Name;
            Assert.DoesNotContain("<", name);
            Assert.DoesNotContain("$", name);
        }

        // Karl lives there, Huskarl leads the troop, Hersir runs the settlement, Jarl rules it.
        [Theory]
        [InlineData(0, false, false, false, false)]
        [InlineData(1, true, false, false, false)]
        [InlineData(2, true, true, false, false)]
        [InlineData(3, true, true, true, false)]
        [InlineData(4, true, true, true, true)]
        public void RightsGrowWithTheRank(int rank, bool live, bool military, bool manage, bool rule)
        {
            var role = (SettlementRole)rank;

            Assert.Equal(live, role.Allows(SettlementRight.Live));
            Assert.Equal(military, role.Allows(SettlementRight.Military));
            Assert.Equal(manage, role.Allows(SettlementRight.Manage));
            Assert.Equal(rule, role.Allows(SettlementRight.Rule));
        }
    }
}
