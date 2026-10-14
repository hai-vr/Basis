using Unity.Scripting.LifecycleManagement;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Basis.BasisUI;
using Basis.Network.Core;
using Basis.Scripts.BasisSdk;
using Basis.Scripts.Drivers;
using Basis.Scripts.Networking;
using Basis.Scripts.Settings;
using Basis.Shims;
using Cilbox;
using NUnit.Framework;
using UnityEngine;

namespace Basis.Tests.Sync
{
    [NoAutoStaticsCleanup]
    public sealed class BasisServerStorageShimTests
    {
        const BindingFlags Internal = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        const BindingFlags Everything = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly;
        static readonly string[] Consented = { "Save", "SaveString", "Load", "LoadString", "Delete", "Keys", "Clear", "Usage" };
        static readonly Type[] Boxes = { typeof(CilboxSceneBasis), typeof(CilboxPropBasis), typeof(CilboxAvatarBasis) };
        readonly List<GameObject> hosts = new List<GameObject>();
        bool untrustedBefore;

        [SetUp]
        public void SetUp()
        {
            untrustedBefore = BasisSettingsDefaults.AllowUntrustedScriptPermissions.RawValue;
            BasisSettingsDefaults.AllowUntrustedScriptPermissions.RawValue = true;
        }

        [TearDown]
        public void TearDown()
        {
            BasisSettingsDefaults.AllowUntrustedScriptPermissions.RawValue = untrustedBefore;
            foreach (GameObject host in hosts)
            {
                if (host != null) UnityEngine.Object.DestroyImmediate(host);
            }
            hosts.Clear();
            Pump();
        }

        static void Pump()
        {
            for (int i = 0; i < 3; i++) BasisFrameClock.Tick(0.016f);
        }

        static string Item(string method) => BasisCilboxPermissionText.MethodPrefix + typeof(BasisServerStorageShim).FullName + "." + method;

        static FieldInfo Field(string name)
        {
            FieldInfo field = typeof(CilboxBasisCommon).GetField(name, Internal);
            Assert.IsNotNull(field, $"CilboxBasisCommon.{name} moved; this test cannot drive the server storage shim.");
            return field;
        }

        static void Grant(CilboxBasisCommon box)
        {
            FieldInfo state = Field("consentState");
            state.SetValue(box, Enum.Parse(state.FieldType, "Allowed"));
            Field("consentGranted").SetValue(box, new HashSet<string>(Consented.Select(Item), StringComparer.Ordinal));
        }

        CilboxBasisCommon AddBox(Type boxType, Transform parent = null)
        {
            GameObject host = new GameObject(boxType.Name + " server storage test");
            if (parent != null) host.transform.SetParent(parent);
            else hosts.Add(host);
            CilboxBasisCommon box = (CilboxBasisCommon)host.AddComponent(boxType);
            box.classes = new Dictionary<string, int>();
            return box;
        }

        CilboxProxy Script(Type boxType, Transform parent = null, bool consent = true, bool running = true)
        {
            CilboxBasisCommon box = AddBox(boxType, parent);
            box.interpreterAccountingDepth = running ? 1u : 0u;
            if (consent) Grant(box);
            CilboxProxy proxy = new GameObject("Script").AddComponent<CilboxProxy>();
            proxy.transform.SetParent(box.transform);
            proxy.box = box;
            return proxy;
        }

        static bool Allowed(CilboxBasisCommon box, string method)
        {
            return box.CheckMethodAllowed(out MethodInfo _, typeof(BasisServerStorageShim), method, Array.Empty<SerializedTypeDescriptor>(), Array.Empty<SerializedTypeDescriptor>(), method);
        }

        static BasisServerStorageResult SaveAndWait(CilboxProxy script, string store, string key)
        {
            BasisServerStorageResult? result = null;
            if (store == null) BasisServerStorageShim.Save(script, key, new byte[] { 1 }, r => result = r);
            else BasisServerStorageShim.Save(script, store, key, new byte[] { 1 }, r => result = r);
            Assert.IsNull(result, "answers always arrive on a later frame, never inside the call");
            Pump();
            Assert.IsTrue(result.HasValue, "every request is answered");
            return result.Value;
        }

