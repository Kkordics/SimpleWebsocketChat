namespace Websockets
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;
    using System.Text.RegularExpressions;


    public class WebSocketClient
    {
        private TcpClient WBClient;

        private NetworkStream NW;

        /// <summary>
        /// Max amount of bytes to send
        /// </summary>
        public const int MaxDataLenght = 65535;

        /// <summary>
        /// True if server connected to WebClient
        /// </summary>
        public bool Connected
        {
            get
            {
                return _Avible;
            }

        }
        private bool _Avible;
        public WebSocketClient(TcpClient client)
        {

            WBClient = client;
            WBClient.SendBufferSize = 1000000;
            NW = WBClient.GetStream();



            HandShake();
            _Avible = true;
        }

        /// <summary>
        /// Read messages and commands from WebClient
        /// </summary>
        /// <returns>String message from client</returns>
        public string Read()
        {
            string Message = string.Empty;

            while (!NW.DataAvailable) ;//Wait for data
            while (WBClient.Available < 3) ;

            byte[] bytes;
            _Avible = WBClient.Connected;

            //Recive data
            #region
            List<byte> data = new List<byte>();

            while (WBClient.Available != 0 && NW.CanRead)
            {
                data.Add((byte)NW.ReadByte());
            }

            bytes = data.ToArray();
            #endregion

            bool fin = (bytes[0] & 0b10000000) != 0;
            bool mask = (bytes[1] & 0b10000000) != 0; // must be true, "All messages from the client to the server have this bit set"

            int opcode = bytes[0] & 0b00001111;
            int offset = 2;
            int msglen = (byte)(bytes[1] - 128);


            if ((bytes[0] & 0b00001111) == 1) //OpCode 1
            {
                if (msglen == 126)
                {
                    msglen = BitConverter.ToUInt16(new byte[] { bytes[3], bytes[2] }, 0);
                    offset = 4;
                }
                else if (msglen == 127)
                {
                    msglen = (int)BitConverter.ToUInt64(new byte[] { bytes[9], bytes[8], bytes[7], bytes[6], bytes[5], bytes[4], bytes[3], bytes[2] }, 0);

                    offset = 10;
                }

                if (mask && msglen != 0)
                {
                    byte[] decoded = new byte[msglen];
                    byte[] masks = new byte[4] { bytes[offset], bytes[offset + 1], bytes[offset + 2], bytes[offset + 3] };
                    offset += 4;

                    for (int i = 0; i < msglen; ++i)
                        decoded[i] = (byte)(bytes[offset + i] ^ masks[i % 4]);

                    string text = Encoding.UTF8.GetString(decoded);

                    Message = text;


                }
                else
                {
                    throw new Exception("Received frame has no mask");

                }

            }
            else if ((bytes[0] & 0b00001111) == 8)//OpCode 8 (Close)
            {
                _Close();
                return string.Empty;
            }


            return Message;
        }

        /// <summary>
        /// Close server side
        /// </summary>
        void _Close()
        {
            NW.Close();
            WBClient.Close();
            _Avible = false;
        }

        /// <summary>
        /// Send close command to the WebClient and the close server 
        /// </summary>
        public void Close()
        {

            byte[] data = Utilities.ToFrame(8, "Close");//8 represent the Close frame's opcode

            NW.Write(data, 0, data.Length);

            if (Read() == "Close")
            {
                _Close();
            }
            _Avible = false;

        }

        /// <summary>
        /// Send a ping command and returns the response time
        /// </summary>
        /// <returns>Time in milliseconds</returns>
        public int Ping()
        {

            byte[] data = Utilities.ToFrame(9, "Ping");//9 represent Ping frame's opcode

            NW.Write(data, 0, data.Length);

            DateTime now = DateTime.Now;
            TimeSpan time = new TimeSpan();

            if (Read() == "Ping")
            {
                time = DateTime.Now - now;
            }


            return Convert.ToInt32(time.TotalMilliseconds);
        }

        /// <summary>
        /// Write string to the WebClient
        /// </summary>
        /// <param name="msg"></param>
        public void Write(string msg)
        {

            _Avible = WBClient.Connected;

            byte[] data = Utilities.MsgToFrame(msg);
            if (data.Length >= MaxDataLenght)
            {
                throw new Exception(string.Format("Data is too long to send. Max lenght {0} bytes", MaxDataLenght));
            }
            if (NW.CanRead)
            {

                NW.Write(data, 0, data.Length);

            }
        }

        /// <summary>
        /// Write binary frame
        /// </summary>
        /// <param name="binary"></param>
        public void WriteRaw(List<byte> binary)
        {
            byte[] handler = Utilities.ToFrame(2, binary);//2 represent the Binary frame's opcode

            NW.Write(handler, 0, handler.Length);
        }

        /// <summary>
        /// Do a handshake
        /// </summary>
        private void HandShake()
        {
            bool HSDone = false;

            while (HSDone == false)
            {
                while (!NW.DataAvailable) ;
                while (WBClient.Available < 3) ;

                byte[] bytes = new byte[WBClient.Available];
                NW.Read(bytes, 0, WBClient.Available);
                string s = Encoding.UTF8.GetString(bytes);


                if (Regex.IsMatch(s, "^GET", RegexOptions.IgnoreCase))
                {
                    //Console.WriteLine("=====Handshaking from client=====\n{0}", s);

                    // 1. Obtain the value of the "Sec-WebSocket-Key" request header without any leading or trailing whitespace
                    // 2. Concatenate it with "258EAFA5-E914-47DA-95CA-C5AB0DC85B11" (a special GUID specified by RFC 6455)
                    // 3. Compute SHA-1 and Base64 hash of the new value
                    // 4. Write the hash back as the value of "Sec-WebSocket-Accept" response header in an HTTP response
                    string swk = Regex.Match(s, "Sec-WebSocket-Key: (.*)").Groups[1].Value.Trim();
                    string swka = swk + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
                    byte[] swkaSha1 = System.Security.Cryptography.SHA1.Create().ComputeHash(Encoding.UTF8.GetBytes(swka));
                    string swkaSha1Base64 = Convert.ToBase64String(swkaSha1);



                    // HTTP/1.1 defines the sequence CR LF as the end-of-line marker
                    byte[] response = Encoding.UTF8.GetBytes("HTTP/1.1 101 Switching Protocols\r\n" + "Connection: Upgrade\r\n" + "Upgrade: websocket\r\n" + "Sec-WebSocket-Accept: " + swkaSha1Base64 + "\r\n\r\n");


                    NW.Write(response, 0, response.Length);
                }
                HSDone = true;
            }
        }

    }

    public static class Utilities
    {
        /// <summary>
        /// Accept WebClient
        /// </summary>
        /// <param name="asd"></param>
        /// <returns></returns>
        public static WebSocketClient AcceptWebsocketClient(this WebSocketServer asd)
        {
            return new WebSocketClient(asd.Server.AcceptTcpClient());
        }

        /// <summary>
        /// Convert Int16 to binary code
        /// </summary>
        /// <param name="value"></param>
        /// <returns>Binary code</returns>
        static List<byte> Int16ToBits(Int16 value)
        {
            List<byte> handler = BitConverter.GetBytes(value).ToList();

            handler.Reverse();

            if (handler.Count == 1)
            {
                handler.Insert(0, 0);
            }
            return handler;
        }
        /// <summary>
        /// Convert Int64 to binary code
        /// </summary>
        /// <param name="value"></param>
        /// <returns>Binary code</returns>
        static List<byte> Int64ToBits(Int64 value)
        {
            List<byte> handler = BitConverter.GetBytes(value).ToList();

            handler.Reverse();


            while (handler[0] == 0)
            {
                handler.RemoveAt(0);

            }

            int count = 8 - handler.Count;
            for (int a = 0; a < count; a++)
            {
                handler.Insert(0, 0);

            }
            return handler;
        }


        /// <summary>
        /// Create a message frame
        /// </summary>
        /// <param name="message"></param>
        /// <returns>A byte array what represent the frame</returns>
        public static byte[] MsgToFrame(string message)
        {
            List<byte> frame = new List<byte>();
            int maker = 0;

            int msglen = Encoding.UTF8.GetByteCount(message);

            //fin
            maker |= 0b10000000;

            //srv1 2 3
            //maker |= 0b00000000;

            //opCode
            //1            0001   
            maker |= 0b00000001;

            frame.Add((byte)maker);

            maker = 0;

            //mask
            //maker |= 0b00000000;

            if (msglen <= 125)
            {
                frame.Add((byte)msglen);
            }
            else
            {
                if (msglen <= 65535)
                {

                    frame.Add((byte)126);


                    //Add extended date lenght
                    frame.AddRange(Int16ToBits((short)msglen));

                }
                //else
                //{

                //    //Console.WriteLine(127);
                //    frame.Add((byte)127);


                //    //Add extended date lenght
                //    frame.AddRange(Int64ToBits(msglen));

                //}


            }


            //Add data
            frame.AddRange(Encoding.UTF8.GetBytes(message).ToList());

            return frame.ToArray();
        }

        /// <summary>
        /// Create a message frame
        /// </summary>
        /// <param name="opcode">Frame type</param>
        /// <param name="message"></param>
        /// <returns>A byte array what represent the frame</returns>
        public static byte[] ToFrame(byte opcode, string message)
        {
            List<byte> frame = new List<byte>();
            int maker = 0;
            int mmsglen = Encoding.UTF8.GetBytes(message).Length;
            //fin
            maker |= 0b10000000;

            //srv1 2 3
            //maker |= 0b00000000;

            //opCode 
            maker |= opcode;


            frame.Add((byte)maker);

            maker = 0;

            //mask
            //maker |= 0b00000000;


            frame.Add((byte)mmsglen);

            //Add data
            frame.AddRange(Encoding.UTF8.GetBytes(message).ToList());



            return frame.ToArray();
        }

        /// <summary>
        /// Create a costume frame
        /// </summary>
        /// <param name="opcode">Frame type</param>
        /// <param name="binary"></param>
        /// <param name="finalFrame"></param>
        /// <returns>A byte array what represent the frame</returns>
        public static byte[] ToFrame(byte opcode, List<byte> binary, bool finalFrame = true)
        {
            List<byte> frame = new List<byte>();
            int maker = 0;
            //fin
            if (finalFrame)
            {
                maker |= 0b10000000;
            }
            else
            {
                maker = 0;
            }

            //srv1 2 3
            //maker |= 0b00000000;

            //opCode 
            maker |= opcode;


            frame.Add((byte)maker);

            maker = 0;

            //mask
            //maker |= 0b00000000;

            frame.Add((byte)binary.Count);

            //Add data
            frame.AddRange(binary);

            return frame.ToArray();
        }
    }

    public class WebSocketServer
    {
        IPAddress Ip;
        int Port;

        public TcpListener Server;

        public WebSocketServer(IPAddress ip, int port)
        {
            this.Ip = ip;
            this.Port = port;


            Server = new TcpListener(ip, port);

            //Server.Start();
        }

        /// <summary>
        /// Start listening
        /// </summary>
        public void Start()
        {
            Server.Start();
        }
        /// <summary>
        /// Stop listening
        /// </summary>
        public void Stop()
        {
            Server.Stop();
        }


    }
}