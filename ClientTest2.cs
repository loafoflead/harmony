using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

using System.Threading;
using System.Threading.Tasks;

using System.Collections;
using System.Collections.Generic;

public struct Message {
    public string content;
    public DateTime time;
    public string sender;
    public int sizeInBytes;

    public Message(string content, string sender, int byteSize) {
        this.content = content;
        this.sender = sender;
        this.sizeInBytes = byteSize;
        this.time = DateTime.Now;
    }
}

public class ClientTest2 {

    private bool online = true;

    private int maxMsgSize = 1024;
    private string username = "unknown";

    private string addressStr = "localhost";

    private List<Message> messagesList;
    
    private Thread inputAndSendingThread;
    private Thread serverDataThread;

    private Socket server;

    IPAddress ipAddress;
    IPEndPoint remoteEP;

    public ClientTest2() {

        Console.WriteLine("Starting...");

        Console.CancelKeyPress += new ConsoleCancelEventHandler(myHandler);

        inputAndSendingThread = new Thread(getInput);
        serverDataThread = new Thread(receiveFromServer);
        messagesList = new List<Message>();

        try {
			string[] config_file = System.IO.File.ReadAllLines("config.txt");
			addressStr = config_file[0];
            username = config_file[1];
            if (username.Contains("/")) {
                Console.WriteLine("Username contains illegal characters, removing them...");
                username = username.Replace("/", "\\");
            }
		} catch {
			Console.WriteLine("Couldn't find 'config.txt' or it is in an incorrect format. Continuing with defaults. (ip: localhost)");
		}

        Console.WriteLine("name: ");
        username = Console.ReadLine();

        try {
            IPHostEntry host = Dns.GetHostEntry(addressStr);
            ipAddress = host.AddressList[0];
            remoteEP = new IPEndPoint(ipAddress, 11000);
        } catch {
            Console.WriteLine("No such ip address exists or the connection was actively refused.");
            Console.WriteLine("Press any key to quit...");
            Console.ReadKey();
        }

        // Create a TCP/IP  socket.
        server = new Socket(ipAddress.AddressFamily,
            SocketType.Stream, ProtocolType.Tcp);

        try {
            server.Connect(remoteEP);
        } catch {
            Console.WriteLine("Server didn't wanna be joined or isn't online :/");
            quit();
            return;
        }

        byte[] bytes = new byte[1024];
        bytes = Encoding.ASCII.GetBytes(username + "\0");

        server.Send(bytes);

        Console.WriteLine("Connected and ready!");

        inputAndSendingThread.Start();
        serverDataThread.Start();

    }

    private async void receiveFromServer() {
        while (online) {
            string received = "";
            int bytesReceived = 0;
            try {
                while (true) {
                    byte[] msgBytes = new byte[maxMsgSize];
                    bytesReceived = await server.ReceiveAsync(msgBytes, 0);
                    received += Encoding.ASCII.GetString(msgBytes, 0, bytesReceived);
                    if (received.IndexOf("\0") > -1) {
                        break;
                    }
                }
                received = received.Replace("\0", "");
            } catch {
                Console.WriteLine("Server closed :(");
                quit();
                return;
            }

            if (!received.Contains("/")) {
                Console.WriteLine("Received invalid message: " + received);
                continue;
            }

            messagesList.Add(new Message(received.Split("/", 2)[1], received.Split("/", 2)[0], bytesReceived));
            Console.Write("\n<" + received.Split("/", 2)[0] + ">: ");
            foreach(Parser.SubString substr in Parser.parseString(received.Split("/", 2)[1])) {
                Console.ForegroundColor = substr.fg_colour;
                Console.BackgroundColor = substr.bg_colour;
                Console.Write(substr.content);
            }
            Console.WriteLine();
            
            Console.ForegroundColor = ConsoleColor.White;
            Console.BackgroundColor = ConsoleColor.Black;
            
        }
    }

    private async void getInput() {
        Console.WriteLine();
        while (online) {

            try {
                string toSend = Console.ReadLine();

                if(string.IsNullOrEmpty(toSend)) {
                    continue;
                }

                if (toSend == "msg") {
                    foreach(Message m in messagesList) {
                        Console.WriteLine(m.sender + ": " + m.content);
                    }
                    continue;
                }
                if (toSend == "quit") {
                    online = false;
                    quit();
                    return;
                }

                byte[] msgBytes = new byte[maxMsgSize];

                msgBytes = Encoding.ASCII.GetBytes(toSend + "\0");
                int bytesSent = await server.SendAsync(msgBytes, 0);

                Console.SetCursorPosition(0, Console.CursorTop-1);
                Console.Write(" ".PadRight(Console.WindowWidth - 2));
                Console.SetCursorPosition(0, Console.CursorTop);
                Console.Write("\n<" + username + ">: " );
                foreach(Parser.SubString substr in Parser.parseString(toSend)) {
                    Console.ForegroundColor = substr.fg_colour;
                    Console.BackgroundColor = substr.bg_colour;
                    Console.Write(substr.content);
                }
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.White;
                Console.BackgroundColor = ConsoleColor.Black;
            } catch {
                Console.WriteLine("You message may have been refused, try rejoining.");
                online = false;
                quit();
            }

        }
    }

    private void quit() {
        online = false;
        Console.WriteLine("Press any key to quit...");
        Console.ReadKey();
    }

    private void myHandler(object sender, ConsoleCancelEventArgs e) {
        e.Cancel = true; 
        online = false;
        quit();
    }


}