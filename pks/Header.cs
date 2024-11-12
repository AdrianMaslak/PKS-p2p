// Header.cs
using System;
using System.Text;

public class Header
{
    public byte Flags { get; set; } // Typ správy (FLAGS)
    public ushort SequenceNumber { get; set; } // Sekvenčné číslo
    public ushort AcknowledgmentNumber { get; set; } // Potvrdenie prijatia
    public ushort FragmentOffset { get; set; } // Poradie fragmentu
    public ushort TotalFragments { get; set; } // Celkový počet fragmentov
    public ushort Checksum { get; set; } // Kontrolný súčet
    public string? Data { get; set; } // Dáta správy

    // Konverzia do bajtov (big-endian) s CRC16-ANSI
    public byte[] ToBytes()
    {
        byte[] dataBytes = Data != null ? Encoding.UTF8.GetBytes(Data) : new byte[0];

        // Vypočítame dĺžku hlavičky
        int headerLength = 1 + 2 * 5; // 1 byte pre Flags + 5 polí po 2 bajty = 11 bajtov
        byte[] segmentBytes = new byte[headerLength + dataBytes.Length];

        // Flags (1 byte)
        segmentBytes[0] = Flags;

        int index = 1;
        Array.Copy(ToBigEndianBytes(SequenceNumber), 0, segmentBytes, index, 2); index += 2;
        Array.Copy(ToBigEndianBytes(AcknowledgmentNumber), 0, segmentBytes, index, 2); index += 2;
        Array.Copy(ToBigEndianBytes(FragmentOffset), 0, segmentBytes, index, 2); index += 2;
        Array.Copy(ToBigEndianBytes(TotalFragments), 0, segmentBytes, index, 2); index += 2;

        // Placeholder pre Checksum (nastavíme na 0 pred výpočtom)
        Array.Copy(ToBigEndianBytes(0), 0, segmentBytes, index, 2); index += 2;

        // Skopírujeme dáta
        if (dataBytes.Length > 0)
        {
            dataBytes.CopyTo(segmentBytes, headerLength);
        }

        // Vypočítame kontrolný súčet (CRC16-ANSI)
        ushort crc = Crc16.ComputeChecksum(segmentBytes);
        Checksum = crc;

        // Vložíme kontrolný súčet do segmentu (posledné 2 bajty hlavičky)
        Array.Copy(ToBigEndianBytes(Checksum), 0, segmentBytes, 9, 2); // Index 9 je pozícia Checksum

        return segmentBytes;
    }

    // Konverzia z bajtov do hlavičky a overenie CRC16-ANSI
    public static Header FromBytes(byte[] segmentBytes)
    {
        Header header = new Header();
        header.Flags = segmentBytes[0];

        int index = 1;
        header.SequenceNumber = FromBigEndianBytes(segmentBytes, index); index += 2;
        header.AcknowledgmentNumber = FromBigEndianBytes(segmentBytes, index); index += 2;
        header.FragmentOffset = FromBigEndianBytes(segmentBytes, index); index += 2;
        header.TotalFragments = FromBigEndianBytes(segmentBytes, index); index += 2;
        header.Checksum = FromBigEndianBytes(segmentBytes, index); index += 2;

        // Overenie kontrolného súčtu
        ushort receivedChecksum = header.Checksum;
        // Nastavíme kontrolný súčet na 0 pred výpočtom
        byte[] dataForChecksum = new byte[segmentBytes.Length];
        Array.Copy(segmentBytes, dataForChecksum, segmentBytes.Length);
        Array.Copy(ToBigEndianBytes(0), 0, dataForChecksum, 9, 2); // Zero out the checksum field

        ushort calculatedChecksum = Crc16.ComputeChecksum(dataForChecksum);

        if (receivedChecksum != calculatedChecksum)
        {
            throw new InvalidOperationException("Kontrolný súčet nesedí, dáta môžu byť poškodené.");
        }

        // Extrahujeme dáta
        int headerLength = 1 + 2 * 5; // 11 bajtov
        if (segmentBytes.Length > headerLength)
        {
            header.Data = Encoding.UTF8.GetString(segmentBytes, headerLength, segmentBytes.Length - headerLength);
        }

        return header;
    }

    // Pomocné metódy na konverziu endianity
    private static byte[] ToBigEndianBytes(ushort value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }
        return bytes;
    }

    private static ushort FromBigEndianBytes(byte[] bytes, int startIndex)
    {
        byte[] buffer = new byte[2];
        Array.Copy(bytes, startIndex, buffer, 0, 2);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(buffer);
        }
        return BitConverter.ToUInt16(buffer, 0);
    }
}
