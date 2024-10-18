
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

        Console.WriteLine("Zadajte správy na odoslanie. Zadajte 'exit' pre ukončenie.");
        while (true)
        {
            string messageToSend = Console.ReadLine();
            if (messageToSend.ToLower() == "exit")
                break;

            await localPeer.SendMessageAsync(messageToSend);
        }
    }
}
