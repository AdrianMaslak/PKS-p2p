using System.Text;

public class Header
{
    public byte Flags { get; set; } // Typ správy (FLAGS)
    public ushort SequenceNumber { get; set; } // Sekvenčné číslo
    public ushort AcknowledgmentNumber { get; set; } // Potvrdenie prijatej správy
    public ushort FragmentID { get; set; } // Identifikátor fragmentu
    public string? Data { get; set; } // Dáta správy


    // Convert to big-endian bytes
    public byte[] ToBytes()
    {
        byte[] dataBytes = Data != null ? Encoding.UTF8.GetBytes(Data) : new byte[0];

        // Calculate the total length of the header (1 byte for Flags + 2 bytes each for SequenceNumber, AcknowledgmentNumber, FragmentID)
        int headerLength = 1 + 2 + 2 + 2;
        byte[] segmentBytes = new byte[headerLength + dataBytes.Length];

        // Flags (1 byte)
        segmentBytes[0] = Flags;

        // Convert fields to big-endian and copy to segmentBytes
        Array.Copy(ToBigEndianBytes(SequenceNumber), 0, segmentBytes, 1, 2);
        Array.Copy(ToBigEndianBytes(AcknowledgmentNumber), 0, segmentBytes, 3, 2);
        Array.Copy(ToBigEndianBytes(FragmentID), 0, segmentBytes, 5, 2);

        // Append data if present
        if (dataBytes.Length > 0)
        {
            dataBytes.CopyTo(segmentBytes, headerLength);
        }

        return segmentBytes;
    }


    // Convert from big-endian bytes to header
    public static Header FromBytes(byte[] segmentBytes)
    {
        Header header = new Header
        {
            Flags = segmentBytes[0],
            SequenceNumber = FromBigEndianBytes(segmentBytes, 1),
            AcknowledgmentNumber = FromBigEndianBytes(segmentBytes, 3),
            FragmentID = FromBigEndianBytes(segmentBytes, 5),
        };

        // Extract data if any is present after the header
        int headerLength = 1 + 2 + 2 + 2;
        if (segmentBytes.Length > headerLength)
        {
            header.Data = Encoding.UTF8.GetString(segmentBytes, headerLength, segmentBytes.Length - headerLength);
        }

        return header;
    }


    // Helper to convert ushort to big-endian byte array
    private static byte[] ToBigEndianBytes(ushort value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes); // Reverse for big-endian
        }
        return bytes;
    }

    // Helper to convert big-endian byte array to ushort
    private static ushort FromBigEndianBytes(byte[] bytes, int startIndex)
    {
        byte[] buffer = new byte[2];
        Array.Copy(bytes, startIndex, buffer, 0, 2);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(buffer); // Reverse for little-endian systems
        }
        return BitConverter.ToUInt16(buffer, 0);
    }
}
