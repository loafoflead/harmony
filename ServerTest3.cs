using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

using System.Threading;
using System.Threading.Tasks;

using System.Collections;
using System.Collections.Generic;

public class client {
    public Socket socket;
    public string username;
    public string uuid;
    public bool connected;

    public List<Msg> msgs;

    public client(Socket s, string username) {
        this.socket = s;
        this.username = username;
        this.uuid = Guid.NewGuid().ToString();
        this.connected = true;
        msgs = new List<Msg>();
    }
}

public struct Msg {
    public string content;
    public DateTime time;
    public client sender;
    public int sizeInBytes;

    public Msg(string content, client sender, int byteSize) {
        this.content = content;
        this.sender = sender;
        this.sizeInBytes = byteSize;
        this.time = DateTime.Now;
    }
}


public class ServerTest3 {

    private bool online = true;
    private bool listenForNewClients = true;

    private int deleteListAfter = 1000;
    private bool saveLogToTextFileWhenCleared = false;

    private List<client> clientList;
    private List<Msg> internalMessagesList;
    private Socket listener;

    private Thread consoleCommandThread;
    private Thread newClientThread;

    private Thread sendingThread;
    private Thread constantMessagingThread;
    private List<Msg> messagesForSendingThreadToSend;

    private string ipAddressStr = "localhost";
    private int maxMsgSizeInBytes = 1024;
    private int maxClientQueue = 10;

    private string[] commands = new string[] {
        "msgs: lists all messages.", "rmsgs: lists all recent messages.",
        "format <rainbow/highlight> <text>: sends a message in the desired format.", "> <text>: outputs a message to connected clients.",
        "clients: lists all ongoing connections.", "kick <client name/uuid>: removes the specified client from the server.", 
        "msgc: outputs the number of messages in the log.", "clr: clears the message log.", "cls: clears the console window.",
        "close: shuts down the server.", "help: bring up this page."
    };

    public ServerTest3() {

        try {
			string[] lines = System.IO.File.ReadAllLines("config.txt");
			ipAddressStr = lines[0].Split(' ',2)[0];
            maxMsgSizeInBytes = int.Parse(lines[1].Split(' ',2)[0]);
            maxClientQueue = int.Parse(lines[2].Split(' ',2)[0]);
            deleteListAfter = int.Parse(lines[3].Split(' ',2)[0]);
            saveLogToTextFileWhenCleared = bool.Parse(lines[4].Split(' ', 2)[0].ToLower());
		} catch (Exception e) {
			Console.WriteLine("Missing config.txt file or it is in the incorrect format, create file or ignore for defaults. Ingore? [Y/N]");
            Console.WriteLine("nerd stuff: " + e.Message);
		}

        IPAddress ipAddress;
        IPEndPoint localEndPoint;

         try {
            IPHostEntry host = Dns.GetHostEntry(ipAddressStr);
            ipAddress = host.AddressList[0];
            localEndPoint = new IPEndPoint(ipAddress, 11000);
        } catch {
            Console.WriteLine("Unkown ip address: host does not exist. Type 'ipconfig' into a terminal to find your ipv4 address.");
            quit();
            return;
        }

        try {
        	listener = new Socket(ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp); // open a listener with tcp 
            // A Socket must be associated with an endpoint using the Bind method
            listener.Bind(localEndPoint); // so the endpoint is the ip address ?? 

            listener.Listen(maxClientQueue);
       	} catch {
	       	Console.WriteLine("Invalid ip address. Quitting...");
	       	quit();
	       	return;
	    }

        clientList = new List<client>();
        internalMessagesList = new List<Msg>();
        messagesForSendingThreadToSend = new List<Msg>();

        sendingThread = new Thread(receiveMessagesAndResendThem);
        consoleCommandThread = new Thread(listenForCommands);
        newClientThread = new Thread(awaitAndAcceptNewClientsAsync);
        constantMessagingThread = new Thread(sendToAllClients);

        newClientThread.Start();
        sendingThread.Start();
        constantMessagingThread.Start();
        consoleCommandThread.Start();

        Console.WriteLine("Server started!!");

    }

