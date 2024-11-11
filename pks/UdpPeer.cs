using System;
using System.Net;
using System.Net.Sockets;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Threading.Tasks;

public class UdpPeer
{
    private UdpClient receivingClient; // Na počúvanie správ
    private UdpClient sendingClient;   // Na odosielanie správ
    private IPEndPoint remoteEndPoint; // Kde posielame správy (adresát)
    private bool isConnected = false;  // Stav pripojenia
    private bool handshakeComplete = false; // Stav handshaku
    private uint localSequenceNumber; 
    private uint remoteSequenceNumber;

    public uint LocalSequenceNumber { get { return localSequenceNumber; } }
    public uint RemoteSequenceNumber { get { return remoteSequenceNumber; } }

    public UdpPeer(int receivePort, string remoteAddress, int remotePort)
    {
        // Inicializácia UDP klienta na počúvanie
        receivingClient = new UdpClient(receivePort);
        // Inicializácia UDP klienta na odosielanie správ (bez špecifického portu)
        sendingClient = new UdpClient();
        remoteEndPoint = new IPEndPoint(IPAddress.Parse(remoteAddress), remotePort);

        localSequenceNumber = (uint)100;
    }

    // Vlákno na počúvanie prichádzajúcich správ
    public async Task StartReceivingAsync(Action<string> onMessageReceived)
    {
        while (true)
        {
            // Prijatá správa
            var result = await receivingClient.ReceiveAsync();
            string receivedMessage = Encoding.UTF8.GetString(result.Buffer);

            byte[] receivedBytes = result.Buffer;


            Header header = Header.FromBytes(receivedBytes);
            Console.WriteLine($"Prijatá správa: Flags = {header.Flags}, Seq = {header.SequenceNumber}, Ack = {header.AcknowledgmentNumber}");

            if (!isConnected)
            {
                // Spracovanie handshake
                if ((header.Flags & 0x01) != 0 && header.Data == null)
                {
                    Console.WriteLine("Prijaté SYN, odosielam SYN_ACK");
                    remoteSequenceNumber = header.SequenceNumber;
                    await SendMessageWithHeaderAsync(null, 0x02, localSequenceNumber, remoteSequenceNumber + 1);
                }
                // Po prijatí SYN-ACK
                else if ((header.Flags & 0x02) != 0 && header.Data == null)
                {
                    Console.WriteLine("Prijaté SYN_ACK, odosielam ACK");
                    remoteSequenceNumber = header.SequenceNumber;
                    await SendMessageWithHeaderAsync(null, 0x04, localSequenceNumber + 1, remoteSequenceNumber + 1);
                    handshakeComplete = true;
                    isConnected = true;  // Ukončenie handshake
                }
                // Po prijatí ACK (musíme overiť, či je ACK správne a ukončiť handshake)
                else if ((header.Flags & 0x04) != 0 && header.Data == null)
                {
                    Console.WriteLine("Prijaté ACK, handshake úspešný");
                    isConnected = true;
                    handshakeComplete = true;
                }
            }

            // Spracovanie bežných správ po úspešnom handshaku
            if (isConnected && header.Data != null)
            {
                onMessageReceived?.Invoke(header.Data);
            }
        }
    }

    // Odosielanie na vzdialený uzol
    public async Task SendMessageAsync(string message)
    {
        byte[] messageBytes = Encoding.UTF8.GetBytes(message);
        await sendingClient.SendAsync(messageBytes, messageBytes.Length, remoteEndPoint);
    }
    public async Task SendMessageWithHeaderAsync(string? message, byte messageType, uint seqNumber, uint ackNumber)
    {
        // Create header
        Header header = new Header
        {
            Flags = messageType,
            SequenceNumber = (ushort)seqNumber,
            AcknowledgmentNumber = (ushort)ackNumber,
            Data = message
        };

        // Serialize header and message
        byte[] messageBytes = header.ToBytes();

        // Send the message
        await sendingClient.SendAsync(messageBytes, messageBytes.Length, remoteEndPoint);
    }



    // Handshake, posielame SYN kým sa neuskutoční handshake
    public async Task SendHandshakeAsync()
    {
        // Odosielame SYN iba dovtedy, kým handshake nie je dokončený
        while (!handshakeComplete)
        {
            Console.WriteLine("Odosielam SYN na nadviazanie spojenia...");

            await SendMessageWithHeaderAsync(null, 0x01, localSequenceNumber, 0);
            // Čakáme 2 sekundy pred ďalším pokusom
            await Task.Delay(2000);
            Console.WriteLine($"Sequence Number: {localSequenceNumber}, Acknowledgment Number: {remoteSequenceNumber}");

            if (handshakeComplete)
            {
                Console.WriteLine("Handshake dokončený, ukončenie odosielania SYN.");
                break;
            }
        }
    }

    public bool IsConnected()
    {
        return isConnected;
    }
}
