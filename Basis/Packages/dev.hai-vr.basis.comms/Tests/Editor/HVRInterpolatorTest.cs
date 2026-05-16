using NUnit.Framework;

namespace HVR.Basis.Comms.Tests
{
    public class HVRInterpolatorTest
    {
		[Test]
        public void It_should_return_snapshot()
        {
            // Given
	        var sut = new HVRInterpolator();
            sut.Add(new HVRInterpolationSnapshot
            {
                deltaTime = 1f,
                addressIdsToValues = new() { { 2, 3f } }
            });

            // When
            var result = sut.Advance(1f);

            // Then
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(3f, result[2]);
        }

		[Test]
        public void It_should_return_snapshot_once()
        {
            // Given
	        var sut = new HVRInterpolator();
            sut.Add(new HVRInterpolationSnapshot
            {
                deltaTime = 1f,
                addressIdsToValues = new() { { 2, 3f } }
            });

            // When
            sut.Advance(1f);
            var result = sut.Advance(1f);

            // Then
            Assert.AreEqual(0, result.Count);
        }

		[Test]
        public void It_should_return_snapshot_within_delta()
        {
            // Given
	        var sut = new HVRInterpolator();
            sut.Add(new HVRInterpolationSnapshot
            {
                deltaTime = 1f,
                addressIdsToValues = new() { { 2, 3f } }
            });

            // When
            sut.Advance(0.5f);
            var result = sut.Advance(0.5f);

            // Then
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(3f, result[2]);
        }

        [Test]
        public void It_should_not_return_snapshot_outside_delta()
        {
            // Given
            var sut = new HVRInterpolator();
            sut.Add(new HVRInterpolationSnapshot
            {
                deltaTime = 1f,
                addressIdsToValues = new() { { 2, 3f } }
            });

            // When
            sut.Advance(0.5f);
            sut.Advance(0.5f);
            var result = sut.Advance(0.1f);

            // Then
            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void It_should_return_first_snapshot()
        {
            // Given
            var sut = new HVRInterpolator();
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
            var result = sut.Advance(1f);

            // Then
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(3f, result[2]);
        }

        [Test]
        public void It_should_return_second_snapshot()
        {
            // Given
            var sut = new HVRInterpolator();
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
            sut.Advance(1f);
            var result = sut.Advance(2f);

            // Then
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(4f, result[2]);
        }

        [Test]
        public void It_should_return_interpolated_value()
        {
            // Given
            var sut = new HVRInterpolator();
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
            sut.Advance(1f);
            var result = sut.Advance(1f);

            // Then
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(3.5f, result[2], 0.0001f);
        }

        [Test]
        public void It_should_interpolate_immediately()
        {
            // Given
            var sut = new HVRInterpolator();
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
            var result = sut.Advance(2f);

            // Then
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(3.5f, result[2], 0.0001f);
        }
    }
}
