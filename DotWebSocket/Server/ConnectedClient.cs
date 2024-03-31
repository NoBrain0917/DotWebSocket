using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DotWebSocket.Server
{
    public class ConnectedClient
    {
        private TcpClient _tcpClient;
        public TcpClient Client => _tcpClient;
        
        private DotSocketServer _baseServer;
        
        private string _clientIP;
        public string ClientIP => _clientIP;
        
        public int _clientPort;
        public int ClientPort => _clientPort;
        
        public readonly Dictionary<string, string> Headers = new Dictionary<string, string>();
        
        internal string _stringData;
        public string StringData => _stringData;
        
        internal byte[] _rawData;
        public byte[] RawData => _rawData;

        internal bool _isBinary;
        public bool IsBinary => _isBinary;

        internal string _path;
        public string Path => _path;
        
        public readonly byte[] dataBuffer = new byte[4];
        internal byte[] CachedBuffer;

        internal DotSocketState _state = DotSocketState.None;
        public DotSocketState State => _state;

        internal bool _ping = true;

        public ConnectedClient(TcpClient client, DotSocketServer baseBaseServer)
        {
            _tcpClient = client;
            _baseServer = baseBaseServer;
            
            var remoteEnd = (IPEndPoint) _tcpClient.Client.RemoteEndPoint;
            _clientIP = remoteEnd.Address.ToString();
            _clientPort = remoteEnd.Port;

            _state = DotSocketState.Connecting;
            _ping = true;
            
            client.GetStream().BeginRead(dataBuffer, 0, dataBuffer.Length, OnDataReceived, null);
        }
        
        private void OnDataReceived(IAsyncResult ar)
        {
            if (_baseServer.IsClose) return;
            
            var client = _tcpClient;
            if (!client.Connected) return;
            
            var stream = client.GetStream();

            try
            {
                var size = stream.EndRead(ar);

                var checkHandshakeBytes = new byte[] { dataBuffer[0], dataBuffer[1], dataBuffer[2] };

                var isClosed = false;
                if (Encoding.UTF8.GetString(checkHandshakeBytes) == "GET" && size >= 3)
                {
                    var bytes = new byte[1024];
                    bytes[0] = dataBuffer[0];
                    bytes[1] = dataBuffer[1];
                    bytes[2] = dataBuffer[2];
                    stream.Read(bytes, 3, bytes.Length - 3);
                    _baseServer.DoHandshake(this, bytes);

                    _state = DotSocketState.Open;
                }
                else
                    _baseServer.ProcessData(this);


                if (!isClosed)
                    stream.BeginRead(dataBuffer, 0, dataBuffer.Length, OnDataReceived, null);
                else
                    _state = DotSocketState.Close;
            }
            catch (Exception e)
            {
                //if (e is SocketException)
                //{
                
                    client.Close();
                    client.Dispose();
                    if (_state == DotSocketState.Open)
                        _baseServer.GetBehavior(_path)?.OnDisconnect(this);
                    _baseServer._clients.Remove(this);
                //}

              
            }

        }

        public void Send(byte[] binary)
        {
            Send(binary, Opcode.Binary, true);
        }
        
        public void SendSeperate(params byte[][] binarys)
        {
            for (var n=0;n<binarys.Length;n++)
            {
                var value = binarys[n];
                var isEnd = n == binarys.Length - 1;
                
                Send(value, isEnd? Opcode.Binary: Opcode.Continue, isEnd);
            }
        }
        
        public void SendSeperate(params string[] messages)
        {
            for (var n=0;n<messages.Length;n++)
            {
                var value = messages[n];
                var isEnd = n == messages.Length - 1;
                
                Send(Encoding.UTF8.GetBytes(value), isEnd? Opcode.Text: Opcode.Continue, isEnd);
            }
        }   
        
        
        public void Send(string message)
        {
            Send(Encoding.UTF8.GetBytes(message), Opcode.Text, true);
        }
        
        public void Send(byte[] binary, Opcode opcode, bool fin)
        {
            if (_tcpClient == null) return;
            if (!_tcpClient.Connected) return;
            
            var firstInfor = new BitArray(new[]
            {
                opcode == Opcode.Text || opcode == Opcode.Ping,
                opcode == Opcode.Binary || opcode == Opcode.Pong,
                false,
                opcode == Opcode.Close || opcode == Opcode.Ping || opcode == Opcode.Pong,
                false,
                false,
                false,
                fin
            });


            byte[] sendByte;
            if (binary.Length < 126)
            {
                sendByte = new byte[binary.Length + 2];
                firstInfor.CopyTo(sendByte,0);
                sendByte[1] = (byte)binary.Length;
                binary.CopyTo(sendByte, 2);
            }
            else if(binary.Length == 126)
            {
                sendByte = new byte[binary.Length + 4];
                firstInfor.CopyTo(sendByte,0);
                sendByte[1] = 126;
                
                var lengths = BitConverter.GetBytes((ushort)binary.Length);
                sendByte[2] = lengths[0];
                sendByte[3] = lengths[1];
                
                binary.CopyTo(sendByte, 4);
            }
            else
            {
                sendByte = new byte[binary.Length + 10];
                firstInfor.CopyTo(sendByte,0);
                sendByte[1] = 127;
                
                var lengths = BitConverter.GetBytes((ulong)binary.Length);
                sendByte[2] = lengths[0];
                sendByte[3] = lengths[1];
                sendByte[4] = lengths[2];
                sendByte[5] = lengths[3];
                sendByte[6] = lengths[4];
                sendByte[7] = lengths[5];
                sendByte[8] = lengths[6];
                sendByte[9] = lengths[7];
                
                binary.CopyTo(sendByte, 4);
            }
            

            _tcpClient.GetStream().Write(sendByte,0,sendByte.Length);
        }
    }
}