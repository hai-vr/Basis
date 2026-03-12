using Basis.Network.Core;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Drivers;
using Basis.Scripts.Networking;
using System;
using System.Collections.Concurrent;
using UnityEngine;
using static SerializableBasis;

/// <summary>
/// Client-side manager for content share spheres.
/// Handles sending/receiving content share messages and managing sphere GameObjects.
/// </summary>
public static class BasisContentShareManager
{
    /// <summary>
    /// All active content share spheres keyed by SphereNetID.
    /// </summary>
    public static ConcurrentDictionary<string, BasisContentSphere> ActiveSpheres =
        new ConcurrentDictionary<string, BasisContentSphere>();

    /// <summary>
    /// Fired when a new content sphere is created (for UI hooks).
    /// </summary>
    public static Action<BasisContentSphere> OnSphereCreated;

    /// <summary>
    /// Fired when a content sphere is removed.
    /// </summary>
    public static Action<string> OnSphereRemoved;

    /// <summary>
    /// Drops a content share sphere in front of the local player.
    /// </summary>
    public static void DropContentSphere(string contentURL, string unlockPassword, ContentShareType contentType)
    {
        if (string.IsNullOrEmpty(contentURL) || string.IsNullOrEmpty(unlockPassword))
        {
            BasisDebug.LogError("Invalid content URL or password for content share.", BasisDebug.LogTag.Networking);
            return;
        }

        // Use camera position for consistent height between local and remote
        Vector3 cameraPos = BasisLocalCameraDriver.Position;
        Vector3 forward = BasisLocalCameraDriver.Forward();
        Vector3 dropPosition = cameraPos + forward * 1.5f;

        ContentShareMessage msg = new ContentShareMessage
        {
            SphereNetID = BasisGenerateUniqueID.GenerateUniqueID(),
            ContentURL = contentURL,
            UnlockPassword = unlockPassword,
            ContentType = contentType,
            PositionX = dropPosition.x,
            PositionY = dropPosition.y,
            PositionZ = dropPosition.z
        };

        NetDataWriter writer = new NetDataWriter();
        msg.Serialize(writer);

        BasisDebug.Log($"Dropping content sphere: {msg.SphereNetID} type={contentType}", BasisDebug.LogTag.Networking);

        BasisNetworkConnection.LocalPlayerPeer?.Send(
            writer,
            BasisNetworkCommons.ContentShareChannel,
            DeliveryMethod.ReliableOrdered
        );
    }

    /// <summary>
    /// Drops a content share sphere using an existing BasisLoadableBundle.
    /// </summary>
    public static void DropContentSphere(BasisLoadableBundle bundle, ContentShareType contentType)
    {
        DropContentSphere(
            bundle.BasisRemoteBundleEncrypted.RemoteBeeFileLocation,
            bundle.UnlockPassword,
            contentType
        );
    }

    /// <summary>
    /// Request removal of a content share sphere.
    /// </summary>
    public static void RequestRemoveSphere(string sphereNetID)
    {
        if (string.IsNullOrEmpty(sphereNetID))
        {
            BasisDebug.LogError("Invalid sphere ID for cleanup.", BasisDebug.LogTag.Networking);
            return;
        }

        ContentShareCleanupMessage msg = new ContentShareCleanupMessage
        {
            SphereNetID = sphereNetID
        };

        NetDataWriter writer = new NetDataWriter();
        msg.Serialize(writer);

        BasisNetworkConnection.LocalPlayerPeer?.Send(
            writer,
            BasisNetworkCommons.ContentShareCleanupChannel,
            DeliveryMethod.ReliableOrdered
        );
    }

    /// <summary>
    /// Called when a content share message is received from the server.
    /// Creates the sphere locally.
    /// </summary>
    public static void HandleContentShareMessage(NetPacketReader reader)
    {
        ServerContentShareMessage serverMsg = new ServerContentShareMessage();
        serverMsg.Deserialize(reader);

        CreateSphere(serverMsg);
    }