    private async void awaitAndAcceptNewClientsAsync() {
         while (listenForNewClients && online) {

            string data = "";

            Socket s = await listener.AcceptAsync(); // wait for a new connection 

            Console.WriteLine("New client! {0}", s.RemoteEndPoint.ToString()); // debug 
            Console.WriteLine("Requesting user details...");

            try {
                while (true) {
                    byte[] userdata = new byte[1024];
                    int bytesReceived = await s.ReceiveAsync(userdata, 0); // try and receive the user's data (username...)

                    data += Encoding.ASCII.GetString(userdata, 0, bytesReceived);
                    if (data.IndexOf('\0') > -1) {
                        break;
                    }
                }
                data.Replace("\0", "");
            } catch {
                Console.WriteLine("Client quit while trying to connect :(");
            }

            Console.WriteLine("Username: " + data + " joined!");
            client newCl = new client(s, data);
            clientList.Add(newCl); // add new client to client list
            Task<Msg> newTask = getClientMessage(newCl);
            taskList.Add(newTask);
        } 
    }

    private List<Task<Msg>> taskList;

    private async void receiveMessagesAndResendThem() {
        taskList = new List<Task<Msg>>();
        while(online) {
            if (taskList.Count < 1) {
                continue;
            }
            // for(int i = 0; i < clientList.Count; i ++) {
            //     try {
            //         Task<Msg> newTask = getClientMessage(clientList[i]);
            //         taskList.Add(newTask);
            //     } catch {
            //         Console.WriteLine("Big problem in receiveMessagesAndResendThem function!!!");
            //     }
            // }
            Task<Msg> complete = await Task.WhenAny(taskList);
            if (!complete.IsCompletedSuccessfully) {
                Console.WriteLine("Message receiving task failed, aborting it- ...");
                taskList.Remove(complete);
                continue;
            }
            internalMessagesList.Add(complete.Result);
            messagesForSendingThreadToSend.Add(complete.Result);
            taskList.Remove(complete);
            Task<Msg> newTask = getClientMessage(complete.Result.sender);
            taskList.Add(newTask);
        }
    }

    private async void sendToAllClients() {
        while(online) {
            if (clientList.Count < 1) {
                continue;
            }
            if (messagesForSendingThreadToSend.Count < 1) {
                continue;
            }
            int listIndex = messagesForSendingThreadToSend.Count - 1;
            List<Task> taskList = new List<Task>();
            for(int i = 0; i < clientList.Count; i ++) {
                try {
                    Task newTask = sendToClientAsync(clientList[i], messagesForSendingThreadToSend[listIndex]);
                    taskList.Add(newTask);
                } catch {
                    Console.WriteLine("Error iterating through client list in 'sendToAllClients()', ignoring.");
                }
            }
            Task completed = await Task.WhenAny(taskList);
            taskList.Clear();
        }
    }

    private async Task sendToClientAsync(client cl, Msg message) {
        if (message.sender == cl) {
            return;
        }
        if (cl.msgs.Contains(message)) {
            return;
        }
        try {
            byte[] data = Encoding.ASCII.GetBytes(message.sender.username + "/" + message.content + "\0");
            int bytesSent = await cl.socket.SendAsync(data, 0);
            cl.msgs.Add(message);
        } catch (Exception e) {
            Console.WriteLine("Error sending message to client : " + e.Message);
        }
    }

    private async Task<Msg> getClientMessage(client cl) {
        if (cl == null) {
            cl.connected = false;
            return new Msg("has left the server.", cl, 0);
        }
        byte[] data_received = new byte[maxMsgSizeInBytes];
        int bytesReceived = 0;
        string stringReceived = "";
        try {
            while (true) {
                byte[] userdata = new byte[1024];
                bytesReceived = await cl.socket.ReceiveAsync(userdata, 0); // try and receive the user's data (username...)

                stringReceived += Encoding.ASCII.GetString(userdata, 0, bytesReceived);
                if (stringReceived.IndexOf('\0') > -1) {
                    break;
                }
            }
            stringReceived.Replace("\0", "");
        } catch {
            if (cl == null) {
                return new Msg("Error receiving client message, ingoring.", new client(listener, "server"), 0);
            }
            Console.WriteLine("Client disconnected: " + cl.username);
            cl.connected = false;
            cl.socket.Shutdown(SocketShutdown.Both);
            cl.socket.Close();
            clientList.Remove(cl);
            return new Msg("has left the server.", cl, bytesReceived);
        }
        if (Encoding.ASCII.GetString(data_received) == "DISCONNECTED") {
            Console.WriteLine("received close symbol");
            cl.connected = false;
            cl.socket.Shutdown(SocketShutdown.Both);
            cl.socket.Close();
            clientList.Remove(cl);
            return new Msg("has left the server.", cl, bytesReceived);
        }
        Console.WriteLine("<" + cl.username + ">: " + stringReceived);
        Msg abogus = new Msg(stringReceived, cl, bytesReceived); 
        return abogus;
    }

