using AgeOfJarls.AI;
using AgeOfJarls.Settlement;
using UnityEngine;
using Xunit;

namespace AgeOfJarls.Tests
{
    /// <summary>How far a settlement reaches: Work Totems outside it, and where its workers still count as at home.</summary>
    public class SettlementReachTests
    {
        [Fact]
        public void EdgeGapIsNegativeInside()
        {
            Assert.Equal(-20.0, JarlTable.EdgeGap(Vector3.zero, 30f, new Vector3(10f, 0f, 0f)), 3);
            Assert.Equal(0.0, JarlTable.EdgeGap(Vector3.zero, 30f, new Vector3(0f, 0f, 30f)), 3);
        }

        [Fact]
        public void EdgeGapIsMeasuredOnTheGround()
        {
            // A totem on a hill 25 m above the table's level is no farther out for it.
            Assert.Equal(20.0, JarlTable.EdgeGap(Vector3.zero, 30f, new Vector3(0f, 25f, 50f)), 3);
        }

        [Fact]
        public void HomeReachesOutToTheFarEdgeOfAnOutsideTotemsZone()
        {
            // A totem 70 m from the table with a 20 m zone: its workers are at home out to 90 m, and a margin.
            Assert.Equal(95.0, HomeRoutine.HomeReach(30f, 70f + 20f), 3);
        }

        [Fact]
        public void HomeIsTheSettlementWhenTheWorkLiesInside()
        {
            Assert.Equal(35.0, HomeRoutine.HomeReach(30f, 10f + 15f), 3);
            Assert.Equal(35.0, HomeRoutine.HomeReach(30f, 0f), 3);
        }
    }
}
