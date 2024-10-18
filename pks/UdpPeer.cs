using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

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
                if (receivedMessage == "SYN")
                {
                    Console.WriteLine("Prijate SYN, odosielam SYN_ACK");
                    SendMessageAsync("SYN_ACK");
                }
                else if (receivedMessage == "SYN_ACK")
                {
                    Console.WriteLine("prijate SYN_ACK, odosielam ACK");
                    await SendMessageAsync("ACK");
                }
                else if ( receivedMessage == "ACK")
                {
                     Console.WriteLine("Handshake uspesny");
                    isConnected = true;
                }

            }
            onMessageReceived?.Invoke(receivedMessage);

        }
    }

    public bool IsConnected()
    {
        return isConnected;
    }

    public async Task SendHandshake()
    {

            await SendMessageAsync("SYN");
            await Task.Delay(5000);



    }

    public async Task SendMessageAsync(string message)
    {
        byte[] messageBytes = Encoding.UTF8.GetBytes(message);
        await udpClient.SendAsync(messageBytes, messageBytes.Length, remoteEndPoint);
    }
}
