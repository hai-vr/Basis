using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Basis.BasisUI;
using Basis.Scripts.BasisSdk;
using Basis.Scripts.Settings;
using Basis.Shims;
using Cilbox;
using NUnit.Framework;
using UnityEngine;

namespace Basis.Tests.Sync
{
    public sealed class BasisSaveShimTests
    {
        const BindingFlags Internal = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        static readonly string[] EntryPoints = { nameof(BasisSaveShim.Save), nameof(BasisSaveShim.Load), nameof(BasisSaveShim.Delete) };
        static readonly Type[] Boxes = { typeof(CilboxSceneBasis), typeof(CilboxPropBasis), typeof(CilboxAvatarBasis) };
        readonly List<GameObject> hosts = new List<GameObject>();
        readonly List<string> stores = new List<string>();
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
            foreach (string store in stores)
            {
                if (Directory.Exists(FolderOf(store))) Directory.Delete(FolderOf(store), true);
            }
            stores.Clear();
        }

        static string Hex(string text)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                return string.Concat(digest.Take(16).Select(b => b.ToString("x2")));
            }
        }

        static string FolderOf(string store) => Path.Combine(Application.persistentDataPath, "CilboxSaves", Hex(store.Trim().ToLowerInvariant()));

        static string FileOf(string store, string slot) => Path.Combine(FolderOf(store), Hex(slot) + ".bsave");

        static string Item(string method) => BasisCilboxPermissionText.MethodPrefix + typeof(BasisSaveShim).FullName + "." + method;

        static FieldInfo Field(string name)
        {
            FieldInfo field = typeof(CilboxBasisCommon).GetField(name, Internal);
            Assert.IsNotNull(field, $"CilboxBasisCommon.{name} moved; this test cannot drive the save shim.");
            return field;
        }

        static void Grant(CilboxBasisCommon box, params string[] methods)
        {
            FieldInfo state = Field("consentState");
            state.SetValue(box, Enum.Parse(state.FieldType, "Allowed"));
            Field("consentGranted").SetValue(box, new HashSet<string>(methods.Select(Item), StringComparer.Ordinal));
        }

        static void Refill(CilboxProxy script) => Field("saveWindowWrites").SetValue(script.box, 0);

        string NewStore()
        {
            string store = "basis-save-test-" + Guid.NewGuid().ToString("N");
            stores.Add(store);
            return store;
        }

        CilboxBasisCommon AddBox(Type boxType, Transform parent = null)
        {
            GameObject host = new GameObject(boxType.Name + " save test");
            if (parent != null) host.transform.SetParent(parent);
            else hosts.Add(host);
            CilboxBasisCommon box = (CilboxBasisCommon)host.AddComponent(boxType);
            box.classes = new Dictionary<string, int>();
            return box;
        }

        CilboxProxy Script(Type boxType, Transform parent = null, bool consent = true)
        {
            CilboxBasisCommon box = AddBox(boxType, parent);
            box.interpreterAccountingDepth = 1;
            if (consent) Grant(box, EntryPoints);
            CilboxProxy proxy = new GameObject("Script").AddComponent<CilboxProxy>();
            proxy.transform.SetParent(box.transform);
            proxy.box = box;
            return proxy;
        }

        static bool Allowed(CilboxBasisCommon box, string method)
        {
            return box.CheckMethodAllowed(out MethodInfo _, typeof(BasisSaveShim), method, Array.Empty<SerializedTypeDescriptor>(), Array.Empty<SerializedTypeDescriptor>(), method);
        }

        [Test]
        public void WithoutConsent_NothingIsWrittenOrRead()
        {
            string store = NewStore();
            CilboxProxy script = Script(typeof(CilboxSceneBasis), consent: false);
            Assert.IsFalse(BasisSaveShim.Save(script, store, "slot", new byte[] { 1 }));
            Assert.IsNull(BasisSaveShim.Load(script, store, "slot"));
            Assert.IsFalse(Directory.Exists(FolderOf(store)));

            CilboxProxy approved = Script(typeof(CilboxSceneBasis));
            BasisSettingsDefaults.AllowUntrustedScriptPermissions.RawValue = false;
            Assert.IsFalse(BasisSaveShim.Save(approved, store, "slot", new byte[] { 1 }), "turning untrusted script permissions off must stop saving too");
            Assert.IsFalse(Directory.Exists(FolderOf(store)));
        }

        [Test]
        public void SaveLoadDelete_RoundTrip_InEveryBox()
        {
            byte[] data = Encoding.UTF8.GetBytes("level=3;score=1200");
            foreach (Type boxType in Boxes)
            {
                string store = NewStore();
                CilboxProxy script = Script(boxType);
                Assert.IsTrue(BasisSaveShim.Save(script, store, "progress", data), boxType.Name);
                Assert.IsTrue(File.Exists(FileOf(store, "progress")), boxType.Name);
                CollectionAssert.AreEqual(data, BasisSaveShim.Load(script, store, "progress"), boxType.Name);
                Assert.IsTrue(BasisSaveShim.Save(script, store, "progress", new byte[0]), boxType.Name);
                CollectionAssert.AreEqual(new byte[0], BasisSaveShim.Load(script, store, "progress"), boxType.Name + ": an empty save is still a save");
                Assert.IsTrue(BasisSaveShim.Delete(script, store, "progress"), boxType.Name);
                Assert.IsNull(BasisSaveShim.Load(script, store, "progress"), boxType.Name);
                Assert.IsFalse(BasisSaveShim.Delete(script, store, "progress"), boxType.Name);
                Assert.IsFalse(Directory.Exists(FolderOf(store)), boxType.Name + ": an emptied store should not leave its folder behind");
            }
        }

        [Test]
        public void SlotsAndStores_AreKeptApart()
        {
            string first = NewStore(), second = NewStore();
            CilboxProxy script = Script(typeof(CilboxPropBasis));
            Assert.IsTrue(BasisSaveShim.Save(script, first, "one", new byte[] { 1 }));
            Assert.IsTrue(BasisSaveShim.Save(script, first, "two", new byte[] { 2 }));
            Assert.IsTrue(BasisSaveShim.Save(script, second, "one", new byte[] { 3 }));
            CollectionAssert.AreEqual(new byte[] { 1 }, BasisSaveShim.Load(script, first, "one"));
            CollectionAssert.AreEqual(new byte[] { 2 }, BasisSaveShim.Load(script, first, "two"));
            CollectionAssert.AreEqual(new byte[] { 3 }, BasisSaveShim.Load(script, second, "one"));
            CollectionAssert.AreEqual(new byte[] { 1 }, BasisSaveShim.Load(script, "  " + first.ToUpperInvariant() + " ", "one"), "store keys ignore case and surrounding spaces");
            Assert.IsNull(BasisSaveShim.Load(script, first, "ONE"), "slot names are exact");
        }

        [Test]
        public void TheDefaultStore_IsTheContentId()
        {
            string propId = NewStore();
            byte[] data = { 7, 7, 7 };
            GameObject content = new GameObject("Prop");
            hosts.Add(content);
            content.AddComponent<BasisProp>().BasisBundleDescription = new BasisBundleDescription { ContentGroupId = propId };
            CilboxProxy prop = Script(typeof(CilboxPropBasis), content.transform);
            Assert.IsTrue(BasisSaveShim.Save(prop, "slot", data));
            Assert.IsTrue(File.Exists(FileOf(propId, "slot")));
            CollectionAssert.AreEqual(data, BasisSaveShim.Load(prop, "slot"));
            CollectionAssert.AreEqual(data, BasisSaveShim.Load(Script(typeof(CilboxSceneBasis)), propId, "slot"), "naming another content's id as the store shares it");
        }

        [Test]
        public void AWorldBox_FindsItsBasisSceneThroughTheSceneRoots()
        {
            Assume.That(UnityEngine.Object.FindObjectsByType<BasisScene>(FindObjectsInactive.Include), Is.Empty, "the open scene already has a BasisScene, which this test would find instead");
            string worldId = NewStore();
            GameObject world = new GameObject("World");
            hosts.Add(world);
            world.AddComponent<BasisScene>().BasisBundleDescription = new BasisBundleDescription { ContentGroupId = worldId };
            CilboxProxy scene = Script(typeof(CilboxSceneBasis));
            Assert.IsTrue(BasisSaveShim.Save(scene, "slot", new byte[] { 7 }));
            Assert.IsTrue(File.Exists(FileOf(worldId, "slot")), "a world box that is not under its BasisScene still finds it in the scene");
        }

        [Test]
        public void Limits_AreEnforced()
        {
            string store = NewStore();
            byte[] data = { 1, 2, 3 };
            CilboxProxy script = Script(typeof(CilboxSceneBasis));
            Assert.IsFalse(BasisSaveShim.Save(script, store, "big", new byte[BasisSaveShim.MaxSlotBytes + 1]));
            Assert.IsFalse(BasisSaveShim.Save(script, store, "slot", null));
            Assert.IsFalse(BasisSaveShim.Save(script, store, null, data));
            Assert.IsFalse(BasisSaveShim.Save(script, store, "", data));
            Assert.IsFalse(BasisSaveShim.Save(script, store, new string('s', BasisSaveShim.MaxNameLength + 1), data));
            Assert.IsFalse(BasisSaveShim.Save(script, "   ", "slot", data));
            Assert.IsFalse(BasisSaveShim.Save(script, new string('k', BasisSaveShim.MaxNameLength + 1), "slot", data));

            for (int i = 0; i < 3; i++) Assert.IsTrue(BasisSaveShim.Save(script, store, "chunk" + i, new byte[BasisSaveShim.MaxSlotBytes]));
            Assert.IsFalse(BasisSaveShim.Save(script, store, "chunk3", new byte[BasisSaveShim.MaxSlotBytes]), "a fourth full slot passes MaxStoreBytes");
            Assert.IsTrue(BasisSaveShim.Save(script, store, "chunk0", new byte[BasisSaveShim.MaxSlotBytes]), "rewriting a slot must not count it twice");

            string many = NewStore();
            for (int i = 0; i < BasisSaveShim.MaxSlots; i++)
            {
                Refill(script);
                Assert.IsTrue(BasisSaveShim.Save(script, many, "slot" + i, data), "slot " + i);
            }
            Refill(script);
            Assert.IsFalse(BasisSaveShim.Save(script, many, "one too many", data));
        }

        [Test]
        public void Writes_AreRateLimited_ButReadsAreNot()
        {
            string store = NewStore();
            CilboxProxy script = Script(typeof(CilboxAvatarBasis));
            for (int i = 0; i < 8; i++) Assert.IsTrue(BasisSaveShim.Save(script, store, "tick", new[] { (byte)i }), "write " + i);
            Assert.IsFalse(BasisSaveShim.Save(script, store, "tick", new byte[] { 99 }), "a ninth write inside one second");
            Assert.IsFalse(BasisSaveShim.Delete(script, store, "tick"), "deletes spend the same budget");
            for (int i = 0; i < 20; i++) CollectionAssert.AreEqual(new byte[] { 7 }, BasisSaveShim.Load(script, store, "tick"));
        }

        [Test]
        public void OneScript_CanOnlyReachAFewStores()
        {
            CilboxProxy script = Script(typeof(CilboxPropBasis));
            for (int i = 0; i < 4; i++)
            {
                Refill(script);
                Assert.IsTrue(BasisSaveShim.Save(script, NewStore(), "slot", new byte[] { 1 }), "store " + i);
            }
            Refill(script);
            Assert.IsFalse(BasisSaveShim.Save(script, NewStore(), "slot", new byte[] { 1 }));
            Assert.IsNull(BasisSaveShim.Load(script, NewStore(), "slot"));
        }

        [Test]
        public void DamagedFiles_LoadAsNull()
        {
            string store = NewStore();
            byte[] data = Encoding.UTF8.GetBytes("a save that will be damaged");
            CilboxProxy script = Script(typeof(CilboxSceneBasis));
            Assert.IsTrue(BasisSaveShim.Save(script, store, "slot", data));
            string file = FileOf(store, "slot");
            byte[] good = File.ReadAllBytes(file);
            Assert.AreEqual(16 + data.Length, good.Length);
            CollectionAssert.AreEqual(Encoding.ASCII.GetBytes("BSAV"), good.Take(4).ToArray());
            Assert.AreEqual(1, good[4]);

            Func<byte[], byte[]>[] damages =
            {
                b => { b[20] ^= 1; return b; },
                b => { b[0] = (byte)'X'; return b; },
                b => { b[4] = 2; return b; },
                b => { b[6] = 1; return b; },
                b => { b[9] ^= 1; return b; },
                b => { b[13] ^= 1; return b; },
                b => b.Take(b.Length - 1).ToArray(),
                b => b.Take(10).ToArray(),
                b => b.Concat(new byte[] { 0 }).ToArray(),
                b => new byte[16 + BasisSaveShim.MaxSlotBytes + 1],
            };
            for (int i = 0; i < damages.Length; i++)
            {
                File.WriteAllBytes(file, damages[i]((byte[])good.Clone()));
                Assert.IsNull(BasisSaveShim.Load(script, store, "slot"), "damage " + i);
            }
            File.WriteAllBytes(file, good);
            CollectionAssert.AreEqual(data, BasisSaveShim.Load(script, store, "slot"));
        }

        [Test]
        public void OnlyARunningScript_CanUseIt()
        {
            string store = NewStore();
            CilboxProxy script = Script(typeof(CilboxSceneBasis));
            Assert.IsFalse(BasisSaveShim.Save(null, store, "slot", new byte[] { 1 }));
            Assert.IsFalse(BasisSaveShim.Save("not a script", store, "slot", new byte[] { 1 }));
            Assert.IsFalse(BasisSaveShim.Save(script.box, store, "slot", new byte[] { 1 }));
            script.box.interpreterAccountingDepth = 0;
            Assert.IsFalse(BasisSaveShim.Save(script, store, "slot", new byte[] { 1 }), "a proxy whose box is not running must not be accepted");
            Assert.IsFalse(Directory.Exists(FolderOf(store)));
        }

        [Test]
        public void TheSandbox_ResolvesOnlyTheEntryPoints_AndAsksForConsent()
        {
            string[] helpers = typeof(BasisSaveShim).GetMethods(BindingFlags.Static | BindingFlags.NonPublic).Select(m => m.Name).Distinct().ToArray();
            Assert.IsNotEmpty(helpers);
            foreach (Type boxType in Boxes)
            {
                CilboxBasisCommon box = AddBox(boxType);
                Assert.IsTrue(box.CheckTypeAllowed(typeof(BasisSaveShim)), boxType.Name);
                foreach (string method in EntryPoints) Assert.IsTrue(Allowed(box, method), boxType.Name + " " + method);
                foreach (string helper in helpers) Assert.IsFalse(Allowed(box, helper), boxType.Name + " resolves the private helper " + helper);

                FieldInfo scanField = Field("consentScan");
                object scan = Activator.CreateInstance(scanField.FieldType, true);
                scanField.SetValue(box, scan);
                try
                {
                    foreach (string method in EntryPoints) Allowed(box, method);
                    Allowed(box, helpers[0]);
                }
                finally
                {
                    scanField.SetValue(box, null);
                }
                CollectionAssert.AreEquivalent(EntryPoints.Select(Item), (SortedSet<string>)scan.GetType().GetField("Items", Internal).GetValue(scan), boxType.Name);
                CollectionAssert.AreEquivalent(new[] { Item(helpers[0]) }, (SortedSet<string>)scan.GetType().GetField("Blocked", Internal).GetValue(scan), boxType.Name);
            }
        }

        [Test]
        public void TheConsentPrompt_CallsItSaving()
        {
            List<BasisCilboxPermissionText.Line> lines = BasisCilboxPermissionText.Describe(EntryPoints.Select(Item));
            Assert.AreEqual(1, lines.Count);
            Assert.AreEqual(BasisLocalization.Get("settings.cilboxPermissions.cap.saves"), lines[0].Text);
            Assert.AreNotEqual(BasisLocalization.Get("settings.cilboxPermissions.cap.basisInternals"), lines[0].Text);
            Assert.IsFalse(lines[0].Severe);
        }
    }
}
