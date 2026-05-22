using System.Collections.Generic;
using NUnit.Framework;
using static HVR.Basis.Comms.Tests.HVRInterpolatorTest_SingleValue;

namespace HVR.Basis.Comms.Tests
{
    public class HVRInterpolatorTest_SingleValue
    {
        public static Dictionary<int, float> NewDict()
        {
            return new Dictionary<int, float>();
        }

        [Test]
        public void It_should_return_snapshot()
        {
            // Given
	        var sut = new HVRInterpolator(false);
            sut.Add(new HVRInterpolationSnapshot
            {
                deltaTime = 1f,
                addressIdsToValues = new() { { 2, 3f } }
            });

            // When
            var result = sut.Advance(1f, NewDict());

            // Then
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(3f, result[2]);
        }

		[Test]
        public void It_should_return_snapshot_once()
        {
            // Given
	        var sut = new HVRInterpolator(false);
            sut.Add(new HVRInterpolationSnapshot
            {
                deltaTime = 1f,
                addressIdsToValues = new() { { 2, 3f } }
            });

            // When
            sut.Advance(1f, NewDict());
            var result = sut.Advance(1f, NewDict());

            // Then
            Assert.AreEqual(0, result.Count);
        }

		[Test]
        public void It_should_return_snapshot_within_delta()
        {
            // Given
	        var sut = new HVRInterpolator(false);
            sut.Add(new HVRInterpolationSnapshot
            {
                deltaTime = 1f,
                addressIdsToValues = new() { { 2, 3f } }
            });

            // When
            sut.Advance(0.5f, NewDict());
            var result = sut.Advance(0.5f, NewDict());

            // Then
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(3f, result[2]);
        }

