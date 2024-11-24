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

    private DateTime lastHeartbeatSent;
    private DateTime lastHeartbeatReceived;
    private int missedHeartbeats = 0;

    private const int HEARTBEAT_INTERVAL_MS = 5000; // 5 sekúnd
    private const int MAX_MISSED_HEARTBEATS = 3;

    private int maxFragmentSize;

    private Dictionary<ushort, List<Header>> receivedMessages = new Dictionary<ushort, List<Header>>();
    private Dictionary<ushort, DateTime> messageStartTimes = new Dictionary<ushort, DateTime>();

    public uint LocalSequenceNumber { get { return localSequenceNumber; } }
    public uint RemoteSequenceNumber { get { return remoteSequenceNumber; } }

    public event Action ConnectionLost;

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
               if (header.Flags == 0x07) // Heartbeat
                {
                    await SendAckAsync(); // Send ACK in response
                    lastHeartbeatReceived = DateTime.Now;
                    missedHeartbeats = 0; // Reset missed heartbeats
                    continue;
                }
                if (header.Flags == 0x08) // ACK
                {
                    // Removed unnecessary console output
                    // Console.WriteLine("Prijaté ACK");
                    lastHeartbeatReceived = DateTime.Now;
                    missedHeartbeats = 0; // Reset missed heartbeats
                    continue;
                }
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

                    // Removed unnecessary console output
                    // Console.WriteLine($"Fragment {header.FragmentOffset}/{header.TotalFragments} prijatý bez chýb.");

                    // Skontrolujeme, či sme prijali všetky fragmenty
                    if (receivedMessages[header.SequenceNumber].All(h => h != null))
                    {
                        // Zložíme správu
                        var messageData = string.Join("", receivedMessages[header.SequenceNumber]
                            .OrderBy(h => h.FragmentOffset)
                            .Select(h => h.Data));

                        // Removed unnecessary console output
                        // Console.WriteLine("Všetky fragmenty úspešne prijaté.");

                        DateTime startTime = messageStartTimes[header.SequenceNumber];
                        TimeSpan duration = DateTime.Now - startTime;
                        messageStartTimes.Remove(header.SequenceNumber);

                        int totalMessageSize = messageData.Length; // Veľkosť prijatej správy/súboru

                        // Zavoláme handler pre prijatú správu
                        onMessageReceived?.Invoke(messageData);

                        // Removed unnecessary console output
                        // Console.WriteLine($"Celková veľkosť: {totalMessageSize} bajtov");
                        // Console.WriteLine($"Trvanie prenosu: {duration.TotalSeconds} sekúnd");
                    }
                }
                else
                {
                    // Spracovanie iných typov správ (napr. handshake)
                    await ProcessControlMessage(header);
                }
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine($"Chyba pri spracovaní prijatých dát: {ex.Message}");
            }
        }
    }

    // Metóda na spracovanie handshake a iných kontrolných správ
    private async Task ProcessControlMessage(Header header)
    {
        if (!handshakeComplete)
        {
            // Spracovanie handshake
            if ((header.Flags & 0x01) != 0 && header.Data == null)
            {
                Console.WriteLine("Prijaté SYN, odosielam SYN_ACK");
                remoteSequenceNumber = header.SequenceNumber;
                await SendMessageWithHeaderAsync(null, 0x02, localSequenceNumber, remoteSequenceNumber + 1);
            }
            else if ((header.Flags & 0x02) != 0 && header.Data == null)
            {
                Console.WriteLine("Prijaté SYN_ACK, odosielam ACK");
                remoteSequenceNumber = header.SequenceNumber;
                await SendMessageWithHeaderAsync(null, 0x04, localSequenceNumber + 1, remoteSequenceNumber + 1);
                handshakeComplete = true;
                isConnected = true;

                _ = Task.Run(() => StartHeartbeat());
            }
            else if ((header.Flags & 0x04) != 0 && header.Data == null)
            {
                Console.WriteLine("Prijaté ACK, handshake úspešný");
                handshakeComplete = true;
                isConnected = true;

                _ = Task.Run(() => StartHeartbeat());
            }
        }
        else
        {
            // Ak handshake už prebehol, ale dostali sme SYN, reštartujeme spojenie
            if ((header.Flags & 0x01) != 0 && header.Data == null)
            {
                Console.WriteLine("Prijaté SYN počas aktívneho spojenia. Reštartujem handshake.");
                handshakeComplete = false;
                isConnected = false;

                remoteSequenceNumber = header.SequenceNumber;
                await SendMessageWithHeaderAsync(null, 0x02, localSequenceNumber, remoteSequenceNumber + 1);
            }
        }
    }


    // Odosielanie správy s fragmentáciou
    public async Task SendMessageAsync(string message, string? filename = null)
    {

        if (!isConnected)
        {
            Console.WriteLine("Spojenie nie je nadviazané. Pokúšam sa o handshake...");
            await SendHandshakeAsync();
            // Wait a bit to see if handshake succeeds
            await Task.Delay(2000);
            if (!isConnected)
            {
                Console.WriteLine("Handshake zlyhal. Správa nebola odoslaná.");
                return;
            }
        }

        byte[] messageBytes = Encoding.UTF8.GetBytes(message);
        int totalMessageSize = messageBytes.Length; // Veľkosť správy/súboru v bajtoch

        // Výpočet maximálnej veľkosti dát v fragmente
        int headerSize = 1 + 2 * 5; // 11 bajtov
        int maxDataPerFragment = maxFragmentSize;
        int totalFragments = (int)Math.Ceiling((double)messageBytes.Length / maxDataPerFragment);

        // Zobrazenie informácií o odosielanej správe/súbore
        if (filename != null)
        {
            Console.WriteLine($"Odosielam súbor: {filename}");
        }

        Console.WriteLine($"Veľkosť správy/súboru: {totalMessageSize} bajtov");
        Console.WriteLine($"Počet odosielaných fragmentov: {totalFragments}");
        Console.WriteLine($"Veľkosť fragmentu: {maxFragmentSize} bajtov");

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

            // Removed unnecessary console output
            // Console.WriteLine($"Odoslaný fragment {i + 1}/{totalFragments}, veľkosť dát: {dataLength} bajtov");
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

    private async Task SendAckAsync()
    {
        try
        {
            Header header = new Header
            {
                Flags = 0x08, // ACK flag
                SequenceNumber = 0,
                AcknowledgmentNumber = 0,
                FragmentOffset = 0,
                TotalFragments = 0,
                Data = null
            };

            byte[] ackMessage = header.ToBytes();
            await sendingClient.SendAsync(ackMessage, ackMessage.Length, remoteEndPoint);
            // Removed unnecessary console output
            // Console.WriteLine("Odoslané ACK na heartbeat.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Chyba pri odosielaní ACK správy: {ex.Message}");
        }
    }

    private async Task StartHeartbeat()
    {
        lastHeartbeatReceived = DateTime.Now;

        while (true)
        {
            if (!isConnected) break; // Ukonči heartbeat, ak nie je pripojenie

            try
            {
                // Odoslanie heartbeat správy
                Header heartbeatHeader = new Header
                {
                    Flags = 0x07, // Heartbeat flag
                    SequenceNumber = 0,
                    AcknowledgmentNumber = 0,
                    FragmentOffset = 0,
                    TotalFragments = 0,
                    Data = null
                };

                byte[] heartbeatMessage = heartbeatHeader.ToBytes();
                await sendingClient.SendAsync(heartbeatMessage, heartbeatMessage.Length, remoteEndPoint);

                lastHeartbeatSent = DateTime.Now;
                await Task.Delay(HEARTBEAT_INTERVAL_MS);

                // Kontrola prijatia odpovede (ACK)
                if ((DateTime.Now - lastHeartbeatReceived).TotalMilliseconds > HEARTBEAT_INTERVAL_MS * MAX_MISSED_HEARTBEATS)
                {
                    missedHeartbeats++;
                    Console.WriteLine($"Chýba odpoveď na heartbeat. Počet zmeškaných: {missedHeartbeats}");

                    if (missedHeartbeats >= MAX_MISSED_HEARTBEATS)
                    {
                        Console.WriteLine("Spojenie zlyhalo. Pokúšam sa znovu nadviazať spojenie...");
                        isConnected = false;
                        handshakeComplete = false;

                        // Resetovanie stavu a spustenie handshake
                        await Task.Run(() => SendHandshakeAsync());
                        break;
                    }
                }
                else
                {
                    missedHeartbeats = 0; // Reset pri úspešnom prijatí odpovede
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Chyba pri odosielaní heartbeat správy: {ex.Message}");
            }
        }

        Console.WriteLine("Heartbeat zastavený.");
    }


    public void SetMaxFragmentSize(int newSize)
    {
        if (newSize < 20 || newSize > 1400)
        {
            throw new ArgumentOutOfRangeException("Fragment size must be between 20 and 1400 bytes.");
        }
        maxFragmentSize = newSize;
    }

    public bool IsConnected()
    {
        return isConnected;
    }
}
