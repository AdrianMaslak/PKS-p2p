using System;
using System.Linq;
using System.Text;

public class Header
{
    public byte Type { get; set; } // Message type (e.g., 0x01 for SYN, 0x06 for standard message)
    public ushort SourcePort { get; set; } // Source port
    public ushort DestinationPort { get; set; } // Destination port
    public ushort Length { get; set; } // Length of the data part of the message
    public string Data { get; set; } // Message content

    // Convert header data to byte array for sending
    public byte[] ToBytes()
    {
        byte[] dataBytes = Encoding.UTF8.GetBytes(Data);
        Length = (ushort)dataBytes.Length; // Set the length based on the actual data length
        byte[] headerBytes = new byte[7 + dataBytes.Length]; // 7 bytes for header fields + length of data
        headerBytes[0] = Type;
        Array.Copy(BitConverter.GetBytes(SourcePort), 0, headerBytes, 1, 2);
        Array.Copy(BitConverter.GetBytes(DestinationPort), 0, headerBytes, 3, 2);
        Array.Copy(BitConverter.GetBytes(Length), 0, headerBytes, 5, 2);
        Array.Copy(dataBytes, 0, headerBytes, 7, dataBytes.Length);
        return headerBytes;
    }

    // Convert received byte array to Header object
    public static Header FromBytes(byte[] bytes)
    {
        var header = new Header
        {
            Type = bytes[0],
            SourcePort = BitConverter.ToUInt16(bytes, 1),
            DestinationPort = BitConverter.ToUInt16(bytes, 3),
            Length = BitConverter.ToUInt16(bytes, 5)
        };

        int dataLength = bytes.Length - 7; // Subtract the header length
        header.Data = Encoding.UTF8.GetString(bytes, 7, dataLength);
        return header;
    }
}
