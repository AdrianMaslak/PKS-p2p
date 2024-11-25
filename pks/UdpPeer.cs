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
    private UdpClient receivingClient; // For receiving messages
    private UdpClient sendingClient;   // For sending messages
    private IPEndPoint remoteEndPoint; // Recipient endpoint
    private bool isConnected = false;  // Connection status
    private bool handshakeComplete = false; // Handshake status
    private uint localSequenceNumber;
    private uint remoteSequenceNumber;

    private DateTime lastHeartbeatSent;
    private DateTime lastHeartbeatReceived;
    private int missedHeartbeats = 0;

    private const int HEARTBEAT_INTERVAL_MS = 5000; // 5 seconds
    private const int MAX_MISSED_HEARTBEATS = 3;

    // ARQ State Variables
    private bool awaitingAck = false;
    private Header lastSentMessage;
    private DateTime ackTimeout;
    private const int ACK_TIMEOUT_MS = 3000; // 3 seconds
    private TaskCompletionSource<bool> ackReceived = new TaskCompletionSource<bool>();

    private int maxFragmentSize;

    private Dictionary<ushort, List<Header>> receivedMessages = new Dictionary<ushort, List<Header>>();
    private Dictionary<ushort, DateTime> messageStartTimes = new Dictionary<ushort, DateTime>();



    public event Action ConnectionLost;

    public UdpPeer(int receivePort, string remoteAddress, int remotePort, int maxFragmentSize)
    {
        // Initialize UDP client for receiving
        receivingClient = new UdpClient(receivePort);
        // Initialize UDP client for sending (without specific port)
        sendingClient = new UdpClient();
        remoteEndPoint = new IPEndPoint(IPAddress.Parse(remoteAddress), remotePort);

        localSequenceNumber = 100; // Initial sequence number
        this.maxFragmentSize = maxFragmentSize;
    }

    // Thread for listening to incoming messages
    public async Task StartReceivingAsync(Action<string> onMessageReceived)
    {
        while (true)
        {
            var result = await receivingClient.ReceiveAsync();
            byte[] receivedBytes = result.Buffer;

            _ = Task.Run(() => ProcessReceivedMessage(receivedBytes, onMessageReceived));
        }
    }

    private void ProcessReceivedMessage(byte[] receivedBytes, Action<string> onMessageReceived)
    {
        try
        {
            Header header = Header.FromBytes(receivedBytes);

            if (header.Flags == 0x07) // Heartbeat
            {
                _ = Task.Run(async () => await SendAckAsync());
                lastHeartbeatReceived = DateTime.Now;
                missedHeartbeats = 0;
                return;
            }
            if (header.Flags == 0x08) // Heartbeat ACK
            {
                lastHeartbeatReceived = DateTime.Now;
                missedHeartbeats = 0;
                return;
            }
            if (header.Flags == 0x09) // Data ACK
            {
                if (awaitingAck && header.AcknowledgmentNumber == lastSentMessage.SequenceNumber)
                {
                    awaitingAck = false;
                    ackReceived.TrySetResult(true);
                    Console.WriteLine($"Received Data ACK for sequence number {header.AcknowledgmentNumber}");
                }
                return;
            }
            if (header.Flags == 0x05) // Data message
            {
                HandleDataMessage(header, onMessageReceived);
                return;
            }
            ProcessControlMessage(header).Wait();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing received data: {ex.Message}");
        }
    }
    private void HandleDataMessage(Header header, Action<string> onMessageReceived)
    {
        if (!receivedMessages.ContainsKey(header.SequenceNumber))
        {
            receivedMessages[header.SequenceNumber] = new List<Header>(new Header[header.TotalFragments]);
            messageStartTimes[header.SequenceNumber] = DateTime.Now;
        }

        var fragmentList = receivedMessages[header.SequenceNumber];
        if (header.FragmentOffset - 1 >= fragmentList.Count)
        {
            Console.WriteLine($"FragmentOffset {header.FragmentOffset} exceeds list size for sequence {header.SequenceNumber}. Discarding.");
            return;
        }

        fragmentList[header.FragmentOffset - 1] = header;

        Console.WriteLine($"Received fragment {header.FragmentOffset}/{header.TotalFragments} for sequence number {header.SequenceNumber}.");
        _ = Task.Run(async () => await SendDataAckAsync(header.SequenceNumber));

        // Odstráňte neúplné správy, ktoré presiahli časový limit
        foreach (var seqNum in messageStartTimes.Keys.ToList())
        {
            if ((DateTime.Now - messageStartTimes[seqNum]).TotalSeconds > 30) // Časový limit 30 sekúnd
            {
                Console.WriteLine($"Discarding stale message with sequence number {seqNum}");
                receivedMessages.Remove(seqNum);
                messageStartTimes.Remove(seqNum);
            }
        }

        if (receivedMessages[header.SequenceNumber].All(h => h != null))
        {
            var messageData = string.Join("", receivedMessages[header.SequenceNumber]
                .OrderBy(h => h.FragmentOffset)
                .Select(h => h.Data));

            onMessageReceived?.Invoke(messageData);

            messageStartTimes.Remove(header.SequenceNumber);
            receivedMessages.Remove(header.SequenceNumber);

            Console.WriteLine($"Complete message received. Sequence: {header.SequenceNumber}, Size: {messageData.Length} bytes.");
        }
    }

    // Method to process handshake and other control messages
    private async Task ProcessControlMessage(Header header)
    {
        if (!handshakeComplete)
        {
            // Handle handshake
            if ((header.Flags & 0x01) != 0 && header.Data == null) // SYN
            {
                Console.WriteLine("Received SYN, sending SYN_ACK");
                remoteSequenceNumber = header.SequenceNumber;
                await SendMessageWithHeaderAsync(null, 0x02, localSequenceNumber, remoteSequenceNumber + 1); // SYN_ACK
            }
            else if ((header.Flags & 0x02) != 0 && header.Data == null) // SYN_ACK
            {
                Console.WriteLine("Received SYN_ACK, sending ACK");
                remoteSequenceNumber = header.SequenceNumber;
                await SendMessageWithHeaderAsync(null, 0x04, localSequenceNumber + 1, remoteSequenceNumber + 1); // ACK
                handshakeComplete = true;
                isConnected = true;

                _ = Task.Run(() => StartHeartbeat());
            }
            else if ((header.Flags & 0x04) != 0 && header.Data == null) // ACK
            {
                Console.WriteLine("Received ACK, handshake successful");
                handshakeComplete = true;
                isConnected = true;

                _ = Task.Run(() => StartHeartbeat());
            }
        }
        else
        {
            // If handshake has already been completed but received SYN, restart handshake
            if ((header.Flags & 0x01) != 0 && header.Data == null) // SYN
            {
                Console.WriteLine("Received SYN during active connection. Restarting handshake.");
                handshakeComplete = false;
                isConnected = false;

                remoteSequenceNumber = header.SequenceNumber;
                await SendMessageWithHeaderAsync(null, 0x02, localSequenceNumber, remoteSequenceNumber + 1); // SYN_ACK
            }
        }
    }

    // Sending a message with fragmentation and Stop-and-Wait ARQ
    public async Task SendMessageAsync(string message, string? filename = null)
    {
        if (!isConnected)
        {
            Console.WriteLine("Connection not established. Attempting handshake...");
            await SendHandshakeAsync();
            // Wait a bit to see if handshake succeeds
            await Task.Delay(2000);
            if (!isConnected)
            {
                Console.WriteLine("Handshake failed. Message not sent.");
                return;
            }
        }

        byte[] messageBytes = Encoding.UTF8.GetBytes(message);
        int totalMessageSize = messageBytes.Length; // Size of the message/file in bytes

        // Calculate maximum data size per fragment
        int headerSize = 1 + 2 * 5; // 11 bytes
        int maxDataPerFragment = maxFragmentSize;
        int totalFragments = (int)Math.Ceiling((double)messageBytes.Length / maxDataPerFragment);

        // Display information about the message/file being sent
        if (filename != null)
        {
            Console.WriteLine($"Sending file: {filename}");
        }

        Console.WriteLine($"Message/File size: {totalMessageSize} bytes");
        if (totalFragments > 1)
        {
            Console.WriteLine($"Number of fragments to send: {totalFragments}");
            Console.WriteLine($"Fragment size: {maxFragmentSize} bytes");
        }

        for (int i = 0; i < totalFragments; i++)
        {
            int offset = i * maxDataPerFragment;
            int dataLength = Math.Min(maxDataPerFragment, messageBytes.Length - offset);
            byte[] fragmentData = new byte[dataLength];
            Array.Copy(messageBytes, offset, fragmentData, 0, dataLength);

            // Create fragment header
            Header header = new Header
            {
                Flags = 0x05, // Data message flag
                SequenceNumber = (ushort)(localSequenceNumber),
                AcknowledgmentNumber = (ushort)remoteSequenceNumber,
                FragmentOffset = (ushort)(i + 1),
                TotalFragments = (ushort)totalFragments,
                Data = Encoding.UTF8.GetString(fragmentData)
            };

            // Serialize and send the fragment
            byte[] fragmentBytes = header.ToBytes();

            if (totalFragments == 1)
            {
                // Single fragment message: send without waiting for ACK
                await sendingClient.SendAsync(fragmentBytes, fragmentBytes.Length, remoteEndPoint);
                Console.WriteLine($"Sent single fragment, size: {dataLength} bytes");
            }
            else
            {
                // Multi-fragment message: implement Stop-and-Wait ARQ
                bool acknowledged = false;
                int retryCount = 0;
                const int MAX_RETRIES = 5;

                for (retryCount = 0; retryCount < MAX_RETRIES; retryCount++)
                {
                    await sendingClient.SendAsync(fragmentBytes, fragmentBytes.Length, remoteEndPoint);
                    Console.WriteLine($"Sent fragment {i + 1}/{totalFragments}, size: {dataLength} bytes (Attempt {retryCount + 1}/{MAX_RETRIES})");

                    // Set the last sent message and start the ACK timeout
                    lastSentMessage = header;
                    awaitingAck = true;

                    // Reset the TaskCompletionSource for the new ACK
                    ackReceived = new TaskCompletionSource<bool>();

                    // Start the ACK timeout task
                    var timeoutTask = Task.Delay(ACK_TIMEOUT_MS);
                    var ackTask = ackReceived.Task;

                    var completedTask = await Task.WhenAny(ackTask, timeoutTask);

                    if (completedTask == ackTask && ackTask.Result)
                    {
                        Console.WriteLine($"Fragment {i + 1} acknowledged.");
                        acknowledged = true;
                        break; // Exit the loop if ACK is received
                    }
                    else
                    {
                        Console.WriteLine($"ACK timeout for fragment {i + 1}. Retrying...");
                    }
                }

                if (!acknowledged)
                {
                    Console.WriteLine($"Failed to receive ACK for fragment {i + 1} after {MAX_RETRIES} attempts. Aborting.");
                    return; // Optionally, throw an exception or handle the failure as needed
                }
            }
        }

        if (totalFragments > 1)
        {
            // Display information about fragment sizes
            Console.WriteLine($"Fragment size: {maxFragmentSize} bytes");
            if (messageBytes.Length % maxDataPerFragment != 0)
            {
                int lastFragmentSize = (messageBytes.Length % maxDataPerFragment);
                Console.WriteLine($"Last fragment size: {lastFragmentSize} bytes");
            }
        }

        localSequenceNumber++;
    }

    // Method to send Data ACKs for multi-fragment messages
    private async Task SendDataAckAsync(ushort sequenceNumber)
    {
        try
        {
            Header ackHeader = new Header
            {
                Flags = 0x09, // Data ACK flag
                SequenceNumber = 0, // Not used for ACK
                AcknowledgmentNumber = sequenceNumber, // Acknowledge the received sequence number
                FragmentOffset = 0,
                TotalFragments = 0,
                Data = null
            };

            byte[] ackMessage = ackHeader.ToBytes();
            await sendingClient.SendAsync(ackMessage, ackMessage.Length, remoteEndPoint);
            Console.WriteLine($"Sent Data ACK for sequence number {sequenceNumber}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending Data ACK: {ex.Message}");
        }
    }

    // Sending a message with a header (e.g., for handshake)
    public async Task SendMessageWithHeaderAsync(string? message, byte messageType, uint seqNumber, uint ackNumber)
    {
        // Create the header
        Header header = new Header
        {
            Flags = messageType,
            SequenceNumber = (ushort)seqNumber,
            AcknowledgmentNumber = (ushort)ackNumber,
            FragmentOffset = 0,
            TotalFragments = 0,
            Data = message
        };

        // Serialize the header and message to bytes
        byte[] messageBytes = header.ToBytes();

        // Send the message
        await sendingClient.SendAsync(messageBytes, messageBytes.Length, remoteEndPoint);
    }

    // Handshake: send SYN until handshake is completed
    public async Task SendHandshakeAsync()
    {
        // Send SYN only until handshake is completed
        while (!handshakeComplete)
        {
            Console.WriteLine("Sending SYN to establish connection...");

            await SendMessageWithHeaderAsync(null, 0x01, localSequenceNumber, 0); // SYN
            // Wait 2 seconds before the next attempt
            await Task.Delay(2000);
            Console.WriteLine($"Sequence Number: {localSequenceNumber}, Acknowledgment Number: {remoteSequenceNumber}");

            if (handshakeComplete)
            {
                Console.WriteLine("Handshake completed, stopping SYN transmission.");
                break;
            }
        }
    }

    // Send Heartbeat ACK
    private async Task SendAckAsync()
    {
        try
        {
            Header header = new Header
            {
                Flags = 0x08, // Heartbeat ACK flag
                SequenceNumber = 0,
                AcknowledgmentNumber = 0,
                FragmentOffset = 0,
                TotalFragments = 0,
                Data = null
            };

            byte[] ackMessage = header.ToBytes();
            await sendingClient.SendAsync(ackMessage, ackMessage.Length, remoteEndPoint);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending Heartbeat ACK: {ex.Message}");
        }
    }

    // Heartbeat mechanism
    private async Task StartHeartbeat()
    {
        lastHeartbeatReceived = DateTime.Now;

        while (true)
        {
            if (!isConnected) break; // Stop heartbeat if not connected

            try
            {
                // Send heartbeat message
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

                // Check if ACK was received
                if ((DateTime.Now - lastHeartbeatReceived).TotalMilliseconds > HEARTBEAT_INTERVAL_MS * MAX_MISSED_HEARTBEATS)
                {
                    missedHeartbeats++;
                    Console.WriteLine($"No response to heartbeat. Missed count: {missedHeartbeats}");

                    if (missedHeartbeats >= MAX_MISSED_HEARTBEATS)
                    {
                        Console.WriteLine("Connection lost. Attempting to re-establish connection...");
                        isConnected = false;
                        handshakeComplete = false;

                        // Reset state and start handshake
                        _ = Task.Run(() => SendHandshakeAsync());
                        break;
                    }
                }
                else
                {
                    missedHeartbeats = 0; // Reset on successful ACK
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending heartbeat: {ex.Message}");
            }
        }

        Console.WriteLine("Heartbeat stopped.");
    }

    // Set maximum fragment size
    public void SetMaxFragmentSize(int newSize)
    {
        if (newSize < 20 || newSize > 1400)
        {
            throw new ArgumentOutOfRangeException("Fragment size must be between 20 and 1400 bytes.");
        }
        maxFragmentSize = newSize;
    }

    // Check if connected
    public bool IsConnected()
    {
        return isConnected;
    }
}
