using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Policy;
using System.Text;

namespace DotWebSocket.Client
{
    public class DotSocketClient
    {
        private Uri _uri;
        private Stream _stream;
        private TcpClient _client;
        private string _mustUseThisKey;
        private readonly byte[] _dataBuffer = new byte[16];
        private byte[] _cachedBuffer;

        private bool _isClose;
        public bool IsClose => _isClose;
        
        private DotSocketState _state = DotSocketState.None;
        public DotSocketState State => _state;

        public readonly Dictionary<string, string> Headers = new Dictionary<string, string>();
        
        public delegate void MessageEvent(bool IsBinary, byte[] RawData);
        public event MessageEvent OnMessage;
        
        public delegate void ServerClosedEvent();
        public event ServerClosedEvent OnServerClose;
        
        public delegate void ConnectEvent();
        public event ConnectEvent OnConnect;
        
        
        private string GenerateRandomByteBase64()
        {
            var bytes16 = new byte[16];
            new Random().NextBytes(bytes16);

            return Convert.ToBase64String(bytes16);
        }

        public DotSocketClient(string URL)
        {
            _uri = new Uri(URL);
        }

        public void Close()
        {
            _state = DotSocketState.Close;
            _isClose = false;
            _client.Close();
            _client.Dispose();
        }
        
