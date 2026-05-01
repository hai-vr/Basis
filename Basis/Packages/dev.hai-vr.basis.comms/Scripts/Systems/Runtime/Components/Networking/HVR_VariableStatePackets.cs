using System;
using System.Collections.Generic;
using System.Text;

namespace HVR.Basis.Comms
{
    public class HVR_VariableState_NewVariables
    {
        public readonly byte packetType = AvatarMessageProcessing.NewNet_WearerSubmitsNewVariables;
        public List<HVR_VariableState_NewVariable> newGeneralVariables;
        public List<HVR_VariableState_NewQuickVariable> floatZero;
        public List<HVR_VariableState_NewQuickVariable> floatOne;

        public class HVR_VariableState_NewVariable
        {
            public string address;
            public ushort networkId;
            public byte variableTypeCode;
            public object initialValue;
        }

        public class HVR_VariableState_NewQuickVariable
        {
            public string address;
            public ushort networkId;
        }

        public byte[] Serialize()
        {
            var totalLength = 1 + 2; // packetType + countGeneral
            var addressBytesList = new List<byte[]>(newGeneralVariables.Count);
            foreach (var newVariableToCreate in newGeneralVariables)
            {
                var addressBytes = Encoding.UTF8.GetBytes(newVariableToCreate.address);
                addressBytesList.Add(addressBytes);

                totalLength += 2 + addressBytes.Length + 2 + 1 + 4;
            }

            totalLength += 2; // countFloatZero
            var floatZeroAddressBytesList = new List<byte[]>(floatZero.Count);
            foreach (var quickVar in floatZero)
            {
                var addressBytes = Encoding.UTF8.GetBytes(quickVar.address);
                floatZeroAddressBytesList.Add(addressBytes);
                totalLength += 2 + addressBytes.Length + 2;
            }

            totalLength += 2; // countFloatOne
            var floatOneAddressBytesList = new List<byte[]>(floatOne.Count);
            foreach (var quickVar in floatOne)
            {
                var addressBytes = Encoding.UTF8.GetBytes(quickVar.address);
                floatOneAddressBytesList.Add(addressBytes);
                totalLength += 2 + addressBytes.Length + 2;
            }

            var result = new byte[totalLength];
            result[0] = packetType;
            var offset = 1;

            // General Variables
            var countGeneral = (ushort)newGeneralVariables.Count;
            result[offset++] = (byte)(countGeneral & 0xFF);
            result[offset++] = (byte)((countGeneral >> 8) & 0xFF);

            for (var i = 0; i < newGeneralVariables.Count; i++)
            {
                var holder = newGeneralVariables[i];
                var addressBytes = addressBytesList[i];

                var m0_addressLength = (ushort)addressBytes.Length;
                var m1_addressBytes = addressBytes;
                var m1b_networkId = holder.networkId;
                var m2_variableTypeCode = holder.variableTypeCode;
                var m3_initialValue = (float)holder.initialValue;

                // Address length (ushort - 2 bytes)
                result[offset++] = (byte)(m0_addressLength & 0xFF);
                result[offset++] = (byte)((m0_addressLength >> 8) & 0xFF);

                // Address bytes
                Buffer.BlockCopy(m1_addressBytes, 0, result, offset, m1_addressBytes.Length);
                offset += m1_addressBytes.Length;

                // Network ID (ushort - 2 bytes)
                result[offset++] = (byte)(m1b_networkId & 0xFF);
                result[offset++] = (byte)((m1b_networkId >> 8) & 0xFF);

                // Variable type code (byte - 1 byte)
                result[offset++] = m2_variableTypeCode;

                // Initial value (float - 4 bytes)
                var valueBytes = BitConverter.GetBytes(m3_initialValue);
                Buffer.BlockCopy(valueBytes, 0, result, offset, 4);
                offset += 4;
            }

            // Float Zero Variables
            var countZero = (ushort)floatZero.Count;
            result[offset++] = (byte)(countZero & 0xFF);
            result[offset++] = (byte)((countZero >> 8) & 0xFF);

            for (var i = 0; i < floatZero.Count; i++)
            {
                var holder = floatZero[i];
                var addressBytes = floatZeroAddressBytesList[i];

                var addressLength = (ushort)addressBytes.Length;
                result[offset++] = (byte)(addressLength & 0xFF);
                result[offset++] = (byte)((addressLength >> 8) & 0xFF);

                Buffer.BlockCopy(addressBytes, 0, result, offset, addressBytes.Length);
                offset += addressBytes.Length;

                result[offset++] = (byte)(holder.networkId & 0xFF);
                result[offset++] = (byte)((holder.networkId >> 8) & 0xFF);
            }

            // Float One Variables
            var countOne = (ushort)floatOne.Count;
            result[offset++] = (byte)(countOne & 0xFF);
            result[offset++] = (byte)((countOne >> 8) & 0xFF);

            for (var i = 0; i < floatOne.Count; i++)
            {
                var holder = floatOne[i];
                var addressBytes = floatOneAddressBytesList[i];

                var addressLength = (ushort)addressBytes.Length;
                result[offset++] = (byte)(addressLength & 0xFF);
                result[offset++] = (byte)((addressLength >> 8) & 0xFF);

                Buffer.BlockCopy(addressBytes, 0, result, offset, addressBytes.Length);
                offset += addressBytes.Length;

                result[offset++] = (byte)(holder.networkId & 0xFF);
                result[offset++] = (byte)((holder.networkId >> 8) & 0xFF);
            }

            return result;
        }

