// FragmentationHelper.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

public static class FragmentationHelper
{
    // Metóda na rozdelenie dát na fragmenty
    public static List<Header> FragmentData(byte[] data, int maxFragmentSize, uint sequenceNumber, uint acknowledgmentNumber)
    {
        int headerSize = 1 + 2 * 5; // 11 bajtov
        int maxDataPerFragment = maxFragmentSize - headerSize;
        int totalFragments = (int)Math.Ceiling((double)data.Length / maxDataPerFragment);

        List<Header> fragments = new List<Header>();

        for (int i = 0; i < totalFragments; i++)
        {
            int offset = i * maxDataPerFragment;
            int dataLength = Math.Min(maxDataPerFragment, data.Length - offset);
            byte[] fragmentData = new byte[dataLength];
            Array.Copy(data, offset, fragmentData, 0, dataLength);

            Header header = new Header
            {
                Flags = 0x05, // Data message flag
                SequenceNumber = (ushort)sequenceNumber,
                AcknowledgmentNumber = (ushort)acknowledgmentNumber,
                FragmentOffset = (ushort)(i + 1),
                TotalFragments = (ushort)totalFragments,
                Data = Encoding.UTF8.GetString(fragmentData)
            };

        }

        return fragments;
    }

    // Metóda na skladanie fragmentov do pôvodných dát
    public static string ReassembleData(List<Header> fragments)
    {
        return string.Join("", fragments.OrderBy(h => h.FragmentOffset).Select(h => h.Data));
    }
}