        [Test]
        public void TheShimType_HasNothingButItsEntryPoints()
        {
            Type shim = typeof(BasisServerStorageShim);
            Assert.IsEmpty(shim.GetNestedTypes(Everything), "a nested type on a whitelisted shim is reachable from scripts");
            foreach (MethodInfo method in shim.GetMethods(Everything))
            {
                Assert.IsTrue(method.IsPublic && method.IsStatic, method.Name + " must be public static");
                Assert.IsTrue(method.Name == nameof(BasisServerStorageShim.IsAvailable) || Consented.Contains(method.Name), method.Name + " is not an entry point");
            }
            foreach (FieldInfo field in shim.GetFields(Everything)) Assert.IsTrue(field.IsLiteral, field.Name + " must be a constant");
            CollectionAssert.AreEquivalent(Enum.GetValues(typeof(BasisStorageStatus)).Cast<BasisStorageStatus>().Select(v => (int)v), Enum.GetValues(typeof(BasisServerStorageResult)).Cast<BasisServerStorageResult>().Select(v => (int)v), "the script result enum mirrors the protocol status");
        }

        [Test]
        public void EveryBox_AllowsTheEntryPoints_AndFloorDeniesAnythingElse()
        {
            foreach (Type boxType in Boxes)
            {
                CilboxBasisCommon box = AddBox(boxType);
                Assert.IsTrue(box.CheckTypeAllowed(typeof(BasisServerStorageShim)), boxType.Name);
                Assert.IsTrue(box.CheckTypeAllowed(typeof(BasisServerStorageResult)), boxType.Name);
                Assert.IsTrue(Allowed(box, nameof(BasisServerStorageShim.IsAvailable)), boxType.Name);
                foreach (string method in Consented) Assert.IsTrue(Allowed(box, method), boxType.Name + "." + method);
                Assert.IsFalse(Allowed(box, "Begin"), boxType.Name + ": helpers must never resolve");
                Assert.IsFalse(Allowed(box, "ScopeOf"), boxType.Name);
            }
        }

        [Test]
        public void WithoutConsent_RequestsAreRefused()
        {
            CilboxProxy script = Script(typeof(CilboxSceneBasis), consent: false);
            Assert.AreEqual(BasisServerStorageResult.NotAllowed, SaveAndWait(script, null, "slot"));
            script.box.interpreterAccountingDepth = 0;

            CilboxProxy approved = Script(typeof(CilboxPropBasis));
            BasisSettingsDefaults.AllowUntrustedScriptPermissions.RawValue = false;
            Assert.AreEqual(BasisServerStorageResult.NotAllowed, SaveAndWait(approved, null, "slot"), "turning untrusted script permissions off stops server saves too");
        }

        [Test]
        public void OnlyTheScriptThatIsRunning_CanAsk()
        {
            CilboxProxy idle = Script(typeof(CilboxSceneBasis), running: false);
            Assert.AreEqual(BasisServerStorageResult.NotAllowed, SaveAndWait(idle, null, "slot"), "a script that is not executing cannot be named as the caller");

            CilboxProxy first = Script(typeof(CilboxSceneBasis));
            Script(typeof(CilboxPropBasis));
            Assert.AreEqual(BasisServerStorageResult.NotAllowed, SaveAndWait(first, null, "slot"), "while another box is running nobody can tell which one called");

            Assert.AreEqual(BasisServerStorageResult.NotAllowed, SaveAndWait(null, null, "slot"));
        }

        [Test]
        public void WithConsent_ButNoServer_TheAnswerIsNotConnected()
        {
            foreach (Type boxType in Boxes)
            {
                CilboxProxy script = Script(boxType);
                Assert.AreEqual(BasisServerStorageResult.NotConnected, SaveAndWait(script, "my-game", "slot"), boxType.Name);
                foreach (GameObject host in hosts) UnityEngine.Object.DestroyImmediate(host);
                hosts.Clear();
            }
        }

