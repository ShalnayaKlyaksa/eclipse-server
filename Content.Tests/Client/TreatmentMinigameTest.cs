using System;
using System.Numerics;
using Content.Client._Eclipse.AdvancedHealth;
using Content.Shared._Eclipse.AdvancedHealth;
using NUnit.Framework;

namespace Content.Tests.Client;

[TestFixture]
public sealed class TreatmentMinigameTest
{
    [Test]
    public void IncisionRequiresFollowingThePathAndRejectsTeleportingToTheEnd()
    {
        var game = new TreatmentMinigameState(AdvancedTreatmentType.SurgicalAccess, 0, true);
        var injuries = 0;
        game.OnInjury += _ => injuries++;
        game.Press(game.IncisionPoint(0));
        Tick(game, 240);
        Assert.That(game.Progress, Is.Zero, "Holding still must not perform an incision.");
        game.Move(game.IncisionPoint(1));
        game.Release();
        Assert.That(game.Success, Is.False);
        Assert.That(injuries, Is.EqualTo(1));
        game.Press(game.IncisionPoint(0));
        for (var i = 1; i <= 180; i++)
        {
            game.Update(1 / 60f);
            game.Move(game.IncisionPoint(i / 180f));
        }
        game.Release();
        Assert.That(game.Success, Is.True);
    }

    [Test]
    public void HemostasisRequiresThreeSeparateStableClamps()
    {
        var game = new TreatmentMinigameState(AdvancedTreatmentType.InternalHemostasis, 0, true);
        game.Press(game.CurrentVessel);
        Tick(game, 20);
        game.Release();
        Assert.That(game.ClampedVessels, Is.Zero, "A brief click cannot seal a vessel.");
        for (var i = 0; i < 3; i++)
        {
            game.Press(game.CurrentVessel);
            Tick(game, 90);
            Assert.That(game.ClampedVessels, Is.EqualTo(i), "Locking requires releasing the clamp.");
            game.Release();
        }
        Assert.That(game.Success, Is.True);
    }

    [Test]
    public void MovingAClampOffTheVesselReportsAnInjury()
    {
        var game = new TreatmentMinigameState(AdvancedTreatmentType.InternalHemostasis, 0, true);
        var injuries = 0;
        game.OnInjury += _ => injuries++;
        game.Press(game.CurrentVessel);
        game.Move(game.CurrentVessel + new Vector2(90, 0));
        Tick(game, 1);
        Assert.That(injuries, Is.EqualTo(1));
        Assert.That(game.Engaged, Is.False);
        Assert.That(game.ClampedVessels, Is.Zero);
    }

    [Test]
    public void SyringeRequiresJabThenHeldInjectionThenWithdrawal()
    {
        var game = new TreatmentMinigameState(AdvancedTreatmentType.Injection, 0, true);
        game.Press(game.PlungerGrip);
        game.Move(game.PlungerGrip + new Vector2(0, 60));
        Tick(game, 300);
        game.Release();
        Assert.That(game.PlungerProgress, Is.Zero);
        Assert.That(game.Success, Is.False);
        InsertNeedle(game);
        var tip = game.SyringeTip;
        game.Press(game.SyringeGrip);
        Tick(game, 180);
        Assert.That(game.PlungerProgress, Is.Zero, "The UI cannot administer a dose without server confirmation.");
        game.SetInjectionProgress(1);
        game.Release();
        Assert.That(game.PlungerProgress, Is.EqualTo(1));
        Assert.That(game.SyringeTip, Is.EqualTo(tip));
        Assert.That(game.Success, Is.False, "An inserted needle must be withdrawn.");
        game.Press(game.SyringeGrip);
        game.Move(game.SyringeGrip - new Vector2(0, 70));
        Assert.That(game.Success, Is.True);
    }