    /// <summary>
    /// Called when a content share cleanup message is received from the server.
    /// Removes the sphere locally.
    /// </summary>
    public static void HandleContentShareCleanup(NetPacketReader reader)
    {
        ServerContentShareCleanupMessage serverMsg = new ServerContentShareCleanupMessage();
        serverMsg.Deserialize(reader);

        RemoveSphere(serverMsg.contentShareCleanupMessage.SphereNetID);
    }

    /// <summary>
    /// Creates a content sphere GameObject in the world.
    /// </summary>
    private static void CreateSphere(ServerContentShareMessage serverMsg)
    {
        ContentShareMessage msg = serverMsg.contentShareMessage;

        if (ActiveSpheres.ContainsKey(msg.SphereNetID))
        {
            BasisDebug.LogWarning($"Content sphere already exists locally: {msg.SphereNetID}");
            return;
        }

        Vector3 position = new Vector3(msg.PositionX, msg.PositionY, msg.PositionZ);

        // Create the sphere primitive
        GameObject sphereObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphereObj.name = $"ContentSphere_{msg.SphereNetID}";
        sphereObj.transform.position = position;
        sphereObj.transform.localScale = Vector3.one * 0.3f;

        // Set up physics
        Rigidbody rb = sphereObj.AddComponent<Rigidbody>();
        rb.useGravity = false;
        rb.isKinematic = true;

        // Add the content sphere component
        BasisContentSphere sphere = sphereObj.AddComponent<BasisContentSphere>();
        sphere.Initialize(
            msg.SphereNetID,
            msg.ContentURL,
            msg.UnlockPassword,
            msg.ContentType,
            serverMsg.playerIdMessage.playerID
        );

        // Apply visual based on content type
        ApplySphereVisual(sphereObj, msg.ContentType);

        if (ActiveSpheres.TryAdd(msg.SphereNetID, sphere))
        {
            BasisDebug.Log($"Content sphere created: {msg.SphereNetID} type={msg.ContentType}", BasisDebug.LogTag.Networking);
            OnSphereCreated?.Invoke(sphere);
        }
    }

    /// <summary>
    /// Removes a content sphere from the world.
    /// </summary>
    private static void RemoveSphere(string sphereNetID)
    {
        if (ActiveSpheres.TryRemove(sphereNetID, out BasisContentSphere sphere))
        {
            if (sphere != null && sphere.gameObject != null)
            {
                UnityEngine.Object.Destroy(sphere.gameObject);
            }
            BasisDebug.Log($"Content sphere removed: {sphereNetID}", BasisDebug.LogTag.Networking);
            OnSphereRemoved?.Invoke(sphereNetID);
        }
    }

    /// <summary>
    /// Applies a color to the sphere based on content type.
    /// Avatar = blue, Prop = green, World = orange.
    /// </summary>
    private static void ApplySphereVisual(GameObject sphereObj, ContentShareType contentType)
    {
        Renderer renderer = sphereObj.GetComponent<Renderer>();
        if (renderer == null) return;

        Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.SetFloat("_Surface", 1); // transparent
        mat.SetFloat("_Blend", 0);

        Color color;
        switch (contentType)
        {
            case ContentShareType.Avatar:
                color = new Color(0.3f, 0.5f, 1.0f, 0.8f); // blue
                break;
            case ContentShareType.Prop:
                color = new Color(0.3f, 1.0f, 0.5f, 0.8f); // green
                break;
            case ContentShareType.World:
                color = new Color(1.0f, 0.6f, 0.2f, 0.8f); // orange
                break;
            default:
                color = new Color(1.0f, 1.0f, 1.0f, 0.8f); // white
                break;
        }

        mat.color = color;
        mat.SetFloat("_Metallic", 0f);
        mat.SetFloat("_Smoothness", 1f);
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", Color.white * 0.5f);
        renderer.material = mat;
    }

    /// <summary>
    /// Cleans up all spheres (called on disconnect).
    /// </summary>
    public static void Reset()
    {
        foreach (var kvp in ActiveSpheres)
        {
            if (kvp.Value != null && kvp.Value.gameObject != null)
            {
                UnityEngine.Object.Destroy(kvp.Value.gameObject);
            }
        }
        ActiveSpheres.Clear();
    }
}
