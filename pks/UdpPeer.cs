﻿using System;
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

    public UdpPeer(int receivePort, string remoteAddress, int remotePort)
    {
        // Inicializácia UDP klienta na počúvanie
        receivingClient = new UdpClient(receivePort);
        // Inicializácia UDP klienta na odosielanie správ (bez špecifického portu)
        sendingClient = new UdpClient();
        remoteEndPoint = new IPEndPoint(IPAddress.Parse(remoteAddress), remotePort);
    }

    // Vlákno na počúvanie prichádzajúcich správ
    public async Task StartReceivingAsync(Action<string> onMessageReceived)
    {
        while (true)
        {
            // Prijatá správa
            var result = await receivingClient.ReceiveAsync();
            string receivedMessage = Encoding.UTF8.GetString(result.Buffer);

            if (!isConnected)
            {
                // Spracovanie handshake
                if (receivedMessage == "SYN")
                {
                    Console.WriteLine("Prijaté SYN, odosielam SYN_ACK");
                    await SendMessageAsync("SYN_ACK");
                }
                else if (receivedMessage == "SYN_ACK")
                {
                    Console.WriteLine("Prijaté SYN_ACK, odosielam ACK");
                    await SendMessageAsync("ACK");
                    isConnected = true;
                    handshakeComplete = true;
                }
                else if (receivedMessage == "ACK")
                {
                    Console.WriteLine("Handshake úspešný");
                    isConnected = true;
                    handshakeComplete = true;
                }
            }

            // Spracovanie bežných správ po úspešnom handshaku
            if (isConnected)
            {
                onMessageReceived?.Invoke(receivedMessage);
            }
        }
    }

    // Odosielanie na vzdialený uzol
    public async Task SendMessageAsync(string message)
    {
        byte[] messageBytes = Encoding.UTF8.GetBytes(message);
        await sendingClient.SendAsync(messageBytes, messageBytes.Length, remoteEndPoint);
    }
    public async Task SendMessageWithHeaderAsync(string message, byte messageType, ushort sourcePort, ushort destinationPort)
    {
        // Vytvorenie hlavičky
        Header header = new Header
        {
            Type = messageType, // Typ správy (napr. 0x01 pre SYN)
            SourcePort = sourcePort,
            DestinationPort = destinationPort,
            Data = message
        };

        // Prevedenie hlavičky a správy do bytu
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
            await SendMessageAsync("SYN");

            // Čakáme 2 sekundy pred ďalším pokusom
            await Task.Delay(2000);

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
