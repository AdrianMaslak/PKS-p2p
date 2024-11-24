// Program.cs
using System;
using System.IO;
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

        // Získanie maximálnej veľkosti fragmentu od používateľa
        const int MAX_FRAGMENT_SIZE_LIMIT = 1400; // MTU pre Ethernet je 1500 bajtov

        Console.Write($"Zadajte maximálnu veľkosť fragmentu (max {MAX_FRAGMENT_SIZE_LIMIT} bajtov): ");
        int maxFragmentSize;
        while (true)
        {
            if (int.TryParse(Console.ReadLine(), out maxFragmentSize))
            {
                if (maxFragmentSize > MAX_FRAGMENT_SIZE_LIMIT)
                {
                    Console.WriteLine($"Veľkosť fragmentu nemôže byť väčšia ako {MAX_FRAGMENT_SIZE_LIMIT} bajtov. Skúste znova:");
                }
                else if (maxFragmentSize < 20) // Minimálna veľkosť fragmentu (príklad)
                {
                    Console.WriteLine("Veľkosť fragmentu musí byť aspoň 20 bajtov. Skúste znova:");
                }
                else
                {
                    break;
                }
            }
            else
            {
                Console.WriteLine("Neplatný vstup. Zadajte celé číslo:");
            }
        }

        UdpPeer localPeer = new UdpPeer(receivePort, remoteAddress, sendPort, maxFragmentSize);

        // Vlákno pre počúvanie (každý uzol počúva na svojom porte)
        Task receivingTask = Task.Run(async () =>
        {
            await localPeer.StartReceivingAsync(message =>
            {
                if (message.StartsWith("<FILENAME>"))
                {
                    // Extrahujeme názov súboru a obsah
                    int endIndex = message.IndexOf("<END>");
                    string fileName = message.Substring(10, endIndex - 10);
                    string fileContent = message.Substring(endIndex + 5);

                    // Prevod z Base64
                    byte[] fileBytes = Convert.FromBase64String(fileContent);

                    // Uložíme súbor
                    string filePath = Path.Combine(Environment.CurrentDirectory, fileName);
                    File.WriteAllBytes(filePath, fileBytes);

                    Console.WriteLine($"Súbor '{fileName}' bol prijatý a uložený na: {filePath}");
                }
                else
                {
                    Console.WriteLine($"Prijatá správa: {message}");
                }
            });
        });

        // Vlákno pre odosielanie (každý uzol posiela správy na port vzdialeného uzla)
        Task sendingTask = Task.Run(async () =>
        {
            // Odosielanie handshake
            await localPeer.SendHandshakeAsync();
            Console.WriteLine("Handshake odoslaný");

            // Odosielanie správ po handshaku
            while (true)
            {
                Console.WriteLine("\nVyberte moznost:");
                Console.WriteLine("1. Odoslat textovu spravu");
                Console.WriteLine("2. Odoslat subor");
                Console.WriteLine("3. Zmenit velkost fragmentu");
                Console.WriteLine("Napiste 'exit' pre ukoncenie.");
                Console.Write("Vasa voľba: ");
                string choice = Console.ReadLine();

                if (choice.ToLower() == "exit")
                    break;

                if (choice == "1")
                {
                    Console.Write("Zadajte správu: ");
                    string messageToSend = Console.ReadLine();
                    if (string.IsNullOrEmpty(messageToSend))
                    {
                        Console.WriteLine("Správa nemôže byť prázdna.");
                        continue;
                    }

                    if (localPeer.IsConnected())
                    {
                        await localPeer.SendMessageAsync(messageToSend);
                    }
                    else
                    {
                        Console.WriteLine("Nie ste pripojení. Čakajte na handshake.");
                    }
                }
                else if (choice == "2")
                {
                    Console.Write("Zadajte cestu k súboru: ");
                    string filePath = Console.ReadLine();
                    if (string.IsNullOrEmpty(filePath))
                    {
                        Console.WriteLine("Cesta k súboru nemôže byť prázdna.");
                        continue;
                    }

                    if (File.Exists(filePath))
                    {
                        try
                        {
                            byte[] fileBytes = File.ReadAllBytes(filePath);
                            string fileContent = Convert.ToBase64String(fileBytes); // Prevod na Base64
                            string fileName = Path.GetFileName(filePath);

                            // Pridáme názov súboru na začiatok dát s markerom
                            string messageToSend = $"<FILENAME>{fileName}<END>{fileContent}";

                            if (localPeer.IsConnected())
                            {
                                await localPeer.SendMessageAsync(messageToSend, fileName); // Odovzdáme fileName
                            }
                            else
                            {
                                Console.WriteLine("Nie ste pripojení. Čakajte na handshake.");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Chyba pri čítaní súboru: {ex.Message}");
                        }
                    }
                    else
                    {
                        Console.WriteLine("Súbor nenájdený.");
                    }
                }
                else if (choice == "3")
                {
                    Console.Write($"Zadaj novu velkost fragmentu (medzi 20 a {MAX_FRAGMENT_SIZE_LIMIT} bajtov): ");
                    try
                    {
                        int newSize = int.Parse(Console.ReadLine());

                        if (newSize < 20 || newSize > MAX_FRAGMENT_SIZE_LIMIT)
                        {
                            Console.WriteLine($"Chyba: Velkost musi byt medzi 20 a {MAX_FRAGMENT_SIZE_LIMIT} bajtov.");
                        }
                        else
                        {
                            localPeer.SetMaxFragmentSize(newSize);
                            Console.WriteLine($"Velkost fragmentu nastavena na {newSize} bajtov.");
                        }
                    }
                    catch (FormatException)
                    {
                        Console.WriteLine("Chyba: Neplatny format vstupu. Zadaj ciselnu hodnotu.");
                    }
                    catch (OverflowException)
                    {
                        Console.WriteLine("Chyba: Zadana hodnota je prilis velka alebo prilis mala.");
                    }
                }

                else
                {
                    Console.WriteLine("Neplatná voľba.");
                }
            }
        });

        // Čakanie na ukončenie úloh, receivingTask pokračuje neustále
        await Task.WhenAny(receivingTask, sendingTask);
    }
}