        public void Connect()
        {
            try
            {
                _client = new TcpClient();
                _client.Connect(_uri.Host, _uri.Port);
                _isClose = true;
                _stream = _client.GetStream();
                _state = DotSocketState.Connecting;
                
                HandshakeRequest();

                _stream.BeginRead(_dataBuffer, 0, _dataBuffer.Length, OnDataReceived, null);
            }
            catch
            {
                _isClose = false;
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
        

        private void Send(byte[] binary, Opcode opcode,bool fin)
        {
            if (_isClose )
                throw new Exception("You're disconnected from the server.");
            if (_client == null) return;
            if (!_client.Connected) return;
            
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


            var randomMaskKey = new byte[4];
            new Random().NextBytes(randomMaskKey);

            byte[] sendByte;
            if (binary.Length < 126)
            {
                sendByte = new byte[binary.Length + 2 +4];
                
                firstInfor.CopyTo(sendByte,0);
                sendByte[1] = (byte)(binary.Length | 0b10000000);

                for (var i = 0; i < randomMaskKey.Length; i++)
                    sendByte[i + 2] = randomMaskKey[i];
                
                for (var i = 0; i < binary.Length; i++)
                    sendByte[i + 6] = (byte)((binary[i] ^ randomMaskKey[i % 4]));
                //binary.CopyTo(sendByte, 2);
            }
            else if(binary.Length == 126)
            {
                sendByte = new byte[binary.Length + 4 +4];
                firstInfor.CopyTo(sendByte,0);
                sendByte[1] = 126 | 0b10000000;
                
                var lengths = BitConverter.GetBytes((ushort)binary.Length);
                sendByte[2] = lengths[0];
                sendByte[3] = lengths[1];
                
                for (var i = 0; i < randomMaskKey.Length; i++)
                    sendByte[i + 4] = randomMaskKey[i];
                
                for (var i = 0; i < binary.Length; i++)
                    sendByte[i + 8] = (byte)((binary[i] ^ randomMaskKey[i % 4]));
            }
            else
            {
                sendByte = new byte[binary.Length + 10 + 4];
                firstInfor.CopyTo(sendByte,0);
                sendByte[1] = 127 | 0b10000000;
                
                var lengths = BitConverter.GetBytes((ulong)binary.Length);
                sendByte[2] = lengths[0];
                sendByte[3] = lengths[1];
                sendByte[4] = lengths[2];
                sendByte[5] = lengths[3];
                sendByte[6] = lengths[4];
                sendByte[7] = lengths[5];
                sendByte[8] = lengths[6];
                sendByte[9] = lengths[7];
                
                for (var i = 0; i < randomMaskKey.Length; i++)
                    sendByte[i + 10] = randomMaskKey[i];
                
                for (var i = 0; i < binary.Length; i++)
                    sendByte[i + 14] = (byte)((binary[i] ^ randomMaskKey[i % 4]));
            }
            

            _stream.Write(sendByte,0,sendByte.Length);
        }

        private void OnDataReceived(IAsyncResult ar)
        {
            if (_isClose) return;
            if (!_client.Connected) return;

            try
            {
                var size = _stream.EndRead(ar);
                if (Encoding.UTF8.GetString(_dataBuffer).StartsWith("HTTP/1.1") && size >= 8)
                {
                    var bytes = new byte[1024];
                    for (var n = 0; n < _dataBuffer.Length; n++)
                        bytes[n] = _dataBuffer[n];
                    _stream.Read(bytes, 16, bytes.Length - 16);



                    CheckHandshake(Encoding.UTF8.GetString(bytes));
                }
                else
                {
                    ProcessData();
                }
            }
            catch (Exception e)
            {
                if (e is SocketException)
                {
                    Close();
                }
            }


            _stream.BeginRead(_dataBuffer, 0, _dataBuffer.Length, OnDataReceived, null);

        }

        private void ProcessData()
        {
            var fin = (_dataBuffer[0] & 0b10000000) != 0;
            var mask = (_dataBuffer[1] & 0b10000000) != 0;

            if (mask) return;

            var opcode = _dataBuffer[0] & 0b00001111;
            var payloadLength = (ulong)(_dataBuffer[1]);
            var offset = 2;

            if (opcode == (int)Opcode.Ping)
            {
                Send(new byte[]{}, Opcode.Pong, true);
                return;
            }
            
            if (opcode == (int)Opcode.Close)
            {
                OnServerClose?.Invoke();
                return;
            }

            // 126이면 다음 2 바이트
            if (payloadLength == 126)
            {
                payloadLength = BitConverter.ToUInt16(new[] { _dataBuffer[3], _dataBuffer[2] }, 0);
                offset = 4;
            }
            // 127이면 다음 8바이트 
            else if (payloadLength == 127)
            {

                payloadLength =
                    BitConverter.ToUInt64(
                        new[]
                        {
                            _dataBuffer[9], _dataBuffer[8], _dataBuffer[7], _dataBuffer[6],
                            _dataBuffer[5], _dataBuffer[4], _dataBuffer[3], _dataBuffer[2]
                        }, 0);
                offset = 10;
            }
            
            var payloadData = new byte[payloadLength];
            var diff = (16 - offset);
            for (var n = 0; n < diff; n++)
                payloadData[n] = _dataBuffer[offset+n];
            if ((uint)diff < payloadLength)
            {
                for (var n = 0; n < diff; n++)
                    payloadData[n] = _dataBuffer[offset+n];

                _stream.Read(payloadData, diff, payloadData.Length-diff);
            }
            
            
            if (fin)
            {
                if(_cachedBuffer != null)
                    payloadData = _cachedBuffer;
                _cachedBuffer = null;
            }
            else
            {
                _cachedBuffer = _cachedBuffer == null ? payloadData : _cachedBuffer.Concat(payloadData).ToArray();
            }
            
            
            if (opcode == (int)Opcode.Binary)
            {
                OnMessage?.Invoke(true, payloadData);
            }

            if (opcode == (int)Opcode.Text)
            {
                OnMessage?.Invoke(false, payloadData);
            }

            
            //Console.WriteLine(Encoding.UTF8.GetString(payloadData));
        }
        
        

        private void CheckHandshake(string message)
        {
            try
            {
                var code = Utils.FastSplit(message, "HTTP/1.1", "Switching Protocols").Trim();

                if (code != "101")
                    throw new Exception("Something went wrong on the server: " + code);


                Headers.Clear();

                var splitedMessage = message.Trim().Split('\n');
                for (var i = 1; i < splitedMessage.Length; i++)
                {
                    var index = splitedMessage[i].IndexOf(":", StringComparison.Ordinal);
                    if (index == -1) continue;
                    Headers[splitedMessage[i].Substring(0, index).Trim().ToLower()] =
                        splitedMessage[i].Substring(index + 1).Trim();
                }

                if (Headers["sec-websocket-accept"] != _mustUseThisKey)
                    throw new Exception("Sec-WebSocket-Accept sent by the server is not valid.");

                OnConnect?.Invoke();
                _state = DotSocketState.Open;
            }
            catch
            {
                Close();
                _state = DotSocketState.Close;
                throw new Exception("It looks like the server-side gave an incorrect response.");
            }
        }

        private void HandshakeRequest()
        {
            var randomBase64 = GenerateRandomByteBase64();
            var request = Encoding.UTF8.GetBytes(
                "GET " + _uri.AbsolutePath + " HTTP/1.1s\r\n" +
                "Host: " + _uri.Host + "\r\n"+
                "Connection: Upgrade\r\n" +
                "Upgrade: websocket\r\n" +
                "Sec-WebSocket-Key: " +randomBase64  + "\r\n\r\n");
            
            var withMagicKey = randomBase64 + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
            var magicKeySha1 = System.Security.Cryptography.SHA1.Create()
                .ComputeHash(Encoding.UTF8.GetBytes(withMagicKey));
            _mustUseThisKey = Convert.ToBase64String(magicKeySha1);
            
            _stream.Write(request, 0 ,request.Length);
            
        }
        
        
    }
}