        [Test]
        public void It_should_not_return_snapshot_outside_delta()
        {
            // Given
            var sut = new HVRInterpolator(false);
            sut.Add(new HVRInterpolationSnapshot
            {
                deltaTime = 1f,
                addressIdsToValues = new() { { 2, 3f } }
            });

            // When
            sut.Advance(0.5f, NewDict());
            sut.Advance(0.5f, NewDict());
            var result = sut.Advance(0.1f, NewDict());

            // Then
            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void It_should_return_first_snapshot()
        {
            // Given
            var sut = new HVRInterpolator(false);
            sut.Add(new HVRInterpolationSnapshot
            {
                deltaTime = 1f,
                addressIdsToValues = new() { { 2, 3f } }
            });
            sut.Add(new HVRInterpolationSnapshot
            {
                deltaTime = 2f,
                addressIdsToValues = new() { { 2, 4f } }
            });

            // When
            var result = sut.Advance(1f, NewDict());

            // Then
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(3f, result[2]);
        }

        [Test]
        public void It_should_return_second_snapshot()
        {
            // Given
            var sut = new HVRInterpolator(false);
            sut.Add(new HVRInterpolationSnapshot
            {
                deltaTime = 1f,
                addressIdsToValues = new() { { 2, 3f } }
            });
            sut.Add(new HVRInterpolationSnapshot
            {
                deltaTime = 2f,
                addressIdsToValues = new() { { 2, 4f } }
            });

            // When
            sut.Advance(1f, NewDict());
            var result = sut.Advance(2f, NewDict());

            // Then
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(4f, result[2]);
        }

        [Test]
        public void It_should_return_interpolated_value()
        {
            // Given
            var sut = new HVRInterpolator(false);
            sut.Add(new HVRInterpolationSnapshot
            {
                deltaTime = 1f,
                addressIdsToValues = new() { { 2, 3f } }
            });
            sut.Add(new HVRInterpolationSnapshot
            {
                deltaTime = 2f,
                addressIdsToValues = new() { { 2, 4f } }
            });

            // When
            sut.Advance(1f, NewDict());
            var result = sut.Advance(1f, NewDict());

            // Then
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(3.5f, result[2], 0.0001f);
        }

        [Test]
        public void It_should_interpolate_immediately()
        {
            // Given
            var sut = new HVRInterpolator(false);
            sut.Add(new HVRInterpolationSnapshot
            {
                deltaTime = 1f,
                addressIdsToValues = new() { { 2, 3f } }
            });
            sut.Add(new HVRInterpolationSnapshot
            {
                deltaTime = 2f,
                addressIdsToValues = new() { { 2, 4f } }
            });

            // When
            var result = sut.Advance(2f, NewDict());

            // Then
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(3.5f, result[2], 0.0001f);
        }

        [Test]
        public void It_should_interpolate_to_the_third()
        {
            // Given
            var sut = new HVRInterpolator(false);
            sut.Add(new HVRInterpolationSnapshot
            {
                deltaTime = 1f,
                addressIdsToValues = new() { { 2, 3f } }
            });
            sut.Add(new HVRInterpolationSnapshot
            {
                deltaTime = 2f,
                addressIdsToValues = new() { { 2, 4f } }
            });
            sut.Add(new HVRInterpolationSnapshot
            {
                deltaTime = 2f,
                addressIdsToValues = new() { { 2, 0f } }
            });

            // When
            var result = sut.Advance(4f, NewDict());

            // Then
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(2f, result[2], 0.0001f);
        }

        [Test]
        public void It_should_interpolate_to_the_end()
        {
            // Given
            var sut = new HVRInterpolator(false);
            sut.Add(new HVRInterpolationSnapshot
            {
                deltaTime = 1f,
                addressIdsToValues = new() { { 2, 3f } }
            });
            sut.Add(new HVRInterpolationSnapshot
            {
                deltaTime = 2f,
                addressIdsToValues = new() { { 2, 4f } }
            });
            sut.Add(new HVRInterpolationSnapshot
            {
                deltaTime = 2f,
                addressIdsToValues = new() { { 2, 0f } }
            });

            // When & Then
            Assert.AreEqual(3f, sut.Advance(0.1f, NewDict())[2], 0.0001f);
            Assert.AreEqual(3f, sut.Advance(0.1f, NewDict())[2], 0.0001f);
            Assert.AreEqual(3f, sut.Advance(0.1f, NewDict())[2], 0.0001f);
            Assert.AreEqual(3f, sut.Advance(0.1f, NewDict())[2], 0.0001f);
            Assert.AreEqual(3f, sut.Advance(0.1f, NewDict())[2], 0.0001f);
            Assert.AreEqual(3f, sut.Advance(0.1f, NewDict())[2], 0.0001f);
            Assert.AreEqual(3f, sut.Advance(0.1f, NewDict())[2], 0.0001f);
            Assert.AreEqual(3f, sut.Advance(0.1f, NewDict())[2], 0.0001f);
            Assert.AreEqual(3f, sut.Advance(0.1f, NewDict())[2], 0.0001f);
            Assert.AreEqual(3f, sut.Advance(0.1f, NewDict())[2], 0.0001f);

            Assert.AreEqual(3.05f, sut.Advance(0.1f, NewDict())[2], 0.0001f);
            Assert.AreEqual(3.10f, sut.Advance(0.1f, NewDict())[2], 0.0001f);
            Assert.AreEqual(3.15f, sut.Advance(0.1f, NewDict())[2], 0.0001f);
            Assert.AreEqual(3.20f, sut.Advance(0.1f, NewDict())[2], 0.0001f);
            Assert.AreEqual(3.25f, sut.Advance(0.1f, NewDict())[2], 0.0001f);
            Assert.AreEqual(3.30f, sut.Advance(0.1f, NewDict())[2], 0.0001f);
            Assert.AreEqual(3.35f, sut.Advance(0.1f, NewDict())[2], 0.0001f);
            Assert.AreEqual(3.40f, sut.Advance(0.1f, NewDict())[2], 0.0001f);
            Assert.AreEqual(3.45f, sut.Advance(0.1f, NewDict())[2], 0.0001f);
            Assert.AreEqual(3.50f, sut.Advance(0.1f, NewDict())[2], 0.0001f);
            Assert.AreEqual(3.55f, sut.Advance(0.1f, NewDict())[2], 0.0001f);
            Assert.AreEqual(3.60f, sut.Advance(0.1f, NewDict())[2], 0.0001f);
            Assert.AreEqual(3.65f, sut.Advance(0.1f, NewDict())[2], 0.0001f);
            Assert.AreEqual(3.70f, sut.Advance(0.1f, NewDict())[2], 0.0001f);
            Assert.AreEqual(3.75f, sut.Advance(0.1f, NewDict())[2], 0.0001f);
            Assert.AreEqual(3.80f, sut.Advance(0.1f, NewDict())[2], 0.0001f);
            Assert.AreEqual(3.85f, sut.Advance(0.1f, NewDict())[2], 0.0001f);
            Assert.AreEqual(3.90f, sut.Advance(0.1f, NewDict())[2], 0.0001f);
            Assert.AreEqual(3.95f, sut.Advance(0.1f, NewDict())[2], 0.0001f);
            Assert.AreEqual(4.00f, sut.Advance(0.1f, NewDict())[2], 0.0001f);

            Assert.AreEqual(3.8f, sut.Advance(0.1f, NewDict())[2], 0.0001f);
            Assert.AreEqual(3.6f, sut.Advance(0.1f, NewDict())[2], 0.0001f);
            Assert.AreEqual(3.4f, sut.Advance(0.1f, NewDict())[2], 0.0001f);
            Assert.AreEqual(3.2f, sut.Advance(0.1f, NewDict())[2], 0.0001f);
            Assert.AreEqual(3.0f, sut.Advance(0.1f, NewDict())[2], 0.0001f);
            Assert.AreEqual(2.8f, sut.Advance(0.1f, NewDict())[2], 0.0001f);
            Assert.AreEqual(2.6f, sut.Advance(0.1f, NewDict())[2], 0.0001f);
            Assert.AreEqual(2.4f, sut.Advance(0.1f, NewDict())[2], 0.0001f);
            Assert.AreEqual(2.2f, sut.Advance(0.1f, NewDict())[2], 0.0001f);
            Assert.AreEqual(2.0f, sut.Advance(0.1f, NewDict())[2], 0.0001f);
            Assert.AreEqual(1.8f, sut.Advance(0.1f, NewDict())[2], 0.0001f);
            Assert.AreEqual(1.6f, sut.Advance(0.1f, NewDict())[2], 0.0001f);
            Assert.AreEqual(1.4f, sut.Advance(0.1f, NewDict())[2], 0.0001f);
            Assert.AreEqual(1.2f, sut.Advance(0.1f, NewDict())[2], 0.0001f);
            Assert.AreEqual(1.0f, sut.Advance(0.1f, NewDict())[2], 0.0001f);
            Assert.AreEqual(0.8f, sut.Advance(0.1f, NewDict())[2], 0.0001f);
            Assert.AreEqual(0.6f, sut.Advance(0.1f, NewDict())[2], 0.0001f);
            Assert.AreEqual(0.4f, sut.Advance(0.1f, NewDict())[2], 0.0001f);
            Assert.AreEqual(0.2f, sut.Advance(0.1f, NewDict())[2], 0.0001f);
            Assert.AreEqual(0.0f, sut.Advance(0.1f, NewDict())[2], 0.0001f);

            Assert.AreEqual(0, sut.Advance(0.1f, NewDict()).Count);
        }
    }

