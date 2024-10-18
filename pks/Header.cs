using System.Text;

public class Header
{
    public byte Type { get; set; }
    public ushort SourcePort { get; set; }
    public ushort DestinationPort { get; set; }
    public ushort Length { get; set; }
    public uint Checksum { get; set; }
    public string Data { get; set; }

    public byte[] ToBytes()
    {
        byte[] dataBytes = Encoding.UTF8.GetBytes(Data);
        Length = (ushort)dataBytes.Length;

        byte[] headerBytes = new byte[9 + dataBytes.Length]; // 9 bajtov hlavička + dáta
        headerBytes[0] = Type;

        // Source Port
        BitConverter.GetBytes(SourcePort).CopyTo(headerBytes, 1);

        // Destination Port
        BitConverter.GetBytes(DestinationPort).CopyTo(headerBytes, 3);

        // Dĺžka
        BitConverter.GetBytes(Length).CopyTo(headerBytes, 5);

        // Checksum (aktuálne na 0)
        Checksum = 0;
        BitConverter.GetBytes(Checksum).CopyTo(headerBytes, 7);

        // Dáta
        dataBytes.CopyTo(headerBytes, 9);

        return headerBytes;
    }

    public static Header FromBytes(byte[] headerBytes)
    {
        Header header = new Header();
        header.Type = headerBytes[0];
        header.SourcePort = BitConverter.ToUInt16(headerBytes, 1);
        header.DestinationPort = BitConverter.ToUInt16(headerBytes, 3);
        header.Length = BitConverter.ToUInt16(headerBytes, 5);
        header.Checksum = BitConverter.ToUInt32(headerBytes, 7);

        // Dočasne ignorujeme overenie checksumu

        // Extrahovanie dát
        header.Data = Encoding.UTF8.GetString(headerBytes, 9, header.Length);

        return header;
    }
}
