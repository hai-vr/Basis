using System.IO;
using Basis.BasisUI;
using NUnit.Framework;
using UnityEngine;

namespace Basis.Tests.UI
{
    public class BasisSpawnAnchorTests
    {
        private string tempFile;

        [SetUp]
        public void SetUp()
        {
            tempFile = Path.Combine(Path.GetTempPath(), "BasisSpawnAnchorTests_" + Path.GetRandomFileName() + ".json");
            BasisSpawnAnchors.DefaultFilePath = tempFile;
            BasisSpawnAnchors.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            BasisSpawnAnchors.Clear();
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }

        [Test]
        public void AddSelectsTheNewAnchor()
        {
            BasisSpawnAnchors.SpawnAnchor added = BasisSpawnAnchors.Add("A", new Vector3(1f, 2f, 3f), Quaternion.identity);
            Assert.AreEqual(1, BasisSpawnAnchors.Count);
            Assert.AreEqual(0, BasisSpawnAnchors.SelectedIndex);
            Assert.IsTrue(BasisSpawnAnchors.TryGetSelected(out BasisSpawnAnchors.SpawnAnchor selected));
            Assert.AreSame(added, selected);
        }

        [Test]
        public void AddWithoutSelectKeepsSelection()
        {
            BasisSpawnAnchors.Add("A", Vector3.zero, Quaternion.identity);
            BasisSpawnAnchors.Add("B", Vector3.one, Quaternion.identity, false);
            Assert.AreEqual(0, BasisSpawnAnchors.SelectedIndex);
        }

        [Test]
        public void RemovingBeforeTheSelectionShiftsIt()
        {
            BasisSpawnAnchors.Add("A", Vector3.zero, Quaternion.identity);
            BasisSpawnAnchors.Add("B", Vector3.zero, Quaternion.identity);
            BasisSpawnAnchors.SpawnAnchor c = BasisSpawnAnchors.Add("C", Vector3.zero, Quaternion.identity);
            BasisSpawnAnchors.Remove(0);
            Assert.AreEqual(1, BasisSpawnAnchors.SelectedIndex);
            Assert.IsTrue(BasisSpawnAnchors.TryGetSelected(out BasisSpawnAnchors.SpawnAnchor selected));
            Assert.AreSame(c, selected);
        }

        [Test]
        public void RemovingTheSelectionClearsIt()
        {
            BasisSpawnAnchors.Add("A", Vector3.zero, Quaternion.identity);
            BasisSpawnAnchors.Add("B", Vector3.zero, Quaternion.identity);
            BasisSpawnAnchors.RemoveSelected();
            Assert.AreEqual(1, BasisSpawnAnchors.Count);
            Assert.AreEqual(-1, BasisSpawnAnchors.SelectedIndex);
            Assert.IsFalse(BasisSpawnAnchors.TryGetSelected(out _));
        }

        [Test]
        public void SelectingOutOfRangeClearsTheSelection()
        {
            BasisSpawnAnchors.Add("A", Vector3.zero, Quaternion.identity);
            BasisSpawnAnchors.Select(5);
            Assert.AreEqual(-1, BasisSpawnAnchors.SelectedIndex);
            BasisSpawnAnchors.Select(0);
            Assert.AreEqual(0, BasisSpawnAnchors.SelectedIndex);
        }

