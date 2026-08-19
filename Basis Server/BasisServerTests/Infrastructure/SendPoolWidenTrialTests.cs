using BasisNetworkServer.BasisNetworkingReductionSystem;
using Xunit;

namespace BasisServerTests;

/// <summary>
/// Covers the one property the send pool's width controller cannot hold on its own: that adding a
/// worker has to make the pool faster to keep it.
///
/// The estimator it guards is stable only while the pass scales. Its target works out to
/// (busyMs x workers) / budgetMs, so a widening that leaves the pass just as slow raises the very
/// number that asked for it and the next step asks for more again — the pool climbs to its ceiling
/// while throughput falls, which is what "we added a core and it got slower" looks like from
/// inside. Everything here is the verdict that stops that, and the conditions under which the
/// verdict is allowed to expire.
///
/// Shares the reduction system's statics, hence the collection.
/// </summary>
[Collection("Basis reduction statics")]
public class SendPoolWidenTrialTests
{
    private const int SteadyPlayers = 1000;

    [Fact]
    public void WideningThatLosesThroughputIsGivenBack()
    {
        try
        {
            BasisServerReductionSystemEvents.TestOnly_ResetPoolTuning(8);

            int width = BasisServerReductionSystemEvents.TestOnly_ResolveWidenTrial(
                from: 8, current: 16, rateBefore: 1000, rateAfter: 900, playerCount: SteadyPlayers);

            Assert.Equal(8, width);
            Assert.Equal(8, BasisServerReductionSystemEvents.TestOnly_SendWorkers);
            Assert.Equal(8, BasisServerReductionSystemEvents.TestOnly_LearnedWidthCeiling);
        }
        finally
        {
            BasisServerReductionSystemEvents.TestOnly_ResetPoolTuning(4);
        }
    }

    [Fact]
    public void WideningThatPaysIsKept()
    {
        try
        {
            BasisServerReductionSystemEvents.TestOnly_ResetPoolTuning(8);

            int width = BasisServerReductionSystemEvents.TestOnly_ResolveWidenTrial(
                from: 8, current: 16, rateBefore: 1000, rateAfter: 1400, playerCount: SteadyPlayers);

            Assert.Equal(16, width);
            Assert.Equal(16, BasisServerReductionSystemEvents.TestOnly_SendWorkers);
            Assert.Equal(0, BasisServerReductionSystemEvents.TestOnly_LearnedWidthCeiling);
        }
        finally
        {
            BasisServerReductionSystemEvents.TestOnly_ResetPoolTuning(4);
        }
    }

    /// <summary>
    /// Flat is a loss, not a draw: the extra worker is a core spent for nothing, and keeping it
    /// leaves the pool one step closer to the width where throughput actually falls.
    /// </summary>
    [Fact]
    public void WideningThatChangesNothingIsGivenBack()
    {
        try
        {
            BasisServerReductionSystemEvents.TestOnly_ResetPoolTuning(8);

            int width = BasisServerReductionSystemEvents.TestOnly_ResolveWidenTrial(
                from: 8, current: 16, rateBefore: 1000, rateAfter: 1010, playerCount: SteadyPlayers);

            Assert.Equal(8, width);
            Assert.Equal(8, BasisServerReductionSystemEvents.TestOnly_LearnedWidthCeiling);
        }
        finally
        {
            BasisServerReductionSystemEvents.TestOnly_ResetPoolTuning(4);
        }
    }

    /// <summary>
    /// Nothing timed at the old width is not evidence against the new one. Judging on it would pin
    /// a freshly started server at whatever width it happened to boot with.
    /// </summary>
    [Fact]
    public void WideningWithNothingToCompareAgainstStands()
    {
        try
        {
            BasisServerReductionSystemEvents.TestOnly_ResetPoolTuning(8);

            int width = BasisServerReductionSystemEvents.TestOnly_ResolveWidenTrial(
                from: 8, current: 16, rateBefore: 0, rateAfter: 1200, playerCount: SteadyPlayers);

            Assert.Equal(16, width);
            Assert.Equal(0, BasisServerReductionSystemEvents.TestOnly_LearnedWidthCeiling);
        }
        finally
        {
            BasisServerReductionSystemEvents.TestOnly_ResetPoolTuning(4);
        }
    }

    [Fact]
    public void LearnedCeilingHoldsWhileThePopulationDoes()
    {
        try
        {
            BasisServerReductionSystemEvents.TestOnly_ResetPoolTuning(8);
            BasisServerReductionSystemEvents.TestOnly_ResolveWidenTrial(
                from: 8, current: 16, rateBefore: 1000, rateAfter: 900, playerCount: SteadyPlayers);

            BasisServerReductionSystemEvents.TestOnly_ExpireLearnedCeiling(SteadyPlayers + 50);

            Assert.Equal(8, BasisServerReductionSystemEvents.TestOnly_LearnedWidthCeiling);
        }
        finally
        {
            BasisServerReductionSystemEvents.TestOnly_ResetPoolTuning(4);
        }
    }

    /// <summary>
    /// The verdict was about one load level. A population a quarter larger is a different question,
    /// so the pool gets to ask it again.
    /// </summary>
    [Theory]
    [InlineData(1400)]
    [InlineData(600)]
    public void LearnedCeilingExpiresWhenThePopulationMoves(int players)
    {
        try
        {
            BasisServerReductionSystemEvents.TestOnly_ResetPoolTuning(8);
            BasisServerReductionSystemEvents.TestOnly_ResolveWidenTrial(
                from: 8, current: 16, rateBefore: 1000, rateAfter: 900, playerCount: SteadyPlayers);

            BasisServerReductionSystemEvents.TestOnly_ExpireLearnedCeiling(players);

            Assert.Equal(0, BasisServerReductionSystemEvents.TestOnly_LearnedWidthCeiling);
        }
        finally
        {
            BasisServerReductionSystemEvents.TestOnly_ResetPoolTuning(4);
        }
    }

    [Fact]
    public void LearnedCeilingExpiresOnceItIsOldEnough()
    {
        try
        {
            BasisServerReductionSystemEvents.TestOnly_ResetPoolTuning(8);
            BasisServerReductionSystemEvents.TestOnly_ResolveWidenTrial(
                from: 8, current: 16, rateBefore: 1000, rateAfter: 900, playerCount: SteadyPlayers);

            BasisServerReductionSystemEvents.TestOnly_AgeLearnedCeiling();
            BasisServerReductionSystemEvents.TestOnly_ExpireLearnedCeiling(SteadyPlayers);

            Assert.Equal(0, BasisServerReductionSystemEvents.TestOnly_LearnedWidthCeiling);
        }
        finally
        {
            BasisServerReductionSystemEvents.TestOnly_ResetPoolTuning(4);
        }
    }
}
