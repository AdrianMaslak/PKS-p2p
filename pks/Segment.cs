using System.Text;

public class Segment
{
    public byte Flags { get; set; } // Flagy pre SYN, ACK, atď.
    public ushort SourcePort { get; set; } // Port odosielateľa
    public ushort DestinationPort { get; set; } // Port prijímateľa
    public uint SequenceNumber { get; set; } // Sekvenčné číslo
    public uint AcknowledgmentNumber { get; set; } // ACK číslo
    public uint Checksum { get; set; } // Kontrolný súčet
    public string? Data { get; set; } // Dáta (používa sa len po handshaku)

    public byte[] ToBytes()
    {
        byte[] dataBytes = Data != null ? Encoding.UTF8.GetBytes(Data) : new byte[0];
        byte[] segmentBytes = new byte[15 + dataBytes.Length]; // 15 bajtov hlavička + dáta

        segmentBytes[0] = Flags;

        // Source Port
        BitConverter.GetBytes(SourcePort).CopyTo(segmentBytes, 1);

        // Destination Port
        BitConverter.GetBytes(DestinationPort).CopyTo(segmentBytes, 3);

        // Sequence Number
        BitConverter.GetBytes(SequenceNumber).CopyTo(segmentBytes, 5);

        // Acknowledgment Number
        BitConverter.GetBytes(AcknowledgmentNumber).CopyTo(segmentBytes, 9);

        // Checksum (na začiatku 0)
        Checksum = 0;
        BitConverter.GetBytes(Checksum).CopyTo(segmentBytes, 13);

        // Ak máme dáta, pridáme ich do správy
        if (dataBytes.Length > 0)
        {
            dataBytes.CopyTo(segmentBytes, 15);
        }

        // Vypočítame a nastavíme checksum
        Checksum = CalculateChecksum(segmentBytes);
        BitConverter.GetBytes(Checksum).CopyTo(segmentBytes, 13);

        return segmentBytes;
    }

    public static Segment FromBytes(byte[] segmentBytes)
    {
        Segment segment = new Segment
        {
            Flags = segmentBytes[0],
            SourcePort = BitConverter.ToUInt16(segmentBytes, 1),
            DestinationPort = BitConverter.ToUInt16(segmentBytes, 3),
            SequenceNumber = BitConverter.ToUInt32(segmentBytes, 5),
            AcknowledgmentNumber = BitConverter.ToUInt32(segmentBytes, 9),
            Checksum = BitConverter.ToUInt32(segmentBytes, 13)
        };

        // Overíme kontrolný súčet
        if (segment.Checksum != CalculateChecksum(segmentBytes))
        {
            throw new InvalidOperationException("Checksum verification failed.");
        }

        // Ak správa obsahuje dáta (po handshaku), extrahujeme ich
        if (segmentBytes.Length > 15)
        {
            segment.Data = Encoding.UTF8.GetString(segmentBytes, 15, segmentBytes.Length - 15);
        }

        return segment;
    }

    private static uint CalculateChecksum(byte[] segmentBytes)
    {
        uint checksum = 0;
        foreach (byte b in segmentBytes)
        {
            checksum += b;
        }
        return checksum;
    }
}
