using System.Net.Sockets;
using System.Net;

public class HandshakeManager
{
    private UdpClient sendingClient;
    private IPEndPoint remoteEndPoint;
    private bool handshakeComplete = false;
    private uint localSequenceNumber;
    private uint remoteSequenceNumber;

    public HandshakeManager(UdpClient sendingClient, IPEndPoint remoteEndPoint)
    {
        this.sendingClient = sendingClient;
        this.remoteEndPoint = remoteEndPoint;

        // Generovanie náhodného počiatočného sekvenčného čísla pre lokálny uzol
        Random random = new Random();
        localSequenceNumber = (uint)random.Next(1, 1000); // Náhodné číslo pre začiatok
    }

    // Metóda na začatie handshaku
    public async Task StartHandshakeAsync()
    {
        // Krok 1: Odosielanie SYN
        await SendSYN();

        // Krok 2: Čakanie na prijatie SYN-ACK a následné odoslanie ACK
        await ListenForSynAck();
    }

    // Krok 1: Odoslanie SYN segmentu
    private async Task SendSYN()
    {
        Segment synSegment = new Segment
        {
            Flags = 0x01, // SYN flag
            SourcePort = (ushort)((IPEndPoint)sendingClient.Client.LocalEndPoint).Port,
            DestinationPort = (ushort)remoteEndPoint.Port,
            SequenceNumber = localSequenceNumber, // Naše sekvenčné číslo
            AcknowledgmentNumber = 0, // Nie je relevantné pre SYN
            Data = null
        };

        byte[] synBytes = synSegment.ToBytes();
        await sendingClient.SendAsync(synBytes, synBytes.Length, remoteEndPoint);
        Console.WriteLine($"Odoslaný SYN s sekvenčným číslom {localSequenceNumber}");
    }

    // Krok 2: Počúvanie na SYN-ACK a odpovedanie ACK
    private async Task ListenForSynAck()
    {
        while (!handshakeComplete)
        {
            UdpReceiveResult result = await sendingClient.ReceiveAsync();
            Segment receivedSegment = Segment.FromBytes(result.Buffer);

            if ((receivedSegment.Flags & 0x03) == 0x03) // SYN-ACK
            {
                Console.WriteLine("Prijatý SYN-ACK");

                // Uložíme si sekvenčné číslo od remote uzla
                remoteSequenceNumber = receivedSegment.SequenceNumber;

                // Odošleme ACK s naším sekvenčným číslom a ACK číslo ako sekvenčné číslo remote uzla + 1
                await SendACK(receivedSegment);
            }
        }
    }

    // Krok 3: Odoslanie ACK segmentu
    private async Task SendACK(Segment receivedSynAckSegment)
    {
        Segment ackSegment = new Segment
        {
            Flags = 0x02, // ACK flag
            SourcePort = receivedSynAckSegment.DestinationPort,
            DestinationPort = receivedSynAckSegment.SourcePort,
            SequenceNumber = localSequenceNumber + 1, // Zväčšujeme naše sekvenčné číslo
            AcknowledgmentNumber = receivedSynAckSegment.SequenceNumber + 1, // Odpovedáme na sekvenčné číslo remote uzla
            Data = null
        };

        byte[] ackBytes = ackSegment.ToBytes();
        await sendingClient.SendAsync(ackBytes, ackBytes.Length, remoteEndPoint);
        Console.WriteLine($"Odoslaný ACK s sekvenčným číslom {localSequenceNumber + 1} a ACK číslo {receivedSynAckSegment.SequenceNumber + 1}");
        handshakeComplete = true;
    }

    public bool IsHandshakeComplete()
    {
        return handshakeComplete;
    }
}
