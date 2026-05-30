using Basis.Network.Core;
using Basis.Scripts.Device_Management;
using Basis.Scripts.Networking;

/// <summary>
/// Sends the first sighting of an error/exception to the server on
/// EventsChannel / EventType_ErrorReport. Identity (UUID, display name, platform) is
/// NOT sent — the server attaches it from the peer's connect metadata. Only transmits
/// while the server has reporting enabled (BasisNetworkModeration.CrashReportingEnabled,
/// pushed by the server). Dedup is the caller's job (BasisExceptionNotifier.Seen).
/// </summary>
public static class BasisErrorReportSender
{
    private const int MaxMessageChars = 2000;
    private const int MaxStackChars = 12000;

    public static void Report(byte severity, string system, string message, string stackTrace)
    {
        if (!BasisNetworkModeration.CrashReportingEnabled) return;
        BasisDeviceManagement.EnqueueOnMainThread(() => Send(severity, system, message, stackTrace));
    }

    private static void Send(byte severity, string system, string message, string stackTrace)
    {
        try
        {
            if (!BasisNetworkModeration.CrashReportingEnabled) return;
            if (BasisNetworkConnection.LocalPlayerPeer == null) return;

            byte[] blob = PermissionCompression.CompressExtras(new[]
            {
                system ?? string.Empty,
                Truncate(message, MaxMessageChars),
                Truncate(stackTrace, MaxStackChars),
            });

            NetDataWriter writer = new NetDataWriter();
            writer.Put(BasisNetworkCommons.EventType_ErrorReport);
            writer.Put(severity);
            writer.PutBytesWithLength(blob);
            BasisNetworkConnection.LocalPlayerPeer.Send(
                writer,
                BasisNetworkCommons.EventsChannel,
                DeliveryMethod.ReliableOrdered);
        }
        catch
        {
        }
    }

    private static string Truncate(string value, int max)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Length <= max ? value : value.Substring(0, max);
    }
}
