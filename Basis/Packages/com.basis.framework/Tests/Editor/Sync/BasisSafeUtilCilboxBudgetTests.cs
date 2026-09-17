using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Basis;
using Cilbox;
using NUnit.Framework;
using UnityEngine;

namespace Basis.Tests.Sync
{
    public sealed class BasisSafeUtilCilboxBudgetTests
    {
        GameObject root;
        CilboxSceneBasis box;
        CilboxProxy proxy;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("SafeUtil Cilbox budget test");
            box = root.AddComponent<CilboxSceneBasis>();
            proxy = root.AddComponent<CilboxProxy>();
            proxy.box = box;
        }

        [TearDown]
        public void TearDown()
        {
            if (root != null)
                UnityEngine.Object.DestroyImmediate(root);
        }

        [Test]
        public void GetExecutionBudgetUs_ReturnsOwningBoxBudget()
        {
            box.timeoutLengthUs = 321_000;

            Assert.AreEqual(321_000, SafeUtil.GetExecutionBudgetUs(proxy));
        }

        [Test]
        public void GetRemainingExecutionBudgetUs_UsesOwningBoxDeadline()
        {
            const long budgetUs = 1_000_000;
            box.timeoutLengthUs = budgetUs;
            box.interpreterTicksInUs = Stopwatch.Frequency / 1_000_000;
            Assert.Greater(box.interpreterTicksInUs, 0);

            box.interpreterAccountingDropDead = Stopwatch.GetTimestamp() + budgetUs * box.interpreterTicksInUs;

            long remaining = SafeUtil.GetRemainingExecutionBudgetUs(proxy);

            Assert.Greater(remaining, 0);
            Assert.LessOrEqual(remaining, budgetUs);
        }

        [Test]
        public void GetRemainingExecutionBudgetUs_ClampsExpiredDeadlineToZero()
        {
            box.interpreterTicksInUs = Stopwatch.Frequency / 1_000_000;
            box.interpreterAccountingDropDead = Stopwatch.GetTimestamp() - box.interpreterTicksInUs;

            Assert.AreEqual(0, SafeUtil.GetRemainingExecutionBudgetUs(proxy));
        }

        [Test]
        public void GetLastFrameExecutionUs_ReturnsOwningBoxAccounting()
        {
            box.usSpentLastFrame = 12_345;

            Assert.AreEqual(12_345, SafeUtil.GetLastFrameExecutionUs(proxy));
        }

        [Test]
        public void SafeUtilWhitelist_PreservesExistingSurfaceAndAddsOnlyBudgetHelpers()
        {
            FieldInfo field = typeof(CilboxBasisCommon).GetField(
                "commonMethodWhitelist",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(field);

            var whitelist = (Dictionary<Type, HashSet<string>>)field.GetValue(null);
            Assert.IsTrue(whitelist.TryGetValue(typeof(SafeUtil), out HashSet<string> allowed));

            CollectionAssert.AreEquivalent(new[]
            {
                ".ctor",
                nameof(SafeUtil.AddEventTrigger),
                nameof(SafeUtil.MakeNetworkable),
                nameof(SafeUtil.MakeInteractable),
                nameof(SafeUtil.GetExecutionBudgetUs),
                nameof(SafeUtil.GetRemainingExecutionBudgetUs),
                nameof(SafeUtil.GetLastFrameExecutionUs),
            }, allowed);
        }
    }
}