        [Test]
        public void BadStoresAndKeys_AreInvalid()
        {
            CilboxProxy script = Script(typeof(CilboxSceneBasis));
            Assert.AreEqual(BasisServerStorageResult.Invalid, SaveAndWait(script, "   ", "slot"));
            Assert.AreEqual(BasisServerStorageResult.Invalid, SaveAndWait(script, "my-game", "line\nbreak"));
            Assert.AreEqual(BasisServerStorageResult.Invalid, SaveAndWait(script, "my-game", new string('k', BasisServerStorageShim.MaxKeyBytes + 1)));
            Assert.AreEqual(BasisServerStorageResult.Invalid, SaveAndWait(script, new string('s', 80), "slot"));
        }

        [Test]
        public void ADestroyedScript_IsNotCalledBack()
        {
            CilboxProxy script = Script(typeof(CilboxSceneBasis));
            bool called = false;
            BasisServerStorageShim.Load(script, "my-game", "slot", (r, data) => called = true);
            UnityEngine.Object.DestroyImmediate(script.gameObject);
            Pump();
            Assert.IsFalse(called);
        }

        [Test]
        public void TheDefaultStore_IsTheContentId_AndNamedStoresArePrefixed()
        {
            MethodInfo scopeOf = typeof(BasisServerStorageShim).Assembly.GetType("Cilbox.CilboxServerStorage").GetMethod("ScopeOf", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(scopeOf, "CilboxServerStorage.ScopeOf moved");
            GameObject content = new GameObject("Prop");
            hosts.Add(content);
            content.AddComponent<BasisProp>().BasisBundleDescription = new BasisBundleDescription { ContentGroupId = "prop-content-id" };
            CilboxBasisCommon box = AddBox(typeof(CilboxPropBasis), content.transform);
            Assert.AreEqual("prop-content-id", scopeOf.Invoke(null, new object[] { box, null }));
            Assert.AreEqual("store:my-game", scopeOf.Invoke(null, new object[] { box, "  My-Game " }));
            Assert.IsNull(scopeOf.Invoke(null, new object[] { box, "" }));
            content.GetComponent<BasisProp>().BasisBundleDescription = new BasisBundleDescription { ContentGroupId = "bad\nid" };
            Assert.IsNull(scopeOf.Invoke(null, new object[] { box, null }), "an unusable content id never falls through to another store");
        }

        [Test]
        public void ThePromptNamesTheCapability()
        {
            List<BasisCilboxPermissionText.Line> lines = BasisCilboxPermissionText.Describe(new[] { Item("Save"), Item("Load") });
            Assert.AreEqual(1, lines.Count);
            Assert.AreEqual(BasisLocalization.Get("settings.cilboxPermissions.cap.serverSaves"), lines[0].Text);
            Assert.IsFalse(lines[0].Severe);
        }

        [Test]
        public void TheClient_RefusesWithoutAConnection_OnTheNextFrame()
        {
            List<BasisStorageReply> replies = new List<BasisStorageReply>();
            BasisServerStorageClient.Set("scope", "key", Encoding.UTF8.GetBytes("value"), replies.Add);
            BasisServerStorageClient.Get("scope", "bad\u0007key", replies.Add);
            BasisServerStorageClient.Set("scope", "key", null, replies.Add);
            Assert.IsEmpty(replies);
            Pump();
            Assert.AreEqual(3, replies.Count);
            Assert.AreEqual(BasisStorageStatus.NotConnected, replies[0].Status);
            Assert.AreEqual(BasisStorageStatus.Invalid, replies[1].Status);
            Assert.AreEqual(BasisStorageStatus.Invalid, replies[2].Status);
            Assert.IsFalse(BasisServerStorageClient.IsSupported);
        }
    }
}
