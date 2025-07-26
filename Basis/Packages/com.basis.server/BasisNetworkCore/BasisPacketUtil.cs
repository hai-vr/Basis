using System;
using System.Collections.Generic;
using System.Text;

namespace BasisNetworkCore
{
    public static class BasisPacketUtil
    {
        public static bool ValidatePacket(ushort previous, ushort ValidateMe)
        {
            if (IsNewer(previous, ValidateMe) && previous != ValidateMe)
            {
                return true;
            }
            return false;
        }
        // Returns true if seq1 is newer than seq2
        public static bool IsNewer(ushort seq1, ushort seq2)
        {
            return (byte)(seq1 - seq2) < 128;
        }
    }
}