        public static HVR_VariableState_NewVariables Deserialize(byte[] data)
        {
            if (data.Length < 3) return null;
            var packet = new HVR_VariableState_NewVariables
            {
                newGeneralVariables = new List<HVR_VariableState_NewVariable>(),
                floatZero = new List<HVR_VariableState_NewQuickVariable>(),
                floatOne = new List<HVR_VariableState_NewQuickVariable>()
            };

            var offset = 1;

            // General Variables
            if (offset + 2 > data.Length) return packet;
            var countGeneral = (ushort)(data[offset] | (data[offset + 1] << 8));
            offset += 2;

            for (var i = 0; i < countGeneral; i++)
            {
                if (offset + 2 > data.Length) break;
                var addressLength = (ushort)(data[offset] | (data[offset + 1] << 8));
                offset += 2;

                if (offset + addressLength > data.Length) break;
                var address = Encoding.UTF8.GetString(data, offset, addressLength);
                offset += addressLength;

                if (offset + 2 > data.Length) break;
                var networkId = (ushort)(data[offset] | (data[offset + 1] << 8));
                offset += 2;

                if (offset + 1 > data.Length) break;
                var variableTypeCode = data[offset++];

                if (offset + 4 > data.Length) break;
                var initialValue = BitConverter.ToSingle(data, offset);
                offset += 4;

                packet.newGeneralVariables.Add(new HVR_VariableState_NewVariable
                {
                    address = address,
                    networkId = networkId,
                    variableTypeCode = variableTypeCode,
                    initialValue = initialValue
                });
            }

            // Float Zero Variables
            if (offset + 2 > data.Length) return packet;
            var countZero = (ushort)(data[offset] | (data[offset + 1] << 8));
            offset += 2;

            for (var i = 0; i < countZero; i++)
            {
                if (offset + 2 > data.Length) break;
                var addressLength = (ushort)(data[offset] | (data[offset + 1] << 8));
                offset += 2;

                if (offset + addressLength > data.Length) break;
                var address = Encoding.UTF8.GetString(data, offset, addressLength);
                offset += addressLength;

                if (offset + 2 > data.Length) break;
                var networkId = (ushort)(data[offset] | (data[offset + 1] << 8));
                offset += 2;

                packet.floatZero.Add(new HVR_VariableState_NewQuickVariable
                {
                    address = address,
                    networkId = networkId
                });
            }

            // Float One Variables
            if (offset + 2 > data.Length) return packet;
            var countOne = (ushort)(data[offset] | (data[offset + 1] << 8));
            offset += 2;

            for (var i = 0; i < countOne; i++)
            {
                if (offset + 2 > data.Length) break;
                var addressLength = (ushort)(data[offset] | (data[offset + 1] << 8));
                offset += 2;

                if (offset + addressLength > data.Length) break;
                var address = Encoding.UTF8.GetString(data, offset, addressLength);
                offset += addressLength;

                if (offset + 2 > data.Length) break;
                var networkId = (ushort)(data[offset] | (data[offset + 1] << 8));
                offset += 2;

                packet.floatOne.Add(new HVR_VariableState_NewQuickVariable
                {
                    address = address,
                    networkId = networkId
                });
            }

            return packet;
        }
    }

