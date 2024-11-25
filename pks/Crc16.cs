using System;

public static class Crc16
{
    private static readonly ushort[] Table = new ushort[256];
    private const ushort Polynomial = 0x8005;
    private const ushort InitialValue = 0xFFFF;

    private static readonly Random RandomGenerator = new Random(); // Náhodný generátor

    // Statický konštruktor na naplnenie CRC tabuľky
    static Crc16()
    {
        for (int i = 0; i < 256; i++)
        {
            ushort crc = 0;
            ushort c = (ushort)(i << 8);
            for (int j = 0; j < 8; j++)
            {
                if (((crc ^ c) & 0x8000) != 0)
                {
                    crc = (ushort)((crc << 1) ^ Polynomial);
                }
                else
                {
                    crc <<= 1;
                }
                c <<= 1;
            }
            Table[i] = crc;
        }
    }

    // Výpočet CRC16-ANSI pre dané dáta so šancou na chybu
    public static ushort ComputeChecksum(byte[] bytes)
    {
        ushort crc = InitialValue;
        foreach (byte b in bytes)
        {
            byte tableIndex = (byte)((crc >> 8) ^ b);
            crc = (ushort)((crc << 8) ^ Table[tableIndex]);
        }

        // Simulácia chyby so šancou 0.01%
        if (RandomGenerator.NextDouble() < 0.0001) // 0.01% šanca
        {
            Console.WriteLine("Simulácia chyby v CRC výpočte!");
            return (ushort)(crc ^ 0xFFFF); // Náhodná zmena CRC výsledku
        }

        return crc;
    }
}