    public class HVRInterpolatorTest_MultiValue
    {
        [Test]
        public void It_should_return_snapshot()
        {
            // Given
            var sut = new HVRInterpolator(false);
            sut.Add(new HVRInterpolationSnapshot
            {
                deltaTime = 1f,
                addressIdsToValues = new() { { 2, 3f } }
            });
            sut.Add(new HVRInterpolationSnapshot
            {
                deltaTime = 1f,
                addressIdsToValues = new() { { 3, 4f } }
            });

            // When
            var r0 = sut.Advance(0.5f, NewDict());
            var r1 = sut.Advance(0.5f, NewDict());
            var r2 = sut.Advance(0.5f, NewDict());
            var r3 = sut.Advance(0.5f, NewDict());
            var r4 = sut.Advance(0.5f, NewDict());

            // Then
            Assert.AreEqual(1, r0.Count);
            Assert.AreEqual(3f, r0[2]);
            Assert.AreEqual(2, r1.Count); // This is the tricky part.
            Assert.AreEqual(3f, r1[2]);
            Assert.AreEqual(4f, r1[3]);
            Assert.AreEqual(1, r2.Count);
            Assert.AreEqual(4f, r2[3]);
            Assert.AreEqual(1, r3.Count);
            Assert.AreEqual(4f, r3[3]);
            Assert.AreEqual(0, r4.Count);
        }

