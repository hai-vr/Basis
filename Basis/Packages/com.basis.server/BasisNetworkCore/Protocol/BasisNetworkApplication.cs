using System;
namespace Basis.Network.Core
{
    public static class BasisNetworkApplication
    {
        public const string DefaultCompanyName = "Basis Unity";
        public const string DefaultProductName = "Basis Unity";
        public const int MaxNameLength = 64;
        private const byte TagRaw = 0;
        private const byte TagDefault = 1;
        public static bool Matches(string acceptedCompanyName, string acceptedProductName, string companyName, string productName)
        {
            return string.Equals(companyName, acceptedCompanyName, StringComparison.Ordinal) && string.Equals(productName, acceptedProductName, StringComparison.Ordinal);
        }
        public static void Write(NetDataWriter writer, string companyName, string productName)
        {
            if (Matches(DefaultCompanyName, DefaultProductName, companyName, productName))
            {
                writer.Put(TagDefault);
                return;
            }
            writer.Put(TagRaw);
            writer.Put(companyName ?? string.Empty, MaxNameLength);
            writer.Put(productName ?? string.Empty, MaxNameLength);
        }
        public static bool TryRead(NetDataReader reader, out string companyName, out string productName)
        {
            companyName = null;
            productName = null;
            if (!reader.TryGetByte(out byte tag))
            {
                return false;
            }
            switch (tag)
            {
                case TagDefault:
                    companyName = DefaultCompanyName;
                    productName = DefaultProductName;
                    return true;
                case TagRaw:
                    return reader.TryGetString(out companyName) && reader.TryGetString(out productName);
                default:
                    return false;
            }
        }
        public static string UnsupportedReason(string acceptedCompanyName, string acceptedProductName, string companyName, string productName)
        {
            return $"This server only accepts company \"{Describe(acceptedCompanyName)}\" and product \"{Describe(acceptedProductName)}\"; your client reports company \"{Describe(companyName)}\" and product \"{Describe(productName)}\".";
        }
        private static string Describe(string value)
        {
            string clean = BasisDisplayNameSanitizer.Sanitize(value);
            if (clean.Length == 0)
            {
                return "none";
            }
            return clean.Length > MaxNameLength ? clean.Substring(0, MaxNameLength) : clean;
        }
    }
}
