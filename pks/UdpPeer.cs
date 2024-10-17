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
            onMessageReceived?.Invoke(receivedMessage);
        }
    }

    public async Task SendMessageAsync(string message)
    {
        byte[] messageBytes = Encoding.UTF8.GetBytes(message);
        await udpClient.SendAsync(messageBytes, messageBytes.Length, remoteEndPoint);
    }
}