        [Test]
        public void It_should_return_both_snapshots()
        {
            // Given
            var sut = new HVRInterpolator(false);
            sut.Add(new HVRInterpolationSnapshot
            {
                deltaTime = 1f,
                addressIdsToValues = new() { { 2, 3f } }
            });
            sut.Add(new HVRInterpolationSnapshot
            {
                deltaTime = 1f,
                addressIdsToValues = new() { { 3, 4f } }
            });

            // When
            var result = sut.Advance(1f, NewDict());

            // Then
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual(3f, result[2]);
            Assert.AreEqual(4f, result[3]);
        }

        [Test]
        public void It_should_interpolate_between_distant_snapshots()
        {
            // Given
            var sut = new HVRInterpolator(false);
            sut.Add(new HVRInterpolationSnapshot
            {
                deltaTime = 1f,
                addressIdsToValues = new() { { 2, 3f } }
            });
            sut.Add(new HVRInterpolationSnapshot
            {
                deltaTime = 1f,
                addressIdsToValues = new() { { 3, 4f } }
            });
            sut.Add(new HVRInterpolationSnapshot
            {
                deltaTime = 1f,
                addressIdsToValues = new() { { 2, 4f } }
            });

            // When
            sut.Advance(1f, NewDict());
            sut.Advance(1f, NewDict());
            var result = sut.Advance(0.5f, NewDict());

            // Then
            Assert.AreEqual(3.5f, result[2], 0.0001f);
        }
    }

    public class HVRInterpolatorTest_CatchUp
    {
        [Test]
        public void It_should_not_catch_up()
        {
            // Given
            var sut = new HVRInterpolator(true);
            sut.Add(new HVRInterpolationSnapshot
            {
                deltaTime = 1f,
                addressIdsToValues = new() { { 2, 3f } }
            });
            sut.Advance(1f, NewDict()); // This effectively sets a value to remember.
            sut.Advance(10f, NewDict());

            sut.Add(new HVRInterpolationSnapshot
            {
                deltaTime = 0.18f,
                addressIdsToValues = new() { { 2, 4f } }
            });

            // When
            var result = sut.Advance(0.09f, NewDict());

            // Then
            Assert.AreEqual(3.5f, result[2], 0.0001f);
        }
        [Test]
        public void It_should_catch_up_slow()
        {
            // Given
            var sut = new HVRInterpolator(true);
            sut.Add(new HVRInterpolationSnapshot
            {
                deltaTime = 1f,
                addressIdsToValues = new() { { 2, 3f } }
            });
            sut.Advance(1f, NewDict()); // This effectively sets a value to remember.
            sut.Advance(10f, NewDict());

            sut.Add(new HVRInterpolationSnapshot
            {
                deltaTime = 0.3f,
                addressIdsToValues = new() { { 2, 4f } }
            });

            // When
            var result = sut.Advance(0.15f, NewDict());

            // Then
            Assert.AreEqual(3.7764608860015869f, result[2], 0.0001f);
        }

        [Test]
        public void It_should_catch_up_fast()
        {
            // Given
            var sut = new HVRInterpolator(true);
            sut.Add(new HVRInterpolationSnapshot
            {
                deltaTime = 1f,
                addressIdsToValues = new() { { 2, 3f } }
            });
            sut.Advance(1f, NewDict()); // This effectively sets a value to remember.
            sut.Advance(10f, NewDict());

            sut.Add(new HVRInterpolationSnapshot
            {
                deltaTime = 1f,
                addressIdsToValues = new() { { 2, 4f } }
            });

            // When
            var result = sut.Advance(0.5f, NewDict());

            // Then
            Assert.AreEqual(3.940593957901001f, result[2], 0.0001f);
        }
    }
}