    private void quit() {
        online = false;
        Console.WriteLine("Press any key to quit...");
        Console.ReadKey();
    }

    private void listenForCommands() {
        while (online) {

            string input = Console.ReadLine();
            if (string.IsNullOrEmpty(input)) {
                continue;
            }

            if (input[0] != '>') 
            {
                string inp = input;
                if (input.Contains(" ")) {
                    inp = input.Split(" ", 2)[0];
                }
                switch (inp) {
                    case "close":
                    case "quit":
                        online = false;
                        if (saveLogToTextFileWhenCleared == true) {
                            List<string> messages = new List<string>();
                            foreach(Msg ms in internalMessagesList) {
                                messages.Add("<"+ms.sender.username+">: " + ms.content + "\n");
                            }
                            string logFileName = "chatlogfile";
                            bool freeName = false;
                            int index = 0;
                            while (!freeName) {
                                if (System.IO.File.Exists(logFileName + index.ToString())) {
                                    index ++;
                                } else {
                                    freeName = true;
                                }
                            }
                            System.IO.File.WriteAllLines(logFileName + index.ToString() + ".txt", messages);
                        }
                        quit();
                        return;

                    case "msgs":
                        if (internalMessagesList.Count < 1) {
                            Console.WriteLine("No messages.");
                        }
                        for(int i = 0; i < internalMessagesList.Count; i ++) {
                            Console.WriteLine("<" + internalMessagesList[i].sender.username + "> " + internalMessagesList[i].content);
                        }
                        break;

                    case "rmsg":
                        if (messagesForSendingThreadToSend.Count < 1) {
                            Console.WriteLine("No messages.");
                        }
                        for(int i = 0; i < messagesForSendingThreadToSend.Count; i ++) {
                            Console.WriteLine("<" + messagesForSendingThreadToSend[i].sender.username + "> " + messagesForSendingThreadToSend[i].content);
                        }
                        break;

                    case "msgscount":
                    case "msgsc":
                        if (internalMessagesList.Count < 1) {
                            Console.WriteLine("No messages.");
                        }
                        Console.WriteLine("There " + (internalMessagesList.Count == 1 ? "is one message." : "are " + internalMessagesList.Count + " messages."));
                        break;

                    case "kick":
                    case "disconnect":
                    case "removeuser":
                    case "deluser":
                    case "/kill":
                        if (!input.Contains(" ")) {
                            Console.WriteLine("Must input user to be kicked, for a list of users type 'clients'");
                            break;
                        }
                        string user = input.Split(" ", 2)[1];
                        List<client> clinetsFound = new List<client>();
                        int nameCount = 0;
                        foreach(client cl in clientList) {
                            if (cl.username == user) {
                                nameCount ++;
                                clinetsFound.Add(cl);
                            }
                        }
                        if (nameCount > 1) {
                            Console.WriteLine("Found multiple users with the same username, choose one.");
                            int index = 1;
                            foreach(client clin in clinetsFound) {
                                Console.WriteLine(index + ": " + clin.username + "; " + clin.uuid + "> " + clin.socket.RemoteEndPoint.ToString());
                                index ++;
                            }
                            string inpoop = Console.ReadLine();
                            int indexChosen = 0;
                            try {
                                indexChosen = int.Parse(inpoop);
                                Console.WriteLine("Kicked " + clinetsFound[indexChosen].username);
                                clinetsFound[indexChosen].connected = false;
                                clinetsFound[indexChosen].socket.Shutdown(SocketShutdown.Both);
                                clinetsFound[indexChosen].socket.Close();
                                clientList.Remove(clinetsFound[indexChosen]);
                            } catch {
                                Console.WriteLine("You either inputted an out of range index or an error occured kicking the user.");
                            }
                            break;
                        }
                        else {
                            try {
                                Console.WriteLine("Kicked " + clinetsFound[0].username);
                                clinetsFound[0].connected = false;
                                clinetsFound[0].socket.Shutdown(SocketShutdown.Both);
                                clinetsFound[0].socket.Close();
                                clientList.Remove(clinetsFound[0]);
                            } catch {
                                Console.WriteLine("There was an error kicking the client; perhaps they disconnected already?");
                            }
                        }
                        break;

                    case "cls":
                        Console.Clear();
                        break;

                    case "clr":
                        if (saveLogToTextFileWhenCleared == true) {
                            List<string> messages = new List<string>();
                            foreach(Msg ms in internalMessagesList) {
                                messages.Add("<"+ms.sender.username+">: " + ms.content + "\n");
                            }
                            string logFileName = "chatlogfile";
                            bool freeName = false;
                            int index = 0;
                            while (!freeName) {
                                if (System.IO.File.Exists(logFileName + index.ToString())) {
                                    index ++;
                                } else {
                                    freeName = true;
                                }
                            }
                            System.IO.File.WriteAllLines(logFileName + index.ToString() + ".txt", messages);
                        }
                        internalMessagesList = new List<Msg>();
                        messagesForSendingThreadToSend = new List<Msg>();
                        Console.WriteLine("Cleared recent and internal messages list.");
                        break;

                    case "format":
                        if (input.Split(" ").Length < 3) {
                            Console.WriteLine("Incorrect number of arguments.");
                            break;
                        }
                        string finalString = "";

                        string format = input.Split(" ", 2)[1].Split(" ", 2)[0];
                        string to_print = input.Split(" ", 2)[1].Split(" ", 2)[1];
                        switch (format) {
                            case "rainbow":
                                finalString = Parser.formatString(to_print, Parser.colourFormatOptions.rainbow, "", "");
                                break;

                            case "rainbow-1":
                                finalString = Parser.formatString(to_print, Parser.colourFormatOptions.rainbow, "highlight", "");
                                break;

                            case "half":
                                finalString = Parser.formatString(to_print, Parser.colourFormatOptions.half_colour, "white", "red");
                                break;

                            case "caps":
                                finalString = to_print.ToUpper();
                                break;

                            case "lowercase":
                                finalString = to_print.ToLower();
                                break;

                            case "highlight":
                                finalString = "|black/white|" + to_print;
                                break;

                            default:
                                finalString = Parser.formatString(to_print, Parser.colourFormatOptions.one_colour, format, "black");
                                break;
                        }
                        internalMessagesList.Add(new Msg(finalString + "\0", new client(listener, "server"), Encoding.ASCII.GetByteCount(finalString)));
                        messagesForSendingThreadToSend.Add(new Msg(finalString + "\0", new client(listener, "server"), Encoding.ASCII.GetByteCount(finalString)));
                        break;

                    case "clients":
                        if (clientList.Count < 1) {
                            Console.WriteLine("No clients :(");
                        }
                        for(int i = 0; i < clientList.Count; i ++) {
                            Console.WriteLine((i + 1).ToString() + ": " + clientList[i].username + ", uuid: " + clientList[i].uuid);
                        }
                        break;

                    case "help":
                    case "?":
                        foreach(string comand in commands) {
                            Console.WriteLine("\\> " + comand);
                        }
                        break;

                    default:
                        Console.WriteLine("Unknown command, type 'help' or '?' for a list of available commands.");
                        break;
                }
            }
            else {
                if (input.Split('>',2)[1].Length < 1) {
                    Console.WriteLine("Attempted to send nothing, cancelling.");
                }
                internalMessagesList.Add(new Msg(input.Split('>', 2)[1] + "\0", new client(listener, "server"), Encoding.ASCII.GetByteCount(input)));
                messagesForSendingThreadToSend.Add(new Msg(input.Split('>', 2)[1] + "\0", new client(listener, "server"), Encoding.ASCII.GetByteCount(input)));
            }
        }
    }


}