    public class HVR_VariableState_UpdatedVariables_ZeroesOrOnes
    {
        public byte packetType;
        public List<ushort> networkIds;

        public byte[] Serialize()
        {
            var totalLength = 1 + 2; // packetType + count
            totalLength += networkIds.Count * 2; // each networkId is ushort (2 bytes)

            var result = new byte[totalLength];
            result[0] = packetType;
            var offset = 1;

            var count = (ushort)networkIds.Count;
            result[offset++] = (byte)(count & 0xFF);
            result[offset++] = (byte)((count >> 8) & 0xFF);

            foreach (var networkId in networkIds)
            {
                result[offset++] = (byte)(networkId & 0xFF);
                result[offset++] = (byte)((networkId >> 8) & 0xFF);
            }

            return result;
        }
    }

    internal class HVR_VariableState_UpdatedVariables_ZeroesAndOnes
    {
        public readonly byte packetType = AvatarMessageProcessing.NewNet_WearerSubmitsUpdatedVariables_ZeroesAndOnes;
        public ushort numberOfZeroes;
        public List<ushort> networkIds;

        public byte[] Serialize()
        {
            var totalLength = 1 + 2 + 2; // packetType + numberOfZeroes + count
            totalLength += networkIds.Count * 2; // each networkId is ushort (2 bytes)

            var result = new byte[totalLength];
            result[0] = packetType;
            var offset = 1;

            result[offset++] = (byte)(numberOfZeroes & 0xFF);
            result[offset++] = (byte)((numberOfZeroes >> 8) & 0xFF);

            var count = (ushort)networkIds.Count;
            result[offset++] = (byte)(count & 0xFF);
            result[offset++] = (byte)((count >> 8) & 0xFF);

            foreach (var networkId in networkIds)
            {
                result[offset++] = (byte)(networkId & 0xFF);
                result[offset++] = (byte)((networkId >> 8) & 0xFF);
            }

            return result;
        }
    }

    internal class HVR_VariableState_UpdatedVariables_Mixed
    {
        public readonly byte packetType = AvatarMessageProcessing.NewNet_WearerSubmitsUpdatedVariables_Mixed;
        public ushort numberOfZeroes;
        public List<ushort> networkIds;
        public List<HVR_VariableState_UpdatedValue> other;

        public class HVR_VariableState_UpdatedValue
        {
            public ushort networkId;
            public object value;
        }

        public byte[] Serialize()
        {
            var totalLength = 1 + 2 + 2; // packetType + numberOfZeroes + count
            totalLength += networkIds.Count * 2; // each networkId is ushort (2 bytes)
            totalLength += 2; // count of 'other'
            totalLength += other.Count * (2 + 4); // each has networkId (2 bytes) + float value (4 bytes)

            var result = new byte[totalLength];
            result[0] = packetType;
            var offset = 1;

            result[offset++] = (byte)(numberOfZeroes & 0xFF);
            result[offset++] = (byte)((numberOfZeroes >> 8) & 0xFF);

            var count = (ushort)networkIds.Count;
            result[offset++] = (byte)(count & 0xFF);
            result[offset++] = (byte)((count >> 8) & 0xFF);

            foreach (var networkId in networkIds)
            {
                result[offset++] = (byte)(networkId & 0xFF);
                result[offset++] = (byte)((networkId >> 8) & 0xFF);
            }

            var otherCount = (ushort)other.Count;
            result[offset++] = (byte)(otherCount & 0xFF);
            result[offset++] = (byte)((otherCount >> 8) & 0xFF);

            foreach (var updatedValue in other)
            {
                result[offset++] = (byte)(updatedValue.networkId & 0xFF);
                result[offset++] = (byte)((updatedValue.networkId >> 8) & 0xFF);

                var floatValue = (float)updatedValue.value;
                var valueBytes = BitConverter.GetBytes(floatValue);
                Buffer.BlockCopy(valueBytes, 0, result, offset, 4);
                offset += 4;
            }

            return result;
        }
    }
}
