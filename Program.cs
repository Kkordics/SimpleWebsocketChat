using System;
using System.Collections.Generic;
using System.Net;
using Websockets;
using System.Threading;

namespace TEsztek
{
    public static class HandleConnection 
    {

        static List<RClient> clients = new List<RClient>();
        /// <summary>
        /// Gets the registered clients names
        /// </summary>
        /// <param name="rClients"></param>
        /// <returns></returns>
        public static List<string> GetNames(this List<RClient> rClients) 
        {
            List<string> names = new List<string>();
            for (int i = 0; i < rClients.Count; i++)
            {

                if(rClients[i].Name != null && rClients[i].Connected) 
                {
                    names.Add(rClients[i].Name);
                }

                
            }
            return names;
        }

        public struct RClient 
        {
            public WebSocketClient Client;
            public string Message;
            public string Name;
            public bool Connected;
        }

        /// <summary>
        /// Sends the registered clients names to everybody
        /// </summary>
        public static void SendTags() 
        {
            string handler = string.Format("Members: {0}", string.Join("|", clients.GetNames()));
            for (int i = 0; i < clients.Count; i++)
            {
                clients[i].Client.Write(handler);
            }
        }

        public static string GetTags() 
        {
            return string.Join("|", clients.GetNames());
        }

        /// <summary>
        /// Registers the client to the list and start listening incoming messages on a new thread
        /// </summary>
        /// <param name="client"></param>
        public static void ClientRegister(WebSocketClient client) 
        {
            clients.Add(new RClient {Client = client, Connected = true});
            //Start reading
            Thread t = new Thread(() => Read(clients.Count-1));
            t.Start();

           
            SendTags();

            client.Write("#Server: Write your name!");


            //Start handling the writing
            if (clients.Count == 1) 
            {
                Thread w = new Thread(Write);
                w.Start();
            }

        }
        /// <summary>
        /// Sends the message to everyone except the specific client
        /// </summary>
        /// <param name="index"></param>
        /// <param name="msg"></param>
        public static void SendToEveryoneElse(int index, string msg) 
        {
            for (int i = 0; i < clients.Count; i++)
            {
                if (i != index) 
                {
                    clients[i].Client.Write(msg);
                }
            }
        }


        /// <summary>
        /// If someone has message it will send to everyone else
        /// </summary>
        public static void Write() 
        {


            int havemsg = 0;
            RClient handler;

            while (true) 
            {
                havemsg = -1;

                //Search for message
                for (int i = 0; i < clients.Count; i++)
                {
                    if(clients[i].Message != null && clients[i].Message != string.Empty) 
                    {
                        havemsg = i;


                        break;
                    }
                }


                if (havemsg!= - 1)
                {
                    //Send message
                    for (int i = 0; i < clients.Count; i++)
                    {
                        
                        
                        if(i != havemsg&&clients[havemsg].Name != null)
                        {
                            clients[i].Client.Write(string.Format("{0}: {1}", clients[havemsg].Name, clients[havemsg].Message));
                        }

                    }

                    //Delete the sent message
                    handler = clients[havemsg];

                    handler.Message = null;
                    clients[havemsg] = handler;
                }
                
            }

        }

        /// <summary>
        /// Receives the message for a specific client
        /// </summary>
        /// <param name="index"></param>
        public static void Read(int index) 
        {
            
            RClient handler;

            while (clients[index].Client.Connected) 
            {
                handler = clients[index];
                handler.Message = clients[index].Client.Read();
                //If client has name
                if(clients[index].Name != null) 
                {
                    Console.WriteLine("[{0}]: {1}",handler.Name,handler.Message);
                }
                else 
                {
                    handler.Name = handler.Message;
                    SendToEveryoneElse(index,GetTags());
                }    
                

                if(handler.Message.ToLower() == "ping") 
                {
                    clients[index].Client.Write(clients[index].Client.Ping().ToString());
                }

                clients[index] = handler;
            }
            
            handler = clients[index];
            handler.Connected = false;
            clients[index] = handler;
            SendToEveryoneElse(index,string.Format("Server: {0} Disconnected!", clients[index].Name));
        }
    }
    

    public class Program
    {
        
       
        static void Main(string[] args)
        {
            
            //Create a websocket server
            WebSocketServer server = new WebSocketServer(IPAddress.Parse("192.168.1.100"), 8080);
            server.Start();
            WebSocketClient client;


            

            while (true) 
            {
                //Accept the client connection
                client = server.AcceptWebsocketClient();
                Console.WriteLine("Sombody connected");
                
                

                Thread t = new Thread(() => HandleConnection.ClientRegister(client));
                t.Start();

                
            }
            
        }
    }
}
