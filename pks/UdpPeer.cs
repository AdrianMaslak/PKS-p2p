using System.Net.Sockets;
using System.Net;
using System.Text;

public class UdpPeer
{
    private UdpClient udpClient;
    private IPEndPoint remoteEndPoint;
    private int listenPort;
    private bool isConnected = false;

    public UdpPeer(int listenPort, string remoteAddress, int remotePort)
    {
        this.listenPort = listenPort;
        udpClient = new UdpClient(listenPort);
        remoteEndPoint = new IPEndPoint(IPAddress.Parse(remoteAddress), remotePort);
    }

    public async Task StartListeningAsync(Action<string> onMessageReceived)
    {
        while (true)
        {
            var result = await udpClient.ReceiveAsync();
            string receivedMessage = Encoding.UTF8.GetString(result.Buffer);
            if (!isConnected)
            {
                if (receivedMessage == "HELLO")
                {
                    Console.WriteLine("Prijatý handshake od remote uzla.");
                    await SendMessageAsync("HELLO_ACK");
                    isConnected = true;
                }
                else if (receivedMessage == "HELLO_ACK")
                {
                    Console.WriteLine("Handshake úspešný.");
                    isConnected = true;
                }
            }
            else
            {
                onMessageReceived?.Invoke(receivedMessage);
            }
        }
    }

    public bool IsConnected
    {
        get { return isConnected; }
    }


    public async Task SendHandshakeAsync()
    {
        // Pridané oneskorenie, aby druhý uzol mal čas na počúvanie
        await Task.Delay(5000); // Pridajte malý delay (500ms) pred odoslaním handshaku
        await SendMessageAsync("HELLO");
    }

    public async Task SendMessageAsync(string message)
    {
        byte[] messageBytes = Encoding.UTF8.GetBytes(message);
        await udpClient.SendAsync(messageBytes, messageBytes.Length, remoteEndPoint);
    }
}
