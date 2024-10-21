using System.Text;

public class Header
{
    public byte Flags { get; set; } // Typ správy (FLAGS)
    public ushort SourcePort { get; set; } // Zdrojový port
    public ushort DestinationPort { get; set; } // Cieľový port
    public ushort SequenceNumber { get; set; } // Sekvenčné číslo
    public ushort AcknowledgmentNumber { get; set; } // Potvrdenie prijatej správy
    public ushort FragmentID { get; set; } // Identifikátor fragmentu
    public ushort Length { get; set; } // Dĺžka dát
    public string? Data { get; set; } // Dáta správy

    // Convert to big-endian bytes
    public byte[] ToBytes()
    {
        byte[] dataBytes = Data != null ? Encoding.UTF8.GetBytes(Data) : new byte[0];
        Length = (ushort)dataBytes.Length;

        // Allocate byte array: 13 bytes for header + data length
        byte[] segmentBytes = new byte[13 + dataBytes.Length];

        // Flags (1 byte)
        segmentBytes[0] = Flags;

        // Convert fields to big-endian and copy to segmentBytes
        Array.Copy(ToBigEndianBytes(SourcePort), 0, segmentBytes, 1, 2);
        Array.Copy(ToBigEndianBytes(DestinationPort), 0, segmentBytes, 3, 2);
        Array.Copy(ToBigEndianBytes(SequenceNumber), 0, segmentBytes, 5, 2);
        Array.Copy(ToBigEndianBytes(AcknowledgmentNumber), 0, segmentBytes, 7, 2);
        Array.Copy(ToBigEndianBytes(FragmentID), 0, segmentBytes, 9, 2);
        Array.Copy(ToBigEndianBytes(Length), 0, segmentBytes, 11, 2);

        // If there are data bytes, append them after the header
        if (dataBytes.Length > 0)
        {
            dataBytes.CopyTo(segmentBytes, 13);
        }

        return segmentBytes;
    }

    // Convert from big-endian bytes to header
    public static Header FromBytes(byte[] segmentBytes)
    {
        Header header = new Header
        {
            Flags = segmentBytes[0],
            SourcePort = FromBigEndianBytes(segmentBytes, 1),
            DestinationPort = FromBigEndianBytes(segmentBytes, 3),
            SequenceNumber = FromBigEndianBytes(segmentBytes, 5),
            AcknowledgmentNumber = FromBigEndianBytes(segmentBytes, 7),
            FragmentID = FromBigEndianBytes(segmentBytes, 9),
            Length = FromBigEndianBytes(segmentBytes, 11)
        };

        // Extract data if any is present after the 13-byte header
        if (segmentBytes.Length > 13)
        {
            header.Data = Encoding.UTF8.GetString(segmentBytes, 13, segmentBytes.Length - 13);
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
