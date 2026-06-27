using System.Collections.Generic;
using Basis.Scripts.Networking.Sync;
using NUnit.Framework;
using UnityEngine;

namespace Basis.Tests.Sync
{
    /// <summary>
    /// Coverage for the Animator-style parameter ids: a name resolves to a stable id once, and the id-based
    /// Set*/Get* overloads drive the same field as the name-based ones without the per-call dictionary lookup.
    /// </summary>
    public class BasisSyncedParametersTests
    {
        [Test]
        public void ParameterId_ResolvesAndDrivesByIdWithoutNameLookup()
        {
            var go = new GameObject("params");
            go.SetActive(false);
            try
            {
                var p = go.AddComponent<BasisSyncedParameters>();
                p.Parameters = new List<BasisSyncedParameter>
                {
                    new BasisSyncedParameter { Name = "Speed", Type = BasisSyncedParameterType.Float },
                    new BasisSyncedParameter { Name = "Aim", Type = BasisSyncedParameterType.Vector3 },
                };
                p.BuildParameterMap();

                int speed = p.GetParameterId("Speed");
                int aim = p.GetParameterId("Aim");
                Assert.AreEqual(0, speed, "first declared parameter -> id 0");
                Assert.AreEqual(1, aim, "second declared parameter -> id 1");
                Assert.AreEqual(-1, p.GetParameterId("Missing"), "unknown name -> -1");
                Assert.IsTrue(p.Has("Speed"));

                // Owner path: LocalSet allocates the local buffer; the id-based reads come straight back.
                p.IsOwnedLocallyOnClient = true;
                p.SetFloat(speed, 4.25f);
                p.SetVector3(aim, new Vector3(1f, 2f, 3f));

                Assert.AreEqual(4.25f, p.GetFloat(speed), 1e-4f, "float by id round-trips");
                Assert.AreEqual(new Vector3(1f, 2f, 3f), p.GetVector3(aim), "vector3 by id round-trips");

                // Id-based and name-based resolve the same field.
                Assert.AreEqual(p.GetFloat("Speed"), p.GetFloat(speed), 1e-4f, "id and name agree");

                // Out-of-range id is a safe no-op / default.
                Assert.DoesNotThrow(() => p.SetFloat(99, 1f));
                Assert.AreEqual(0f, p.GetFloat(99), "out-of-range id returns default");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
