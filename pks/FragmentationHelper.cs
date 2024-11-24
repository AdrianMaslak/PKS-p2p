using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

public static class FragmentationHelper
{
    // Method to fragment data into smaller chunks
    public static List<Header> FragmentData(byte[] data, int maxFragmentSize, uint sequenceNumber, uint acknowledgmentNumber)
    {
        List<Header> fragments = new List<Header>();
        int totalDataLength = data.Length;

        int offset = 0;
        int totalFragments = 0;

        // Fragment until all data is divided
        while (offset < totalDataLength)
        {
            // Create a temporary header to calculate its size dynamically
            Header tempHeader = new Header
            {
                Flags = 0x05,
                SequenceNumber = (ushort)sequenceNumber,
                AcknowledgmentNumber = (ushort)acknowledgmentNumber,
                FragmentOffset = (ushort)(totalFragments + 1),
                TotalFragments = 0, // Placeholder
                Data = ""
            };

            // Calculate the dynamic header size
            int headerSize = Encoding.UTF8.GetByteCount(tempHeader.ToString()) - tempHeader.Data.Length;
            int maxDataPerFragment = maxFragmentSize - headerSize;

            if (maxDataPerFragment <= 0)
            {
                throw new ArgumentException("Max fragment size is too small to accommodate the header.");
            }

            // Determine the size of data to include in this fragment
            int dataLength = Math.Min(maxDataPerFragment, totalDataLength - offset);
            byte[] fragmentData = new byte[dataLength];
            Array.Copy(data, offset, fragmentData, 0, dataLength);

            // Create the actual fragment header
            Header header = new Header
            {
                Flags = 0x05, // Data message flag
                SequenceNumber = (ushort)sequenceNumber,
                AcknowledgmentNumber = (ushort)acknowledgmentNumber,
                FragmentOffset = (ushort)(totalFragments + 1),
                TotalFragments = 0, // Will set this later
                Data = Encoding.UTF8.GetString(fragmentData)
            };

            fragments.Add(header);

            offset += dataLength;
            totalFragments++;
        }

        // Update TotalFragments in each fragment's header
        foreach (var fragment in fragments)
        {
            fragment.TotalFragments = (ushort)totalFragments;
        }

        return fragments;
    }

    // Method to reassemble fragments into the original data
    public static string ReassembleData(List<Header> fragments)
    {
        return string.Join("", fragments.OrderBy(h => h.FragmentOffset).Select(h => h.Data));
    }
}
