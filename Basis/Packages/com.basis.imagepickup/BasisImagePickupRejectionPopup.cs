using System.IO;
using System.Text;
using Basis.BasisUI;
using UnityEngine;

namespace Basis.ImagePickup
{
    internal static class BasisImagePickupRejectionPopup
    {
        private const string RejectionTitleKey = "imagePickup.popup.rejected.title";
        private const string LimitTitleKey = "imagePickup.popup.limit.title";
        private const string BatchNoticeTitleKey =
            "imagePickup.popup.batchNotice.title";
        private const string AnimatedBatchTitleKey =
            "imagePickup.popup.animatedBatch.title";
        private const string AcceptLabelKey = "imagePickup.popup.accept";
        private const string UnknownFileKey = "imagePickup.popup.unknownFile";
        private const string DefaultReasonKey = "imagePickup.popup.defaultReason";
        private const string RejectionDescriptionKey =
            "imagePickup.popup.rejection.description";
        private const string LimitSummaryKey = "imagePickup.popup.limit.summary";
        private const string LimitAllowedSomeKey =
            "imagePickup.popup.limit.allowedSome";
        private const string LimitNoneImportedKey =
            "imagePickup.popup.limit.noneImported";
        private const string AnimatedWarningKey = "imagePickup.popup.animated.warning";

        public static void Show(string path, string reason)
        {
            ShowDialogue(BasisLocalization.Get(RejectionTitleKey), BuildDescription(path, reason));
        }

        public static void ShowImageLimit(int currentCount, int requestedCount)
        {
            ShowDialogue(BasisLocalization.Get(LimitTitleKey), BuildBatchNotice(currentCount, requestedCount, 0, 0));
        }

        public static void ShowBatchNotice(int currentCount, int requestedCount, int allowedCount, int animatedCount)
        {
            bool limited = allowedCount < requestedCount;
            bool animationWarning =
                animatedCount > BasisImagePickupSettings.AnimationBatchWarningThreshold;
            if (!limited && !animationWarning)
                return;

            string titleKey =
                limited && animationWarning ? BatchNoticeTitleKey
                : limited ? LimitTitleKey
                : AnimatedBatchTitleKey;
            string title = BasisLocalization.Get(titleKey);
            ShowDialogue(title, BuildBatchNotice(currentCount, requestedCount, allowedCount, animatedCount));
        }

        internal static string BuildDescription(string path, string reason)
        {
            string fileName;
            try
            {
                fileName = Path.GetFileName(path);
            }
            catch
            {
                fileName = path;
            }

            if (string.IsNullOrWhiteSpace(fileName))
                fileName = BasisLocalization.Get(UnknownFileKey);
            if (string.IsNullOrWhiteSpace(reason))
                reason = BasisLocalization.Get(DefaultReasonKey);

            return BasisLocalization.Get(RejectionDescriptionKey, EscapeRichText(fileName), EscapeRichText(reason));
        }

        internal static string BuildBatchNotice(
            int currentCount,
            int requestedCount,
            int allowedCount,
            int animatedCount
        )
        {
            int limit = BasisImagePickupSettings.MaxConcurrentImagesPerSender;
            currentCount = Mathf.Max(0, currentCount);
            requestedCount = Mathf.Max(0, requestedCount);
            allowedCount = Mathf.Clamp(allowedCount, 0, requestedCount);
            animatedCount = Mathf.Max(0, animatedCount);

            var description = new StringBuilder(384);
            if (allowedCount < requestedCount)
            {
                description.Append(BasisLocalization.Get(LimitSummaryKey, limit, currentCount));

                if (allowedCount > 0)
                {
                    description.Append(BasisLocalization.Get(LimitAllowedSomeKey, allowedCount, requestedCount));
                }
                else
                {
                    description.Append(BasisLocalization.Get(LimitNoneImportedKey));
                }
            }

            if (animatedCount > BasisImagePickupSettings.AnimationBatchWarningThreshold)
            {
                if (description.Length > 0)
                    description.Append("\n\n");
                description.Append(BasisLocalization.Get(AnimatedWarningKey, animatedCount));
            }

            return description.ToString();
        }

        private static void ShowDialogue(string title, string description)
        {
            bool menuWasAlreadyOpen = BasisMainMenu.Instance != null;

            if (!menuWasAlreadyOpen)
            {
                BasisMainMenu.Open();
            }
            if (BasisMainMenu.Instance == null)
            {
                return;
            }

            if (BasisMainMenu.Instance.Dialogue)
            {
                BasisMainMenu.Instance.Dialogue.ReleaseInstance();
            }

            BasisMainMenu.Instance.OpenDialogue(
                title,
                description,
                BasisLocalization.Get(AcceptLabelKey),
                _ =>
                {
                    if (!menuWasAlreadyOpen)
                    {
                        BasisMainMenu.Close();
                    }
                },
                category: BasisNotificationCategory.Content
            );
        }

        private static string EscapeRichText(string value)
        {
            return value
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;");
        }
    }
}
