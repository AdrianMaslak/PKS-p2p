using System;
using System.Threading.Tasks;

class Program
{
    static async Task Main(string[] args)
    {
        // Získanie portov a IP adresy od používateľa
        Console.Write("Zadajte svoj receiving port (počúvanie): ");
        int receivePort = int.Parse(Console.ReadLine());

        Console.Write("Zadajte remote IP adresu (odosielanie na tento uzol): ");
        string remoteAddress = Console.ReadLine();

        Console.Write("Zadajte remote sending port (port, na ktorý budeme posielať správy): ");
        int sendPort = int.Parse(Console.ReadLine());

        UdpPeer localPeer = new UdpPeer(receivePort, remoteAddress, sendPort);

        // Vlákno pre počúvanie (každý uzol počúva na svojom porte)
        Task receivingTask = Task.Run(async () =>
        {
            await localPeer.StartReceivingAsync(message =>
            {
                Console.WriteLine($"Prijaté: {message}");
            });
        });

        // Vlákno pre odosielanie (každý uzol posiela správy na port vzdialeného uzla)
        Task sendingTask = Task.Run(async () =>
        {
            // Odosielanie handshake
            await localPeer.SendHandshakeAsync();
            Console.WriteLine("Handshake odoslaný");

            // Odosielanie správ po handshaku
            Console.WriteLine("Zadajte správy na odoslanie. Zadajte 'exit' pre ukončenie.");
            while (true)
            {
                string messageToSend = Console.ReadLine();
                if (messageToSend.ToLower() == "exit")
                    break;

                if (localPeer.IsConnected())
                {
                    await localPeer.SendMessageWithHeaderAsync(messageToSend, 0x05, (ushort)receivePort, (ushort)(sendPort), localPeer.LocalSequenceNumber + 1, localPeer.RemoteSequenceNumber + 1);                    //await localPeer.SendMessageAsync(messageToSend);
                }
                else
                {
                    Console.WriteLine("Nie ste pripojení. Čakajte na handshake.");
                }
            }
        });

        // Čakanie na ukončenie úloh, receivingTask pokračuje neustále
        await Task.WhenAny(receivingTask, sendingTask);
    }
}
