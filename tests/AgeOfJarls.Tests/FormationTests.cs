using AgeOfJarls.AI;
using UnityEngine;
using Xunit;

namespace AgeOfJarls.Tests
{
    public class FormationTests
    {
        [Fact]
        public void EscortSlotsFormPairsOfRows()
        {
            Formation.EscortSlot(0, out float behind0, out float right0);
            Formation.EscortSlot(1, out float behind1, out float right1);
            Formation.EscortSlot(2, out float behind2, out float right2);
            Formation.EscortSlot(3, out float behind3, out float right3);

            Assert.Equal(Formation.BehindDistance, behind0);
            Assert.Equal(behind0, behind1);
            Assert.Equal(Formation.SideDistance, right0);
            Assert.Equal(-Formation.SideDistance, right1);
            Assert.Equal(Formation.BehindDistance + Formation.RowGap, behind2);
            Assert.Equal(behind2, behind3);
            // The second pair walks a little farther out, not in the first pair's backs.
            Assert.Equal(Formation.SideDistance + Formation.OuterRowExtra, right2);
            Assert.Equal(-(Formation.SideDistance + Formation.OuterRowExtra), right3);
        }

        [Fact]
        public void RotateTurnsClockwiseSeenFromAbove()
        {
            Vector3 turned = Formation.Rotate(Vector3.forward, 90f);
            Assert.Equal(1f, turned.x, 3);
            Assert.Equal(0f, turned.z, 3);
        }

        [Fact]
        public void WalkersMeetingHeadOnBothTurnLeft()
        {
            // A walks north; B stands a little to A's right ahead. A turns left (its x goes negative).
            Vector3 steered = Formation.SteerAround(Vector3.forward, new Vector3(0.3f, 0f, 1.5f), 45f);
            Assert.True(steered.x < 0f);
            Assert.True(steered.z > 0f);
            // B walks south and sees A to its own right too: it turns to its left, which is east (+x).
            Vector3 other = Formation.SteerAround(Vector3.back, new Vector3(-0.3f, 0f, -1.5f), 45f);
            Assert.True(other.x > 0f);
        }

        [Fact]
        public void BlocksOnlyInsideTheConeAndReach()
        {
            Assert.True(Formation.Blocks(Vector3.forward, new Vector3(0.2f, 0f, 1.2f), 1.8f, 0.6f));
            Assert.False(Formation.Blocks(Vector3.forward, new Vector3(1.5f, 0f, 0.2f), 1.8f, 0.6f));
            Assert.False(Formation.Blocks(Vector3.forward, new Vector3(0f, 0f, 3f), 1.8f, 0.6f));
            Assert.False(Formation.Blocks(Vector3.forward, Vector3.zero, 1.8f, 0.6f));
        }

        [Fact]
        public void StepsAsideToTheSideItAlreadyStandsOn()
        {
            Vector3 player = Vector3.zero;
            Vector3 velocity = Vector3.forward * 3f;
            Vector3 settlerRight = new Vector3(0.3f, 0f, 2f);
            Vector3 spot = Formation.StepAside(settlerRight, player, velocity, 1.6f);
            Assert.True(spot.x > settlerRight.x + 1f);
            Vector3 settlerLeft = new Vector3(-0.3f, 0f, 2f);
            Vector3 left = Formation.StepAside(settlerLeft, player, velocity, 1.6f);
            Assert.True(left.x < settlerLeft.x - 1f);
        }

        [Fact]
        public void WalksIntoNeedsSpeedDirectionAndRange()
        {
            Vector3 me = new Vector3(0f, 0f, 2f);
            Assert.True(Formation.WalksInto(me, Vector3.zero, Vector3.forward * 2f, 2.6f, 0.8f, 0.6f));
            Assert.False(Formation.WalksInto(me, Vector3.zero, Vector3.forward * 0.3f, 2.6f, 0.8f, 0.6f));
            Assert.False(Formation.WalksInto(me, Vector3.zero, Vector3.right * 2f, 2.6f, 0.8f, 0.6f));
            Assert.False(Formation.WalksInto(me, new Vector3(0f, 0f, -5f), Vector3.forward * 2f, 2.6f, 0.8f, 0.6f));
        }

        [Fact]
        public void LineOfFireCatchesAnAllyBetweenAndNotBeside()
        {
            Vector3 shooter = Vector3.zero;
            Vector3 target = new Vector3(0f, 0f, 10f);
            Assert.True(Formation.InLineOfFire(shooter, target, new Vector3(0.4f, 0f, 5f), 0.9f));
            Assert.False(Formation.InLineOfFire(shooter, target, new Vector3(2f, 0f, 5f), 0.9f));
            Assert.False(Formation.InLineOfFire(shooter, target, new Vector3(0f, 0f, 12f), 0.9f));
            Assert.False(Formation.InLineOfFire(shooter, target, new Vector3(0f, 0f, -1f), 0.9f));
            Assert.Equal(1f, Formation.SidestepSide(shooter, target, new Vector3(-0.4f, 0f, 5f)));
            Assert.Equal(-1f, Formation.SidestepSide(shooter, target, new Vector3(0.4f, 0f, 5f)));
        }
    }
}
