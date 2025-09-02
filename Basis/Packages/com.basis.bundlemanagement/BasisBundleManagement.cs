using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public static class BasisBundleManagement
{
    public static string BasisMetaExtension = ".BME";
    public static string BasisEncryptedExtension = ".BEE";
    public static string AssetBundlesFolder = "BEEData";

    /// <summary>
    /// Downloads remote BEE, stores it, and returns the platform-matching generated metadata + bundle bytes.
    /// </summary>
    public static async Task<(BasisBundleGenerated Generated, byte[] BundleBytes, string ErrorMessage)> DownloadStoreMetaAndBundle(BasisTrackedBundleWrapper bundleWrapper, BasisProgressReport progressCallback, CancellationToken cancellationToken)
    {
        // Validate inputs up-front with actionable details
        if (!IsValidBundleWrapper(bundleWrapper, out var wrapperErr))
            return (null, null!, wrapperErr);

        string metaUrl = bundleWrapper.LoadableBundle!.BasisRemoteBundleEncrypted!.RemoteBeeFileLocation;
        if (!IsValidUrl(metaUrl, out var urlErr))
            return (null, null!, urlErr);

        if (string.IsNullOrWhiteSpace(bundleWrapper.LoadableBundle.UnlockPassword))
            return (null, null!, "Unlock password is null or empty.");

        if (cancellationToken.IsCancellationRequested)
            return (null, null!, "Cancelled before starting.");

        try
        {
            BasisDebug.Log($"Starting download process for {metaUrl}");

            var result = await BasisIOManagement.DownloadBEEEx( metaUrl, bundleWrapper.LoadableBundle.UnlockPassword!,progressCallback, cancellationToken);

            if (!result.IsSuccess || result.Value is null)
            {
                string reason = BuildResultError("DownloadBEEEx failed", result.Error, result.ResponseCode);
                BasisDebug.LogError(reason);
                return (null, null!, reason);
            }

            var bee = result.Value;
            if (string.IsNullOrWhiteSpace(bee.LocalPath))
            {
                BasisDebug.LogError("Download returned empty local path.");
                return (null, null!, "Download completed but local file path is empty.");
            }

            if (bee.Connector is null)
            {
                BasisDebug.LogError("Connector was null after download.");
                return (null, null!, "Connector is null after download.");
            }

            if (bee.SectionData is null || bee.SectionData.Length == 0)
            {
                BasisDebug.LogError("Section data was null or empty after download.");
                return (null, null!, "Section data is missing after download.");
            }

            // persist references to wrapper
            bundleWrapper.LoadableBundle.BasisBundleConnector = bee.Connector;
            bundleWrapper.LoadableBundle.BasisLocalEncryptedBundle.DownloadedBeeFileLocation = bee.LocalPath;

            BasisDebug.Log($"Parsing downloaded connector & resolving platform bundle from {metaUrl}");

            if (!TryGetPlatform(bundleWrapper.LoadableBundle.BasisBundleConnector, out var generated, out var pfErr))
            {
                string msg = $"Connector loaded, but {pfErr} (platform={Application.platform}).";
                BasisDebug.LogError(msg);
                return (null, null!, msg);
            }

            return (generated!, bee.SectionData, string.Empty);
        }
        catch (OperationCanceledException)
        {
            BasisDebug.LogWarning("DownloadStoreMetaAndBundle cancelled by token.");
            return (null, null!, "Cancelled by token.");
        }
        catch (Exception ex)
        {
            string parse = FormatException("Error during download and processing of meta", ex);
            BasisDebug.LogError(parse);
            return (null, null!, parse);
        }
    }

    /// <summary>
    /// Downloads connector only and returns it.
    /// </summary>
    public static async Task<(BasisBundleConnector Connector, string ErrorMessage)>  DownloadConnectorOnly(BasisTrackedBundleWrapper bundleWrapper, BasisProgressReport progressCallback, CancellationToken cancellationToken)
    {
        if (!IsValidBundleWrapper(bundleWrapper, out var wrapperErr))
            return (null!, wrapperErr);

        string metaUrl = bundleWrapper.LoadableBundle!.BasisRemoteBundleEncrypted!.RemoteBeeFileLocation;
        if (!IsValidUrl(metaUrl, out var urlErr))
            return (null!, urlErr);

        if (string.IsNullOrWhiteSpace(bundleWrapper.LoadableBundle.UnlockPassword))
            return (null!, "Unlock password is null or empty.");

        if (cancellationToken.IsCancellationRequested)
            return (null!, "Cancelled before starting.");

        try
        {
            BasisDebug.Log($"Downloading BEE (connector-only) from {metaUrl}");

            var result = await BasisIOManagement.DownloadConnectorOnlyEx( metaUrl, bundleWrapper.LoadableBundle.UnlockPassword!, progressCallback, cancellationToken);

            if (!result.IsSuccess || result.Value is null)
            {
                string reason = BuildResultError("DownloadConnectorOnlyEx failed", result.Error, result.ResponseCode);
                BasisDebug.LogError(reason);
                return (null!, reason);
            }

            bundleWrapper.LoadableBundle.BasisBundleConnector = result.Value;

            if (!IsValidConnector(result.Value, out var connErr))
                return (null!, connErr);

            BasisDebug.Log("Successfully obtained connector (connector-only).");
            return (result.Value, string.Empty);
        }
        catch (OperationCanceledException)
        {
            BasisDebug.LogWarning("DownloadConnectorOnly cancelled by token.");
            return (null!, "Cancelled by token.");
        }
        catch (Exception ex)
        {
            string parse = FormatException("Error during connector-only download", ex);
            BasisDebug.LogError(parse);
            return (null!, parse);
        }
    }

    /// <summary>
    /// Reads connector and section bytes from an already-downloaded .BEE file.
    /// </summary>
    public static async Task<(BasisBundleGenerated Generated, byte[] BundleBytes, string ErrorMessage)> ProcessOnDiscMetaDataAsync( BasisTrackedBundleWrapper bundleWrapper, BasisStoredEncryptedBundle storedBundle, BasisProgressReport progressCallback, CancellationToken cancellationToken)
    {
        if (!IsValidBundleWrapper(bundleWrapper, out var wrapperErr) || storedBundle is null)
        {
            var msg = wrapperErr ?? "Stored bundle is null.";
            BasisDebug.LogError($"Invalid bundle data. {msg}");
            return (null, null!, "Invalid Bundle Wrapper or stored bundle.");
        }

        if (string.IsNullOrWhiteSpace(storedBundle.DownloadedBeeFileLocation))
            return (null, null!, "Stored bundle path is null or empty.");

        if (string.IsNullOrWhiteSpace(bundleWrapper.LoadableBundle!.UnlockPassword))
            return (null, null!, "Unlock password is null or empty.");

        string metaUrl = bundleWrapper.LoadableBundle.BasisRemoteBundleEncrypted!.RemoteBeeFileLocation;
        if (!IsValidUrl(metaUrl, out var urlErr))
            return (null, null!, urlErr);

        if (cancellationToken.IsCancellationRequested)
            return (null, null!, "Cancelled before starting.");

        try
        {
            BasisDebug.Log($"Processing on-disk meta at {storedBundle.DownloadedBeeFileLocation}");

            var result = await BasisIOManagement.ReadBEEFileEx( storedBundle.DownloadedBeeFileLocation, bundleWrapper.LoadableBundle.UnlockPassword!,  progressCallback, cancellationToken);

            if (!result.IsSuccess || result.Value is null)
            {
                string reason = $"ReadBEEFileEx failed. {result.Error ?? "No details."}";
                BasisDebug.LogError(reason);
                return (null, null!, reason);
            }

            var data = result.Value;
            bundleWrapper.LoadableBundle.BasisBundleConnector = data.Connector;

            if (!IsValidConnector(data.Connector, out var connErr))
            {
                BasisDebug.LogError(connErr);
                return (null, null!, connErr);
            }

            if (data.SectionData is null)
            {
                BasisDebug.LogError("ReadBEEFileEx returned null section data.");
                return (null, null!, "Section data was null.");
            }

            BasisDebug.Log("Successfully processed the Connector and related files.");

            if (!TryGetPlatform(bundleWrapper.LoadableBundle.BasisBundleConnector!, out var generated, out var pfErr))
            {
                string msg = $"Was able to load connector but {pfErr} (platform={Application.platform}).";
                BasisDebug.LogError(msg);
                return (null, null!, msg);
            }

            return (generated!, data.SectionData, string.Empty);
        }
        catch (OperationCanceledException)
        {
            BasisDebug.LogWarning("ProcessOnDiscMetaDataAsync cancelled by token.");
            return (null, null!, "Cancelled by token.");
        }
        catch (Exception ex)
        {
            string parse = FormatException("Error during on-disk meta processing", ex);
            BasisDebug.LogError(parse);
            return (null, null!, parse);
        }
    }

    /// <summary>
    /// Reads only the connector from an already-downloaded .BEE file.
    /// </summary>
    public static async Task<(BasisBundleConnector Connector, string ErrorMessage)> ProcessOnDiscConnectorOnlyAsync( BasisTrackedBundleWrapper bundleWrapper, BasisStoredEncryptedBundle storedBundle, BasisProgressReport progressCallback, CancellationToken cancellationToken)
    {
        if (!IsValidBundleWrapper(bundleWrapper, out var wrapperErr) || storedBundle is null)
            return (null!, wrapperErr ?? "Stored bundle is null.");

        if (string.IsNullOrWhiteSpace(storedBundle.DownloadedBeeFileLocation))
            return (null!, "Stored bundle path is null or empty.");

        if (string.IsNullOrWhiteSpace(bundleWrapper.LoadableBundle!.UnlockPassword))
            return (null!, "Unlock password is null or empty.");

        if (cancellationToken.IsCancellationRequested)
            return (null!, "Cancelled before starting.");

        try
        {
            BasisDebug.Log($"Reading BEE (connector-only) from disk: {storedBundle.DownloadedBeeFileLocation}");

            var result = await BasisIOManagement.ReadBEEFileEx(
                storedBundle.DownloadedBeeFileLocation,
                bundleWrapper.LoadableBundle.UnlockPassword!,
                progressCallback,
                cancellationToken);

            if (!result.IsSuccess || result.Value is null)
            {
                string reason = $"ReadBEEFileEx failed. {result.Error ?? "No details."}";
                BasisDebug.LogError(reason);
                return (null!, reason);
            }

            var data = result.Value;
            bundleWrapper.LoadableBundle.BasisBundleConnector = data.Connector;

            if (!IsValidConnector(data.Connector, out var connErr))
                return (null!, connErr);

            BasisDebug.Log("Successfully recovered connector from disk (connector-only).");
            return (data.Connector, string.Empty);
        }
        catch (OperationCanceledException)
        {
            BasisDebug.LogWarning("ProcessOnDiscConnectorOnlyAsync cancelled by token.");
            return (null!, "Cancelled by token.");
        }
        catch (Exception ex)
        {
            string parse = FormatException("Error during on-disk connector-only processing", ex);
            BasisDebug.LogError(parse);
            return (null!, parse);
        }
    }

    private static bool IsValidBundleWrapper(BasisTrackedBundleWrapper bundleWrapper, out string error)
    {
        error = string.Empty;

        if (bundleWrapper is null)
        {
            error = "BasisTrackedBundleWrapper is null.";
            BasisDebug.LogError(error);
            return false;
        }

        var lb = bundleWrapper.LoadableBundle;
        if (lb is null)
        {
            error = "LoadableBundle is null.";
            BasisDebug.LogError(error);
            return false;
        }

        if (lb.BasisRemoteBundleEncrypted is null)
        {
            error = "BasisRemoteBundleEncrypted is null.";
            BasisDebug.LogError(error);
            return false;
        }

        // Local container should be present; if not, initialize if that's acceptable. Otherwise, flag an error.
        if (lb.BasisLocalEncryptedBundle is null)
        {
            // Not necessarily an error for methods that only download; leave as a warning.
            BasisDebug.Log("BasisLocalEncryptedBundle is null (will be set after download if needed).");
        }

        return true;
    }

    private static bool IsValidUrl(string url, out string error)
    {
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(url))
        {
            error = "URL is null or empty.";
            BasisDebug.LogError(error);
            return false;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            error = $"URL is not a valid absolute URI: '{url}'.";
            BasisDebug.LogError(error);
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            error = $"Unsupported URL scheme '{uri.Scheme}'. Only HTTP/HTTPS are supported.";
            BasisDebug.LogError(error);
            return false;
        }

        return true;
    }

    private static bool IsValidConnector(BasisBundleConnector connector, out string error)
    {
        if (connector is null)
        {
            error = "Failed to decrypt meta file: BasisBundleConnector is null.";
            BasisDebug.LogError(error);
            return false;
        }
        error = string.Empty;
        return true;
    }

    private static bool TryGetPlatform(BasisBundleConnector connector, out BasisBundleGenerated? generated, out string error)
    {
        generated = null;
        error = string.Empty;

        try
        {
            if (connector is null)
            {
                error = "Connector is null.";
                return false;
            }

            if (connector.GetPlatform(out generated))
            {
                if (generated is null)
                {
                    error = "GetPlatform returned true but provided generated == null.";
                    return false;
                }
                return true;
            }

            error = "missing bundle for current platform";
            return false;
        }
        catch (Exception ex)
        {
            error = $"Exception from GetPlatform: {ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    private static string BuildResultError(string prefix, string? inner, long? code)
    {
        string codeStr = code.HasValue ? $" (HTTP {code.Value})" : string.Empty;
        return string.IsNullOrWhiteSpace(inner) ? $"{prefix}{codeStr}." : $"{prefix}{codeStr}: {inner}";
    }

    private static string FormatException(string prefix, Exception ex)
    {
        // Provide succinct but useful details; avoid overly large logs
        string innerMsg = ex.InnerException != null ? $" | Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}" : string.Empty;
        return $"{prefix}: {ex.GetType().Name}: {ex.Message}{innerMsg}\n{ex.StackTrace}";
    }
}
