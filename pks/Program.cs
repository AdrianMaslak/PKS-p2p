using System;
using System.Threading.Tasks;

class Program
{
    static async Task Main(string[] args)
    {
        // Konfigurácia portov a IP adresy
        Console.Write("Zadajte svoj listen port: ");
        int localPort = int.Parse(Console.ReadLine());

        Console.Write("Zadajte remote IP adresu: ");
        string remoteAddress = Console.ReadLine();

        Console.Write("Zadajte remote port: ");
        int remotePort = int.Parse(Console.ReadLine());

        UdpPeer localPeer = new UdpPeer(localPort, remoteAddress, remotePort);
        _ = localPeer.StartListeningAsync(message =>
        {
            Console.WriteLine($"Prijaté: {message}");
        });

        // Odoslanie handshake
        await localPeer.SendHandshakeAsync();
        Console.WriteLine("Handshake odoslaný.");

        Console.WriteLine("Zadajte správy na odoslanie. Zadajte 'exit' pre ukončenie.");
        while (true)
        {
            string messageToSend = Console.ReadLine();
            if (messageToSend.ToLower() == "exit")
                break;

            if (localPeer.IsConnected)
            {
                await localPeer.SendMessageAsync(messageToSend);
            }
            else
            {
                Console.WriteLine("Nie ste pripojení. Čakajte na handshake.");
            }
        }
    }
}