    [Test]
    public void SyringeKeepsPartialDoseAndCannotSlideInsideTissue()
    {
        var game = new TreatmentMinigameState(AdvancedTreatmentType.Injection, 0, true);
        InsertNeedle(game);
        var tip = game.SyringeTip;
        game.Press(game.SyringeGrip);
        game.Move(game.SyringeGrip + new Vector2(8, 0));
        Assert.That(game.NeedleBroken, Is.False);
        game.Release();
        Assert.That(game.SyringeTip.X, Is.EqualTo(tip.X));
        game.Press(game.SyringeGrip);
        Tick(game, 45);
        game.SetInjectionProgress(0.35f);
        game.CancelGrip();
        var partial = game.PlungerProgress;
        Tick(game, 180);
        Assert.That(partial, Is.InRange(0.1f, 0.9f));
        Assert.That(game.PlungerProgress, Is.EqualTo(partial));
        Assert.That(game.Running, Is.True);
        game.Press(game.SyringeGrip);
        game.Move(game.SyringeGrip - new Vector2(0, 70));
        game.Release();
        Assert.That(game.Success, Is.True, "Withdrawing preserves the partial dose confirmed by the server.");
    }

    [Test]
    public void SlowSyringePressureDoesNotPierceButAShortJabDoes()
    {
        var game = new TreatmentMinigameState(AdvancedTreatmentType.Injection, 0, true);
        game.Press(game.SyringeGrip);
        game.Move(game.SyringeGrip - new Vector2(0, 30));
        for (var i = 0; i < 20; i++)
        {
            game.Update(0.05f);
            game.Move(game.SyringeGrip + new Vector2(0, 4));
        }
        Assert.That(game.NeedleInserted, Is.False);
        Assert.That(game.PlungerProgress, Is.Zero);
        game.Move(game.SyringeGrip - new Vector2(0, 50));
        Tick(game, 2);
        game.Move(game.SyringeGrip + new Vector2(0, 90));
        Assert.That(game.NeedleInserted, Is.True);
        Assert.That(game.NeedleKick, Is.EqualTo(1));
    }

    [TestCase(float.NaN, 0)]
    [TestCase(-10, 0)]
    [TestCase(0, 0)]
    [TestCase(11, 0)]
    [TestCase(18, 2.8125f)]
    [TestCase(32, 5)]
    [TestCase(44, 6.5f)]
    [TestCase(1000, 0)]
    public void NeedleDepthControlsBoundedServerFlow(float depth, float expected)
    {
        Assert.That(AdvancedInjectionFlow.Rate(depth), Is.EqualTo(expected).Within(0.0001f));
    }

    private static void InsertNeedle(TreatmentMinigameState game)
    {
        game.Press(game.SyringeGrip);
        game.Move(game.SyringeGrip - new Vector2(70, 30));
        Tick(game, 2);
        game.Move(game.SyringeGrip + new Vector2(0, 80));
        game.Move(game.Cursor - new Vector2(0, 12));
        game.Release();
        Assert.That(game.NeedleInserted, Is.True);
    }

    private static void Pull(TreatmentMinigameState game, Vector2 destination, int frames = 120)
    {
        var start = game.Cursor;
        for (var i = 1; i <= frames && game.Running; i++)
        {
            game.Update(1 / 60f);
            game.Move(Vector2.Lerp(start, destination, (float) i / frames));
        }
    }