        [Test]
        public void SaveAndLoadRoundTrip()
        {
            Quaternion rotation = Quaternion.Euler(10f, 80f, -5f);
            BasisSpawnAnchors.SpawnAnchor a = BasisSpawnAnchors.Add("A", new Vector3(1.5f, -2f, 3.25f), rotation);
            BasisSpawnAnchors.SetScaleOverride(a, true, 2.5f);
            BasisSpawnAnchors.Add("B", Vector3.one, Quaternion.identity, false);
            string path = tempFile + ".copy.json";
            try
            {
                Assert.IsTrue(BasisSpawnAnchors.Save(path));
                BasisSpawnAnchors.Clear();
                Assert.AreEqual(0, BasisSpawnAnchors.Count);
                Assert.IsTrue(BasisSpawnAnchors.Load(path));
                Assert.AreEqual(2, BasisSpawnAnchors.Count);
                Assert.AreEqual(0, BasisSpawnAnchors.SelectedIndex);
                BasisSpawnAnchors.SpawnAnchor loaded = BasisSpawnAnchors.Anchors[0];
                Assert.AreEqual("A", loaded.Name);
                Assert.That(Vector3.Distance(loaded.Position, new Vector3(1.5f, -2f, 3.25f)), Is.LessThan(1e-5f));
                Assert.That(Quaternion.Angle(loaded.Rotation, rotation), Is.LessThan(0.01f));
                Assert.IsTrue(loaded.OverrideScale);
                Assert.AreEqual(2.5f, loaded.Scale, 1e-5f);
                Assert.IsFalse(BasisSpawnAnchors.Anchors[1].OverrideScale);
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [Test]
        public void ChangesAutosaveToTheDefaultFile()
        {
            BasisSpawnAnchors.Add("A", Vector3.zero, Quaternion.identity);
            Assert.IsTrue(File.Exists(tempFile));
            BasisSpawnAnchors.SpawnAnchorFile file = JsonUtility.FromJson<BasisSpawnAnchors.SpawnAnchorFile>(File.ReadAllText(tempFile));
            Assert.AreEqual(1, file.Anchors.Length);
            Assert.AreEqual("A", file.Anchors[0].Name);
            Assert.AreEqual(0, file.SelectedIndex);
        }

        [Test]
        public void LoadSanitizesBrokenValues()
        {
            string path = tempFile + ".broken.json";
            try
            {
                File.WriteAllText(path, "{\"Anchors\":[{\"Name\":\"\",\"Position\":{\"x\":1,\"y\":2,\"z\":3},\"Rotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":0},\"OverrideScale\":true,\"Scale\":99}],\"SelectedIndex\":7}");
                Assert.IsTrue(BasisSpawnAnchors.Load(path));
                Assert.AreEqual(1, BasisSpawnAnchors.Count);
                BasisSpawnAnchors.SpawnAnchor loaded = BasisSpawnAnchors.Anchors[0];
                Assert.AreEqual("Anchor 1", loaded.Name);
                Assert.That(Quaternion.Angle(loaded.Rotation, Quaternion.identity), Is.LessThan(0.01f));
                Assert.AreEqual(BasisSpawnAnchors.MaxScale, loaded.Scale, 1e-5f);
                Assert.AreEqual(-1, BasisSpawnAnchors.SelectedIndex);
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [Test]
        public void LoadingAMissingFileLeavesAnchorsAlone()
        {
            BasisSpawnAnchors.Add("A", Vector3.zero, Quaternion.identity);
            Assert.IsFalse(BasisSpawnAnchors.Load(tempFile + ".missing.json"));
            Assert.AreEqual(1, BasisSpawnAnchors.Count);
            Assert.AreEqual(0, BasisSpawnAnchors.SelectedIndex);
        }

        [Test]
        public void ResolvePathUsesTheDefaultFolderForBareNames()
        {
            Assert.AreEqual(tempFile, BasisSpawnAnchors.ResolvePath(string.Empty));
            Assert.AreEqual(tempFile, BasisSpawnAnchors.ResolvePath("   "));
            Assert.AreEqual(Path.Combine(Path.GetDirectoryName(tempFile), "set.json"), BasisSpawnAnchors.ResolvePath("set.json"));
            string rooted = Path.Combine(Path.GetTempPath(), "elsewhere.json");
            Assert.AreEqual(rooted, BasisSpawnAnchors.ResolvePath(rooted));
        }

        [Test]
        public void SetPoseWritesPositionAndRotationAndAutosaves()
        {
            BasisSpawnAnchors.SpawnAnchor a = BasisSpawnAnchors.Add("A", Vector3.zero, Quaternion.identity);
            Quaternion rotation = Quaternion.Euler(0f, 45f, 0f);
            BasisSpawnAnchors.SetPose(a, new Vector3(2f, 1f, -3f), rotation);
            Assert.That(Vector3.Distance(a.Position, new Vector3(2f, 1f, -3f)), Is.LessThan(1e-6f));
            Assert.That(Quaternion.Angle(a.Rotation, rotation), Is.LessThan(0.01f));
            BasisSpawnAnchors.SpawnAnchorFile file = JsonUtility.FromJson<BasisSpawnAnchors.SpawnAnchorFile>(File.ReadAllText(tempFile));
            Assert.That(Vector3.Distance(file.Anchors[0].Position, new Vector3(2f, 1f, -3f)), Is.LessThan(1e-5f));
            Assert.That(Quaternion.Angle(file.Anchors[0].Rotation, rotation), Is.LessThan(0.01f));
        }

        [Test]
        public void SetNameTrimsAndAutosaves()
        {
            BasisSpawnAnchors.SpawnAnchor a = BasisSpawnAnchors.Add("A", Vector3.zero, Quaternion.identity);
            BasisSpawnAnchors.SetName(a, "  Stage left  ");
            Assert.AreEqual("Stage left", a.Name);
            BasisSpawnAnchors.SpawnAnchorFile file = JsonUtility.FromJson<BasisSpawnAnchors.SpawnAnchorFile>(File.ReadAllText(tempFile));
            Assert.AreEqual("Stage left", file.Anchors[0].Name);
        }

        [Test]
        public void SetNameIgnoresBlankNames()
        {
            BasisSpawnAnchors.SpawnAnchor a = BasisSpawnAnchors.Add("A", Vector3.zero, Quaternion.identity);
            BasisSpawnAnchors.SetName(a, "   ");
            BasisSpawnAnchors.SetName(a, null);
            Assert.AreEqual("A", a.Name);
        }

        [Test]
        public void FillArcSweepsFromTheStartVectorToItsRotation()
        {
            Vector3[] points = new Vector3[BasisSpawnAnchorHandle.ArcPoints];
            Vector3 origin = new Vector3(1f, 2f, 3f);
            BasisSpawnAnchorHandle.FillArc(points, origin, Vector3.up, Vector3.forward, 90f, 2f);
            Assert.That(Vector3.Distance(points[0], origin + Vector3.forward * 2f), Is.LessThan(1e-5f));
            Assert.That(Vector3.Distance(points[points.Length - 1], origin + Vector3.right * 2f), Is.LessThan(1e-5f));
            Assert.That(Vector3.Distance(points[(points.Length - 1) / 2], origin + (Vector3.forward + Vector3.right).normalized * 2f), Is.LessThan(1e-5f));
        }

        [Test]
        public void FillRingClosesOnItself()
        {
            Vector3[] points = new Vector3[BasisSpawnAnchorHandle.RingPoints];
            BasisSpawnAnchorHandle.FillRing(points, Vector3.zero, Vector3.up, Vector3.forward, 1f);
            for (int i = 0; i < points.Length; i++)
            {
                Assert.That(points[i].magnitude, Is.EqualTo(1f).Within(1e-5f));
                Assert.That(points[i].y, Is.EqualTo(0f).Within(1e-6f));
            }
            Assert.That(Vector3.Distance(points[0], Vector3.forward), Is.LessThan(1e-5f));
            Assert.That(Vector3.Distance(points[points.Length / 4], Vector3.right), Is.LessThan(1e-5f));
        }

        [Test]
        public void SnapPositionRoundsEveryAxisToTheGrid()
        {
            Vector3 snapped = BasisSpawnAnchors.SnapPosition(new Vector3(1.12f, -0.4f, 2.87f), 0.25f);
            Assert.That(Vector3.Distance(snapped, new Vector3(1f, -0.5f, 2.75f)), Is.LessThan(1e-5f));
            Vector3 untouched = BasisSpawnAnchors.SnapPosition(new Vector3(1.12f, -0.4f, 2.87f), 0f);
            Assert.That(Vector3.Distance(untouched, new Vector3(1.12f, -0.4f, 2.87f)), Is.LessThan(1e-6f));
        }

        [Test]
        public void SnapRotationRoundsToTheStep()
        {
            Quaternion snapped = BasisSpawnAnchors.SnapRotation(Quaternion.Euler(0f, 37f, 0f), 15f);
            Assert.That(Quaternion.Angle(snapped, Quaternion.Euler(0f, 30f, 0f)), Is.LessThan(0.01f));
            Quaternion untouched = BasisSpawnAnchors.SnapRotation(Quaternion.Euler(0f, 37f, 0f), 0f);
            Assert.That(Quaternion.Angle(untouched, Quaternion.Euler(0f, 37f, 0f)), Is.LessThan(0.01f));
        }

        [Test]
        public void SeatOnSurfaceLiftsThePivotByTheBoundsBottom()
        {
            Vector3 point = new Vector3(1f, 0f, 1f);
            Vector3 seatedAtBottom = PropSpawnPlacement.SeatOnSurface(point, Quaternion.identity, new BasisBounds(new Vector3(0f, 0.5f, 0f), Vector3.one), Vector3.one);
            Assert.That(Vector3.Distance(seatedAtBottom, point), Is.LessThan(1e-5f));

            Vector3 centered = PropSpawnPlacement.SeatOnSurface(point, Quaternion.identity, new BasisBounds(Vector3.zero, Vector3.one), Vector3.one);
            Assert.That(Vector3.Distance(centered, point + Vector3.up * 0.5f), Is.LessThan(1e-5f));

            Vector3 scaled = PropSpawnPlacement.SeatOnSurface(point, Quaternion.identity, new BasisBounds(Vector3.zero, Vector3.one), Vector3.one * 2f);
            Assert.That(Vector3.Distance(scaled, point + Vector3.up), Is.LessThan(1e-5f));

            Vector3 unbounded = PropSpawnPlacement.SeatOnSurface(point, Quaternion.identity, default, Vector3.one);
            Assert.That(Vector3.Distance(unbounded, point), Is.LessThan(1e-5f));
        }
    }
}
