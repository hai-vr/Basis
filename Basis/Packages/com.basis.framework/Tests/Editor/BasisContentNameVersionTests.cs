using Basis.BasisUI;
using NUnit.Framework;

namespace Basis.Tests.UI
{
    /// <summary>
    /// Covers the name-based version grouping the library falls back to when content carries no
    /// authored ContentGroupId — i.e. everything built before that field existed, which is exactly
    /// the content creators have been re-uploading by hand as "My Avatar v2".
    ///
    /// <para>It is a heuristic over creator-chosen text, so the cases that matter most are the ones
    /// where it must NOT fire: a name that merely ends in a digit-bearing word, and a name that
    /// would be left empty by stripping.</para>
    /// </summary>
    public class BasisContentNameVersionTests
    {
        // ---- version tokens that should be recognised ----

        [TestCase("My Avatar v2", "My Avatar")]
        [TestCase("My Avatar V2", "My Avatar")]
        [TestCase("My Avatar v1.4", "My Avatar")]
        [TestCase("My Avatar_v3", "My Avatar")]
        [TestCase("My Avatar-v3", "My Avatar")]
        [TestCase("My Avatar (2)", "My Avatar")]
        [TestCase("My Avatar [v3]", "My Avatar")]
        [TestCase("My Avatar 2", "My Avatar")]
        [TestCase("My Avatar - 3", "My Avatar")]
        [TestCase("My Avatar_2", "My Avatar")]
        [TestCase("Dooly Sailor3", "Dooly Sailor")]
        [TestCase("My Avatar v2 (3)", "My Avatar")]
        [TestCase("  My Avatar v2  ", "My Avatar")]
        public void StripVersionSuffix_RemovesTrailingVersion(string name, string expected)
        {
            Assert.That(BasisContentNameVersion.StripVersionSuffix(name), Is.EqualTo(expected));
        }

        // ---- and the ones it must leave alone ----

        [TestCase("My Avatar")]
        [TestCase("Half Life 3 Confirmed")]
        [TestCase("v2")]
        [TestCase("2024")]
        [TestCase("3")]
        public void StripVersionSuffix_LeavesNonVersionNamesIntact(string name)
        {
            Assert.That(BasisContentNameVersion.StripVersionSuffix(name), Is.EqualTo(name.Trim()),
                "stripping here would either invent a grouping or leave nothing to group by.");
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void StripVersionSuffix_HandlesEmptyNames(string name)
        {
            Assert.That(BasisContentNameVersion.StripVersionSuffix(name), Is.EqualTo(string.Empty));
        }

        // ---- grouping ----

        [TestCase("My Avatar", "My Avatar v2")]
        [TestCase("My Avatar v1", "My Avatar v2")]
        [TestCase("My  Avatar v1", "my avatar")]
        [TestCase("MY AVATAR (4)", "My Avatar")]
        [TestCase("Dooly Sailor3", "Dooly Sailor 4")]
        public void GroupKeyFromName_GroupsVersionsOfTheSameContent(string left, string right)
        {
            Assert.That(BasisContentNameVersion.GroupKeyFromName(left),
                Is.EqualTo(BasisContentNameVersion.GroupKeyFromName(right)).And.Not.Empty);
        }

        [TestCase("My Avatar", "Your Avatar")]
        [TestCase("Cat", "Cat Ears")]
        [TestCase("Half Life 3 Confirmed", "Half Life")]
        public void GroupKeyFromName_KeepsDifferentContentApart(string left, string right)
        {
            Assert.That(BasisContentNameVersion.GroupKeyFromName(left),
                Is.Not.EqualTo(BasisContentNameVersion.GroupKeyFromName(right)));
        }

        [TestCase(null)]
        [TestCase("")]
        public void GroupKeyFromName_IsEmptyForUnusableNames(string name)
        {
            Assert.That(BasisContentNameVersion.GroupKeyFromName(name), Is.Empty,
                "an empty key must not become a bucket that every nameless entry falls into.");
        }

        // ---- ordering within a stack ----

        [Test]
        public void CompareVersionDescending_PutsHigherVersionsFirst()
        {
            Assert.That(BasisContentNameVersion.CompareVersionDescending("A v3", "A v2"), Is.LessThan(0));
            Assert.That(BasisContentNameVersion.CompareVersionDescending("A v2", "A v3"), Is.GreaterThan(0));
            Assert.That(BasisContentNameVersion.CompareVersionDescending("A v2", "A v2"), Is.EqualTo(0));
        }

        [Test]
        public void CompareVersionDescending_ComparesNumericallyNotAlphabetically()
        {
            Assert.That(BasisContentNameVersion.CompareVersionDescending("A v10", "A v9"), Is.LessThan(0),
                "v10 is newer than v9 even though it sorts earlier as text.");
        }

        [Test]
        public void CompareVersionDescending_HandlesDottedVersions()
        {
            Assert.That(BasisContentNameVersion.CompareVersionDescending("A v1.10", "A v1.9"), Is.LessThan(0));
            Assert.That(BasisContentNameVersion.CompareVersionDescending("A v1.1", "A v1"), Is.LessThan(0));
        }

        [Test]
        public void CompareVersionDescending_SortsUnversionedNamesLast()
        {
            Assert.That(BasisContentNameVersion.CompareVersionDescending("A", "A v2"), Is.GreaterThan(0),
                "an unversioned name reads as the original upload, so it belongs behind v2.");
            Assert.That(BasisContentNameVersion.CompareVersionDescending("A v2", "A"), Is.LessThan(0));
            Assert.That(BasisContentNameVersion.CompareVersionDescending("A", "B"), Is.EqualTo(0));
        }

        [Test]
        public void ExtractVersion_ReadsNumericComponents()
        {
            Assert.That(BasisContentNameVersion.ExtractVersion("A v1.4"), Is.EqualTo(new[] { 1, 4 }));
            Assert.That(BasisContentNameVersion.ExtractVersion("A (12)"), Is.EqualTo(new[] { 12 }));
            Assert.That(BasisContentNameVersion.ExtractVersion("A"), Is.Empty);
        }
    }
}
