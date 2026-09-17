using System.Collections;
using System.Threading.Tasks;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Networking;
using Basis.Scripts.Networking.Behaviour;
using Basis.Scripts.Networking.NetworkedAvatar;
using Basis.Scripts.Networking.Transmitters;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class BasisNetworkLifeCycleTeardownTests
{
    private sealed class RecordingBehaviour : BasisNetworkAvatarBehaviour
    {
        public int Terminated;
        public bool LastWasLocallyOwned;
        public override void OnNetworkTerminated(bool WasLocallyOwned)
        {
            Terminated++;
            LastWasLocallyOwned = WasLocallyOwned;
        }
    }

    private GameObject avatar;
    private BasisNetworkTransmitter previousTransmitter;
    private NetworkClient previousClient;

    [SetUp]
    public void SetUp()
    {
        previousTransmitter = BasisNetworkManagement.Transmitter;
        previousClient = BasisNetworkConnection.NetworkClient;
        BasisNetworkConnection.NetworkClient = null;
    }

    [TearDown]
    public void TearDown()
    {
        BasisNetworkManagement.Transmitter = previousTransmitter;
        BasisNetworkConnection.NetworkClient = previousClient;
        if (avatar != null)
        {
            Object.DestroyImmediate(avatar);
        }
    }

    private RecordingBehaviour AssignLocalAvatar(out BasisNetworkTransmitter transmitter)
    {
        avatar = new GameObject("LocalAvatar");
        RecordingBehaviour behaviour = avatar.AddComponent<RecordingBehaviour>();
        transmitter = new BasisNetworkTransmitter(7) { Player = new BasisRemotePlayer { IsLocal = true } };
        transmitter.NetworkBehaviours = new BasisNetworkAvatarBehaviour[] { behaviour };
        transmitter.NetworkBehaviourCount = 1;
        behaviour.OnNetworkAssign(0, transmitter);
        BasisNetworkManagement.Transmitter = transmitter;
        return behaviour;
    }

    private static IEnumerator Await(Task task)
    {
        double deadline = Time.realtimeSinceStartupAsDouble + 10.0;
        while (!task.IsCompleted && Time.realtimeSinceStartupAsDouble < deadline)
        {
            yield return null;
        }
        Assert.IsTrue(task.IsCompleted, "RebootManagement did not complete.");
        Assert.IsFalse(task.IsFaulted, task.Exception?.ToString());
    }

    [UnityTest]
    public IEnumerator RebootManagementUnassignsTheLocalAvatarsBehaviours()
    {
        RecordingBehaviour behaviour = AssignLocalAvatar(out BasisNetworkTransmitter transmitter);
        Assert.IsTrue(behaviour.IsInitialized);

        yield return Await(BasisNetworkLifeCycle.RebootManagement(false, null, default));

        Assert.IsFalse(behaviour.IsInitialized, "tearing down the connection must unassign the local avatar's behaviours, as removing a remote player does.");
        Assert.AreEqual(1, behaviour.Terminated);
        Assert.IsTrue(behaviour.LastWasLocallyOwned);
        Assert.IsNull(transmitter.NetworkBehaviours);
        Assert.IsNull(BasisNetworkManagement.Transmitter);
    }

    [UnityTest]
    public IEnumerator ASendAfterRebootManagementNoLongerReachesTheMissingTransmitterPath()
    {
        RecordingBehaviour behaviour = AssignLocalAvatar(out _);
        yield return Await(BasisNetworkLifeCycle.RebootManagement(false, null, default));

        long failedBefore = BasisAdditionalDataDiagnostics.SenderSubmitFailedNoTransmitter;
        long submittedBefore = BasisAdditionalDataDiagnostics.SenderSubmitted;
        LogAssert.ignoreFailingMessages = true;
        try
        {
            behaviour.ServerReductionSystemMessageSend(new byte[] { 1, 2, 3 });
        }
        finally
        {
            LogAssert.ignoreFailingMessages = false;
        }
        Assert.AreEqual(failedBefore, BasisAdditionalDataDiagnostics.SenderSubmitFailedNoTransmitter, "an unassigned behaviour must stop at its own guard instead of reaching the missing transmitter.");
        Assert.AreEqual(submittedBefore, BasisAdditionalDataDiagnostics.SenderSubmitted);
    }
}
