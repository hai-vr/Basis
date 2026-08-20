using System;
using Basis.BasisUI;
using Basis.Scripts.Drivers;
using NUnit.Framework;

namespace Basis.Tests.Rendering
{
    public class BasisBottleneckHintTests
    {
        [Test]
        public void CpuVerdict_LightsCpuControls()
        {
            Assert.That(SettingsProviderBottleneckHints.SideFor(BasisFrameBottleneckKind.Cpu),
                Is.EqualTo(BasisFrameCostSide.Cpu));
        }

        [Test]
        public void GpuVerdict_LightsGpuControls()
        {
            Assert.That(SettingsProviderBottleneckHints.SideFor(BasisFrameBottleneckKind.Gpu),
                Is.EqualTo(BasisFrameCostSide.Gpu));
        }

        [Test]
        public void EveryOtherVerdict_LightsNothing()
        {
            foreach (BasisFrameBottleneckKind kind in Enum.GetValues(typeof(BasisFrameBottleneckKind)))
            {
                if (kind == BasisFrameBottleneckKind.Cpu || kind == BasisFrameBottleneckKind.Gpu)
                {
                    continue;
                }

                Assert.That(SettingsProviderBottleneckHints.SideFor(kind),
                    Is.EqualTo(BasisFrameCostSide.None),
                    $"{kind} names no single side, so nothing on the page should be pointed at.");
            }
        }

        [Test]
        public void BothSidedControls_MatchEitherVerdict()
        {
            Assert.That(BasisFrameCostSide.Both & BasisFrameCostSide.Cpu, Is.EqualTo(BasisFrameCostSide.Cpu));
            Assert.That(BasisFrameCostSide.Both & BasisFrameCostSide.Gpu, Is.EqualTo(BasisFrameCostSide.Gpu));
        }
    }
}