    private static void FollowCompression(TreatmentMinigameState game, int frames)
    {
        for (var i = 0; i < frames && game.Running; i++)
        {
            game.Move(game.HoldTarget);
            game.Update(1 / 60f);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void BadExtractionSlipsAndReportsInjuryWithoutEndingProcedure(bool forceps)
    {
        var game = new TreatmentMinigameState(AdvancedTreatmentType.ForeignBodyRemoval, 0, forceps);
        var injury = 0f;
        game.OnInjury += amount => injury += amount;
        game.Press(game.ShardPosition);
        game.Move(forceps ? game.ShardPosition + new Vector2(30, -20) : game.ExtractionPoint(1));
        game.Release();
        Assert.That(injury, Is.GreaterThan(0));
        Assert.That(game.ExtractionMistakes, Is.EqualTo(1));
        Assert.That(game.Engaged, Is.False);
        Assert.That(game.Progress, Is.Zero);
        Assert.That(game.Running, Is.True);
        Assert.That(game.Success, Is.False);
        game.Press(game.ShardPosition);
        Pull(game, game.ExtractionPoint(1), 180);
        game.Release();
        Assert.That(game.Success, Is.True, "A slip injures the wound but leaves the same procedure playable.");
    }

    [Test]
    public void ForcepsAllowFastVerticalPulls()
    {
        var game = new TreatmentMinigameState(AdvancedTreatmentType.ForeignBodyRemoval, 0, true);
        game.Press(game.ShardPosition);
        game.Move(game.ExtractionPoint(1));
        game.Release();
        Assert.That(game.Success, Is.True);
        Assert.That(game.ExtractionMistakes, Is.Zero);
    }

    [Test]
    public void IncreasingPainImmediatelyIncreasesHandShake()
    {
        var game = new TreatmentMinigameState(AdvancedTreatmentType.ForeignBodyRemoval, 0.1f, false);
        game.Update(0.13f);
        game.Move(Vector2.Zero);
        var steady = game.Cursor.Length();
        game.SetPain(0.9f);
        game.Move(Vector2.Zero);
        Assert.That(game.Cursor.Length(), Is.GreaterThan(steady * 5));
    }

    [Test]
    public void BadCompressionAndLooseWrappingCauseInjuryButRemainPlayable()
    {
        foreach (var mode in new[] { AdvancedTreatmentType.Hemostatic, AdvancedTreatmentType.Bandage })
        {
            var game = new TreatmentMinigameState(mode, 0, true, 20);
            var injuries = 0;
            game.OnInjury += _ => injuries++;
            game.Press(mode == AdvancedTreatmentType.Hemostatic ? game.CompressionPadPosition : game.WrapSpoolPosition);
            if (mode == AdvancedTreatmentType.Bandage) game.Move(TreatmentMinigameState.Center + new Vector2(300, 0));
            Tick(game, 90);
            Assert.That(injuries, Is.GreaterThan(0));
            Assert.That(game.Running, Is.True);
            Assert.That(game.Success, Is.False);
        }
    }

    [Test]
    public void ExtractionWithForcepsRequiresGripThenPullAndRelease()
    {
        var game = new TreatmentMinigameState(AdvancedTreatmentType.ForeignBodyRemoval, 0, true);
        game.Press(Vector2.Zero);
        game.Move(new Vector2(0, -300));
        Tick(game, 60);
        Assert.That(game.Progress, Is.Zero);
        game.Release();
        game.Press(game.ExtractionPoint(0));
        Pull(game, game.ExtractionPoint(1));
        Assert.That(game.Progress, Is.EqualTo(1));
        Assert.That(game.Success, Is.False, "The player must release the extracted shard.");
        game.Release();
        Assert.That(game.Success, Is.True);
        Assert.That(game.Quality, Is.GreaterThan(0.95));
    }

    [Test]
    public void ForcepsAllowSteadyMovementAndRegrippingWithoutStrain()
    {
        var game = new TreatmentMinigameState(AdvancedTreatmentType.ForeignBodyRemoval, 0.2f, true);
        game.Press(game.ExtractionPoint(0));
        Pull(game, game.ExtractionPoint(0.4f));
        game.Release();
        Assert.That(game.Progress, Is.EqualTo(0.4f).Within(0.03f));
        Assert.That(game.Success, Is.False);
        Tick(game, 30);
        game.Press(game.ShardPosition);
        Pull(game, game.ExtractionPoint(1) - new Vector2(0, 2));
        game.CancelGrip();
        Assert.That(game.Success, Is.False, "Losing focus must not finish a treatment.");
        game.Press(game.ShardPosition);
        game.Release();
        Assert.That(game.Success, Is.True);
        Assert.That(game.Strain, Is.Zero);
        Assert.That(game.Quality, Is.EqualTo(1));
    }

    [Test]
    public void ForcepsForgiveAnOffsetGripAndNeedLessPullThanBareHands()
    {
        var tool = new TreatmentMinigameState(AdvancedTreatmentType.ForeignBodyRemoval, 0, true);
        var hand = new TreatmentMinigameState(AdvancedTreatmentType.ForeignBodyRemoval, 0, false);
        var offset = new Vector2(20, 0);
        tool.Press(tool.ShardPosition + offset);
        hand.Press(hand.ShardPosition + offset);
        Assert.That(tool.Engaged, Is.True);
        Assert.That(hand.Engaged, Is.False);
        Pull(tool, tool.ExtractionPoint(1) + offset);
        tool.Release();
        Assert.That(tool.Success, Is.True);
        hand.Release();
        hand.Press(hand.ShardPosition);
        Pull(hand, tool.ExtractionPoint(1));
        hand.Release();
        Assert.That(hand.Success, Is.False);
    }

    [Test]
    public void SuturesRequireNeedlePassTensionAndAKnotForEachStitch()
    {
        var game = new TreatmentMinigameState(AdvancedTreatmentType.Suture, 0, true);
        game.Press(game.StitchPoint(0, false));
        game.Move(game.StitchPoint(0, true));
        game.Release();
        Assert.That(game.Stitch, Is.Zero, "Clicking the old endpoints cannot sew a wound.");
        game.Press(game.NeedlePosition);
        game.Move(game.StitchPoint(0, false));
        game.Move(new Vector2(600, 60));
        Assert.That(game.InjuryFlash, Is.GreaterThan(0));
        Assert.That(game.Running, Is.True);
        game.Release();
        for (var i = 0; i < 4; i++) TieStitch(game);
        Assert.That(game.Success, Is.True);
        Assert.That(game.Quality, Is.LessThan(1));
    }

    private static void TieStitch(TreatmentMinigameState game)
    {
        var stitch = game.Stitch;
        game.Press(game.NeedlePosition);
        game.Move(game.StitchPoint(stitch, false));
        for (var i = 1; i <= 60; i++)
        {
            game.Update(1 / 60f);
            game.Move(game.NeedlePath(stitch, i / 60f));
        }
        Assert.That(game.SuturePhase, Is.EqualTo(StitchPhase.Tension));
        Pull(game, game.StitchPoint(stitch, true) + new Vector2(0, 88), 60);
        Tick(game, 30);
        Assert.That(game.SuturePhase, Is.EqualTo(StitchPhase.Knot));
        Assert.That(game.Stitch, Is.EqualTo(stitch));
        var centre = game.KnotCenter;
        Pull(game, centre + new Vector2(24, 0), 30);
        for (var i = 1; i <= 100; i++)
        {
            game.Update(1 / 60f);
            var angle = i / 100f * MathF.Tau;
            game.Move(centre + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 24);
        }
        Assert.That(game.KnotProgress, Is.EqualTo(1));
        Pull(game, centre + new Vector2(0, 78), 30);
        Assert.That(game.KnotTied, Is.True,
            $"stitch={stitch} phase={game.SuturePhase} grip={game.Engaged} needle={game.NeedlePosition} knot={game.KnotCenter} progress={game.KnotProgress}");
        game.Release();
        Assert.That(game.Stitch, Is.EqualTo(stitch + 1));
    }

    [TestCase(15f)]
    [TestCase(7.5f)]
    [TestCase(2f)]
    public void SyringeFillAndPistonUseActualRemainingVolume(float starting)
    {
        var game = new TreatmentMinigameState(AdvancedTreatmentType.Injection, 0, true);
        game.SetInjectionContents(0, starting, 15);
        Assert.That(game.FillFraction, Is.EqualTo(starting / 15).Within(0.0001));
        var initialTravel = game.PistonTravel;
        game.SetInjectionContents(starting / 2, starting / 2, 15);
        Assert.That(game.PistonTravel, Is.GreaterThan(initialTravel));
        game.SetInjectionContents(starting, 0, 15);
        Assert.That(game.FillFraction, Is.Zero);
        Assert.That(game.PistonTravel, Is.EqualTo(TreatmentMinigameState.PistonStroke));
        Assert.That(TreatmentMinigameState.ChamberTop + game.PistonTravel,
            Is.EqualTo(TreatmentMinigameState.ChamberBottom));
    }

    [Test]
    public void PlungerMustBePressedAndBarrelHoldingDoesNotAdminister()
    {
        var game = new TreatmentMinigameState(AdvancedTreatmentType.Injection, 0, true);
        InsertNeedle(game);
        game.Press(game.SyringeGrip);
        Tick(game, 60);
        Assert.That(game.PressingPlunger, Is.False);
        game.Release();
        game.Press(game.PlungerGrip);
        var tip = game.SyringeTip;
        game.Move(game.PlungerGrip + new Vector2(0, 60));
        Assert.That(game.PressingPlunger, Is.True);
        Assert.That(game.SyringeTip, Is.EqualTo(tip));
        Assert.That(game.RemainingVolume, Is.EqualTo(15), "Pushing requests flow; only the server consumes medicine.");
        game.SetInjectionContents(15, 0, 15);
        Assert.That(game.PressingPlunger, Is.False);
        Assert.That(game.Success, Is.False, "An empty syringe still needs to be withdrawn.");
    }

    [Test]
    public void ASharpLateralJerkBreaksAnEmbeddedNeedleButAnAxialWithdrawalDoesNot()
    {
        var game = new TreatmentMinigameState(AdvancedTreatmentType.Injection, 0, true);
        InsertNeedle(game);
        var breaks = 0;
        game.OnNeedleBroken += _ => breaks++;
        game.Press(game.SyringeGrip);
        game.Move(game.SyringeGrip + new Vector2(65, 0));
        Assert.That(game.NeedleBroken, Is.True);
        Assert.That(breaks, Is.EqualTo(1));
        Assert.That(game.Running, Is.True);
        game.Move(game.SyringeGrip + new Vector2(80, 0));
        Assert.That(breaks, Is.EqualTo(1));
        var safe = new TreatmentMinigameState(AdvancedTreatmentType.Injection, 0, true);
        InsertNeedle(safe);
        safe.SetInjectionContents(5, 10, 15);
        safe.Press(safe.SyringeGrip);
        safe.Move(safe.SyringeGrip - new Vector2(0, 85));
        Assert.That(safe.NeedleBroken, Is.False);
        Assert.That(safe.Success, Is.True);
    }

    [Test]
    public void AShallowNeedlePullsFreeRatherThanLeavingAnEmbeddedFragment()
    {
        var game = new TreatmentMinigameState(AdvancedTreatmentType.Injection, 0, true);
        InsertNeedle(game);
        game.Press(game.SyringeGrip);
        game.Move(game.SyringeGrip - new Vector2(0, 28));
        Assert.That(game.SyringeTip.Y - TreatmentMinigameState.TissueSurface, Is.InRange(0, 12));
        game.Move(game.SyringeGrip + new Vector2(65, 0));
        Assert.That(game.NeedleBroken, Is.False);
        Assert.That(game.NeedleInserted, Is.False);
        Assert.That(game.Running, Is.True);
    }

    [Test]
    public void ReleasingAHalfPassedSuturePreservesTheNeedleButDoesNotCompleteAStitch()
    {
        var game = new TreatmentMinigameState(AdvancedTreatmentType.Suture, 0, true);
        game.Press(game.NeedlePosition);
        game.Move(game.StitchPoint(0, false));
        for (var i = 1; i <= 30; i++)
        {
            game.Update(1 / 60f);
            game.Move(game.NeedlePath(0, i / 60f));
        }
        game.Release();
        var needle = game.NeedlePosition;
        Tick(game, 60);
        Assert.That(game.NeedlePosition, Is.EqualTo(needle));
        Assert.That(game.NeedlePass, Is.EqualTo(0.5f).Within(0.001f));
        Assert.That(game.Stitch, Is.Zero);
        game.Press(game.NeedlePosition);
        for (var i = 31; i <= 60; i++)
        {
            game.Update(1 / 60f);
            game.Move(game.NeedlePath(0, i / 60f));
        }
        game.Release();
        Assert.That(game.SuturePhase, Is.EqualTo(StitchPhase.Tension));
        Assert.That(game.Stitch, Is.Zero, "A needle pass alone is not a tightened, tied stitch.");
    }

    [TestCase(60, 0)]
    [TestCase(0, -60)]
    public void MovingTheSyringeProducesDampedSway(float x, float y)
    {
        var game = new TreatmentMinigameState(AdvancedTreatmentType.Injection, 0, true);
        game.Press(game.SyringeGrip);
        game.Move(game.SyringeGrip + new Vector2(x, y));
        Tick(game, 5);
        Assert.That(Math.Abs(game.SyringeAngle), Is.GreaterThan(0.005f));
        game.Release();
        Tick(game, 240);
        Assert.That(Math.Abs(game.SyringeAngle), Is.LessThan(0.002f));
    }

    [Test]
    public void SplintRequiresStableAlignmentAndExplicitRelease()
    {
        var game = new TreatmentMinigameState(AdvancedTreatmentType.Splint, 0, true);
        game.Press(TreatmentMinigameState.Center + game.BoneOffset);
        game.Move(TreatmentMinigameState.Center);
        game.Update(1 / 60f);
        game.Release();
        Assert.That(game.Success, Is.False);
        game.Press(TreatmentMinigameState.Center + game.BoneOffset);
        for (var i = 0; i < 140; i++)
        {
            game.Move(TreatmentMinigameState.Center);
            game.Update(1 / 60f);
        }
        Assert.That(game.Running, Is.True);
        game.Release();
        Assert.That(game.Success, Is.True);
    }

    [TestCase(AdvancedTreatmentType.Bandage)]
    [TestCase(AdvancedTreatmentType.PressureBandage)]
    public void WrapCountsActualClockwiseSectorsAndKeepsPartialWork(AdvancedTreatmentType mode)
    {
        var game = new TreatmentMinigameState(mode, 0, true, 8);
        game.Press(WrapPoint(game, 0));
        for (var i = 1; i <= 180; i++)
        {
            game.Move(WrapPoint(game, i * MathF.PI / 180));
            game.Update(1 / 60f);
        }
        Assert.That(game.CompletedSegments, Is.EqualTo(4));
        game.Release();
        Tick(game, 60);
        Assert.That(game.CompletedSegments, Is.EqualTo(4));
        Assert.That(game.Running, Is.True);
        game.Press(WrapPoint(game, MathF.PI));
        for (var i = 181; i <= 362 && game.Running; i++)
        {
            game.Move(WrapPoint(game, i * MathF.PI / 180));
            game.Update(1 / 60f);
        }
        Assert.That(game.CompletedSegments, Is.EqualTo(8));
        Assert.That(game.Success, Is.True);
    }

    [Test]
    public void WrapRejectsBacktrackingJumpsAndMovementThroughTheLimb()
    {
        var game = new TreatmentMinigameState(AdvancedTreatmentType.Bandage, 0, true, 100);
        game.Press(WrapPoint(game, 0));
        // Repeatedly retracing a fraction of one sector must never count as a revolution.
        for (var i = 0; i < 100; i++)
        {
            game.Move(WrapPoint(game, 0.04f)); game.Update(1 / 60f);
            game.Move(WrapPoint(game, 0)); game.Update(1 / 60f);
        }
        Assert.That(game.CompletedSegments, Is.Zero);
        game.Move(WrapPoint(game, MathF.PI)); game.Update(1 / 60f);
        for (var i = 0; i < 180; i++)
        {
            game.Move(TreatmentMinigameState.Center + new Vector2(MathF.Cos(i / 60f), MathF.Sin(i / 60f)) * 18);
            game.Update(1 / 60f);
        }
        Assert.That(game.CompletedSegments, Is.Zero);
    }

    [Test]
    public void BandagingRequiresPickingUpTheRoll()
    {
        var game = new TreatmentMinigameState(AdvancedTreatmentType.Bandage, 0, true, 100);
        game.Press(TreatmentMinigameState.Center);
        for (var i = 0; i <= 360; i++)
        {
            game.Move(WrapPoint(game, i * MathF.PI / 180));
            game.Update(1 / 60f);
        }
        Assert.That(game.Engaged, Is.False);
        Assert.That(game.CompletedSegments, Is.Zero);
    }

    [Test]
    public void WindlassCanBeUnwoundWithoutLosingTheHandle()
    {
        var game = new TreatmentMinigameState(AdvancedTreatmentType.Tourniquet, 0, true);
        game.Press(game.WindlassTip);
        for (var i = 1; i <= 60; i++)
        {
            var angle = i / 60f;
            game.Move(TreatmentMinigameState.Center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 74);
            game.Update(1 / 60f);
        }
        var tightened = game.Tension;
        for (var i = 59; i >= 30; i--)
        {
            var angle = i / 60f;
            game.Move(TreatmentMinigameState.Center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 74);
            game.Update(1 / 60f);
        }
        Assert.That(game.Tension, Is.EqualTo(tightened * 0.5f).Within(0.001f));
        Assert.That(game.Engaged, Is.True);
    }

    [Test]
    public void SuppliesRemainReachableAfterDraggingOffTheCanvas()
    {
        var game = new TreatmentMinigameState(AdvancedTreatmentType.Hemostatic, 0, true);
        game.Press(game.CompressionPadPosition);
        game.Move(new Vector2(-1000));
        game.CancelGrip();
        Assert.That(game.CompressionPadPosition.X, Is.GreaterThan(0));
        Assert.That(game.CompressionPadPosition.Y, Is.GreaterThan(0));
        game.Press(game.CompressionPadPosition);
        Assert.That(game.Engaged, Is.True);
        game.Move(game.HoldTarget);
        FollowCompression(game, 210);
        Assert.That(game.Success, Is.True);
    }

    [Test]
    public void WindlassMustBeTurnedIntoPositionAndReleased()
    {
        var tourniquet = new TreatmentMinigameState(AdvancedTreatmentType.Tourniquet, 0, true);
        tourniquet.Press(TreatmentMinigameState.Center);
        Tick(tourniquet, 480);
        Assert.That(tourniquet.Success, Is.False, "Clicking without turning the handle cannot apply a tourniquet.");
        var controlled = new TreatmentMinigameState(AdvancedTreatmentType.Tourniquet, 0, true);
        controlled.Press(controlled.WindlassTip);
        var target = controlled.TensionTarget * MathF.Tau * 0.65f;
        for (var i = 1; i <= 120; i++)
        {
            var angle = target * i / 120;
            controlled.Move(TreatmentMinigameState.Center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 74);
            controlled.Update(1 / 60f);
        }
        Tick(controlled, 80);
        Assert.That(controlled.Success, Is.False);
        controlled.Release();
        Assert.That(controlled.Success, Is.True);
    }

    [Test]
    public void HemostaticPadMustBePickedUpAndAppliedToTheWound()
    {
        var compression = new TreatmentMinigameState(AdvancedTreatmentType.Hemostatic, 0, true);
        compression.Press(compression.HoldTarget);
        Tick(compression, 90);
        Assert.That(compression.Progress, Is.Zero);
        compression.Release();
        compression.Press(compression.CompressionPadPosition);
        compression.Move(compression.HoldTarget);
        FollowCompression(compression, 210);
        Assert.That(compression.Success, Is.True);
    }

    [TestCase(30)]
    [TestCase(144)]
    public void ExtractionDoesNotDependOnFrameRate(int fps)
    {
        var game = new TreatmentMinigameState(AdvancedTreatmentType.ForeignBodyRemoval, 0, true);
        game.Press(game.ExtractionPoint(0));
        for (var i = 1; i <= fps; i++)
        {
            game.Move(game.ExtractionPoint((float) i / fps));
            game.Update(1f / fps);
        }
        game.Release();
        Assert.That(game.Success, Is.True);
        Assert.That(game.Time, Is.InRange(0.99f, 1.01f));
    }

    [TestCase(0f, BleedingLevel.None)]
    [TestCase(0.004f, BleedingLevel.None)]
    [TestCase(0.006f, BleedingLevel.Light)]
    [TestCase(0.05f, BleedingLevel.Light)]
    [TestCase(0.099f, BleedingLevel.Light)]
    [TestCase(0.101f, BleedingLevel.Moderate)]
    [TestCase(0.3f, BleedingLevel.Moderate)]
    [TestCase(0.499f, BleedingLevel.Moderate)]
    [TestCase(0.501f, BleedingLevel.Heavy)]
    [TestCase(1f, BleedingLevel.Heavy)]
    public void BloodDropsUseLitresPerMinute(float litresPerMinute, BleedingLevel expected)
        => Assert.That(BodyPartState.GetBleedingLevel(litresPerMinute / 0.06f), Is.EqualTo(expected));

    [Test]
    public void TourniquetChangesTheDropButDoesNotHideInternalBleeding()
    {
        var health = new AdvancedHealthComponent();
        var part = new BodyPartState { Slot = BodyPartSlot.LeftForearm };
        part.Wounds.Add(new Wound { ExternalBleedingRate = 10 });
        Assert.That(part.ToUiState(health.GetBloodLossRate(part)).Bleeding, Is.EqualTo(BleedingLevel.Heavy));
        part.HasTourniquet = true;
        Assert.That(part.ToUiState(health.GetBloodLossRate(part)).Bleeding, Is.EqualTo(BleedingLevel.Light));
        part.Wounds.Add(new Wound { InternalBleedingRate = 4 });
        Assert.That(part.ToUiState(health.GetBloodLossRate(part)).Bleeding, Is.EqualTo(BleedingLevel.Moderate));
    }

    private static Vector2 WrapPoint(TreatmentMinigameState game, float angle) => TreatmentMinigameState.Center
        + new Vector2(MathF.Cos(angle + MathF.PI), MathF.Sin(angle + MathF.PI)) * game.WrapRadius(angle);
    private static void Tick(TreatmentMinigameState game, int frames)
    {
        for (var i = 0; i < frames; i++) game.Update(1 / 60f);
    }
}
