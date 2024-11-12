// UdpPeer.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
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

    private int maxFragmentSize;

    private Dictionary<ushort, List<Header>> receivedMessages = new Dictionary<ushort, List<Header>>();
    private Dictionary<ushort, DateTime> messageStartTimes = new Dictionary<ushort, DateTime>();

    public uint LocalSequenceNumber { get { return localSequenceNumber; } }
    public uint RemoteSequenceNumber { get { return remoteSequenceNumber; } }

    public UdpPeer(int receivePort, string remoteAddress, int remotePort, int maxFragmentSize)
    {
        // Inicializácia UDP klienta na počúvanie
        receivingClient = new UdpClient(receivePort);
        // Inicializácia UDP klienta na odosielanie správ (bez špecifického portu)
        sendingClient = new UdpClient();
        remoteEndPoint = new IPEndPoint(IPAddress.Parse(remoteAddress), remotePort);

        localSequenceNumber = 100; // Počiatočné sekvenčné číslo
        this.maxFragmentSize = maxFragmentSize;
    }

    // Vlákno na počúvanie prichádzajúcich správ
    public async Task StartReceivingAsync(Action<string> onMessageReceived)
    {
        while (true)
        {
            // Prijatá správa
            var result = await receivingClient.ReceiveAsync();
            byte[] receivedBytes = result.Buffer;

            try
            {
                Header header = Header.FromBytes(receivedBytes);
                Console.WriteLine($"Prijatý fragment: Flags={header.Flags}, Seq={header.SequenceNumber}, Ack={header.AcknowledgmentNumber}, FragOffset={header.FragmentOffset}/{header.TotalFragments}");

                // Spracovanie dátových fragmentov
                if (header.Flags == 0x05) // Data message
                {
                    // Overíme alebo vytvoríme záznam pre toto sekvenčné číslo
                    if (!receivedMessages.ContainsKey(header.SequenceNumber))
                    {
                        receivedMessages[header.SequenceNumber] = new List<Header>(new Header[header.TotalFragments]);
                        messageStartTimes[header.SequenceNumber] = DateTime.Now;
                    }

                    // Uložíme fragment
                    receivedMessages[header.SequenceNumber][header.FragmentOffset - 1] = header;

                    Console.WriteLine($"Fragment {header.FragmentOffset}/{header.TotalFragments} prijatý bez chýb.");

                    // Skontrolujeme, či sme prijali všetky fragmenty
                    if (receivedMessages[header.SequenceNumber].All(h => h != null))
                    {
                        // Zložíme správu
                        var messageData = string.Join("", receivedMessages[header.SequenceNumber]
                            .OrderBy(h => h.FragmentOffset)
                            .Select(h => h.Data));

                        Console.WriteLine("Všetky fragmenty úspešne prijaté.");

                        DateTime startTime = messageStartTimes[header.SequenceNumber];
                        TimeSpan duration = DateTime.Now - startTime;
                        messageStartTimes.Remove(header.SequenceNumber);

                        int totalMessageSize = messageData.Length; // Veľkosť prijatej správy/súboru

                        // Zavoláme handler pre prijatú správu
                        onMessageReceived?.Invoke(messageData);

                        Console.WriteLine($"Celková veľkosť: {totalMessageSize} bajtov");
                        Console.WriteLine($"Trvanie prenosu: {duration.TotalSeconds} sekúnd");
                    }
                }
                else
                {
                    // Spracovanie iných typov správ (napr. handshake)
                    ProcessControlMessage(header);
                }
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine($"Chyba pri spracovaní prijatých dát: {ex.Message}");
            }
        }
    }

    // Metóda na spracovanie handshake a iných kontrolných správ
    private async void ProcessControlMessage(Header header)
    {
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
            // Po prijatí ACK
            else if ((header.Flags & 0x04) != 0 && header.Data == null)
            {
                Console.WriteLine("Prijaté ACK, handshake úspešný");
                isConnected = true;
                handshakeComplete = true;
            }
        }
    }

    // Odosielanie správy s fragmentáciou
    public async Task SendMessageAsync(string message, string? filename = null)
    {
        byte[] messageBytes = Encoding.UTF8.GetBytes(message);
        int totalMessageSize = messageBytes.Length; // Veľkosť správy/súboru v bajtoch

        // Výpočet maximálnej veľkosti dát v fragmente
        int headerSize = 1 + 2 * 5; // 11 bajtov
        int maxDataPerFragment = maxFragmentSize - headerSize;
        int totalFragments = (int)Math.Ceiling((double)messageBytes.Length / maxDataPerFragment);

        if (totalFragments == 0) totalFragments = 1;

        // Zobrazenie informácií o odosielanej správe/súbore
        if (filename != null)
        {
            Console.WriteLine($"Odosielam súbor: {filename}");
        }

        Console.WriteLine($"Veľkosť správy/súboru: {totalMessageSize} bajtov");
        Console.WriteLine($"Počet odosielaných fragmentov: {totalFragments}");
        Console.WriteLine($"Veľkosť fragmentu: {maxFragmentSize} bajtov");

        // Ak má posledný fragment inú veľkosť
        int lastFragmentDataSize = messageBytes.Length % maxDataPerFragment;
        if (lastFragmentDataSize != 0)
        {
            int lastFragmentSize = lastFragmentDataSize + headerSize;
            Console.WriteLine($"Posledný fragment má veľkosť: {lastFragmentSize} bajtov");
        }

        for (int i = 0; i < totalFragments; i++)
        {
            int offset = i * maxDataPerFragment;
            int dataLength = Math.Min(maxDataPerFragment, messageBytes.Length - offset);
            byte[] fragmentData = new byte[dataLength];
            Array.Copy(messageBytes, offset, fragmentData, 0, dataLength);

            // Vytvorenie hlavičky fragmentu
            Header header = new Header
            {
                Flags = 0x05, // Data message flag
                SequenceNumber = (ushort)(localSequenceNumber),
                AcknowledgmentNumber = (ushort)remoteSequenceNumber,
                FragmentOffset = (ushort)(i + 1),
                TotalFragments = (ushort)totalFragments,
                Data = Encoding.UTF8.GetString(fragmentData)
            };

            // Sériovanie a odoslanie fragmentu
            byte[] fragmentBytes = header.ToBytes();
            await sendingClient.SendAsync(fragmentBytes, fragmentBytes.Length, remoteEndPoint);

            Console.WriteLine($"Odoslaný fragment {i + 1}/{totalFragments}, veľkosť dát: {dataLength} bajtov");
        }

        // Zobrazenie informácií o veľkosti fragmentov
        Console.WriteLine($"Veľkosť fragmentu: {maxFragmentSize} bajtov");
        if (messageBytes.Length % maxDataPerFragment != 0)
        {
            int lastFragmentSize = (messageBytes.Length % maxDataPerFragment) + headerSize;
            Console.WriteLine($"Posledný fragment má veľkosť: {lastFragmentSize} bajtov");
        }

        localSequenceNumber++;
    }

    // Odosielanie správy s hlavičkou (napr. pre handshake)
    public async Task SendMessageWithHeaderAsync(string? message, byte messageType, uint seqNumber, uint ackNumber)
    {
        // Vytvorenie hlavičky
        Header header = new Header
        {
            Flags = messageType,
            SequenceNumber = (ushort)seqNumber,
            AcknowledgmentNumber = (ushort)ackNumber,
            FragmentOffset = 0,
            TotalFragments = 0,
            Data = message
        };

        // Prevedenie hlavičky a správy do bajtov
        byte[] messageBytes = header.ToBytes();

        // Odoslanie správy
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
