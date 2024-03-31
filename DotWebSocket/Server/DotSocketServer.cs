using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Timers;

namespace DotWebSocket.Server
{ 
    public class DotSocketServer
    {
        private TcpListener _tcpListener;
        internal List<ConnectedClient> _clients = new List<ConnectedClient>();
        private Dictionary<string, DotSocketBehavior> _behaviors = new Dictionary<string, DotSocketBehavior>();
        private int _port;
        private bool _close;

        private const int HEARTBEAT_RATE = 30;
        
        public bool IsClose => _close;
        public int Port => _port;

        private Timer _timer;
        
        

        public DotSocketServer(int port = 80)
        {
            _port = port;

            _timer = Utils.SetTimer(PingEveryone, HEARTBEAT_RATE*1000);
        }

        private void PingEveryone()
        {
            foreach (var client in _clients)
            {

                if (!client._ping)
                {
                    client.Client.Close();
                    client.Client.Dispose();
                    _clients.Remove(client);    
                    GetBehavior(client._path)?.OnDisconnect(client);
                    continue;
                }
                
                client._ping = false;
                client.Send(new byte[] { }, Opcode.Ping, true);
            }
        }

        public void Start()
        {
            _tcpListener = new TcpListener(IPAddress.Any, _port);
            _tcpListener.Start();

            _tcpListener.BeginAcceptTcpClient(OnTcpClientAccept, null);
            
            foreach (var v in _behaviors.Values)
                v.OnOpen();
        }

        public void Stop()
        {
            _close = true;
            if (_tcpListener != null)
            {
                foreach (var client in _clients)
                {
                    client.Send(new byte[]{}, Opcode.Close, true);
                    client._state = DotSocketState.Close;
                    client.Client.Close();
                }

                _clients.Clear();
                _tcpListener.Server.Close(0);
                Utils.StopTimer(_timer);
                _tcpListener.Stop();
                _tcpListener = null;
            }
            
        }

        public T AddBehavior<T>(string path = "/") where T : DotSocketBehavior, new()
        {
            var wsb = new T() as DotSocketBehavior;
            
            if(_behaviors.TryGetValue(path, out var v))
                throw new Exception("The same path already exists.");
            
            if (path.Contains("?") || path.Contains("&"))
                throw new Exception("Query string is not available.");
            
            wsb.Init(this, path);
            _behaviors[path] = wsb;
            return (T)wsb;
        }


        public void RemoveBehavior(string path)
        {
            if (_behaviors.TryGetValue(path, out var v))
            {
                _behaviors.Remove(path);
            }
        }
 
        public DotSocketBehavior GetBehavior(string path)
        {
            if (_behaviors.TryGetValue(path, out var v))
                return v;
            return null;
        }
        
        
        private void OnTcpClientAccept(IAsyncResult ar)
        {
            var client = _tcpListener.EndAcceptTcpClient(ar);
            var wshc = new ConnectedClient(client, this);
            _clients.Add(wshc);

            _tcpListener.BeginAcceptTcpClient(OnTcpClientAccept, null);
        }
        
        

        internal bool DoHandshake(ConnectedClient wshc, byte[] dataBuffer)
        {
            var message = Encoding.UTF8.GetString(dataBuffer);
            var path = Utils.FastSplit(message, "GET", "HTTP");
            var client = wshc.Client;
            var stream = client.GetStream();
            
            wshc.Headers.Clear();
   
                var splitedMessage = message.Trim().Split('\n');
                for (var i = 1; i < splitedMessage.Length; i++)
                {
                    var index = splitedMessage[i].IndexOf(":", StringComparison.Ordinal);
                    if(index == -1) continue;
                    wshc.Headers[splitedMessage[i].Substring(0, index).Trim().ToLower()] =
                        splitedMessage[i].Substring(index + 1).Trim();
                }
     

            wshc._path = path;

            if (_behaviors.Count > 0)
            {
                if (GetBehavior(path) == null)
                {
                    
                    client.Close();
                    client.Dispose();
                    _clients.Remove(wshc);
                    return true;
                }
            }

            var secretWebSocketKey = wshc.Headers["sec-websocket-key"];
            var withMagicKey = secretWebSocketKey + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
            var magicKeySha1 = System.Security.Cryptography.SHA1.Create()
                .ComputeHash(Encoding.UTF8.GetBytes(withMagicKey));
            var andBase64 = Convert.ToBase64String(magicKeySha1);

            var response = Encoding.UTF8.GetBytes(
                "HTTP/1.1 101 Switching Protocols\r\n" +
                "Connection: Upgrade\r\n" +
                "Upgrade: websocket\r\n" +
                "Sec-WebSocket-Accept: " + andBase64 + "\r\n\r\n");

            stream.Write(response, 0, response.Length);
            
            GetBehavior(path)?.OnConnect(wshc);

            return false;
        }

        internal bool ProcessData(ConnectedClient wshc)
        {
            var client = wshc.Client;
            var stream = client.GetStream();

            var fin = (wshc.dataBuffer[0] & 0b10000000) != 0;
            var mask = (wshc.dataBuffer[1] & 0b10000000) != 0;
            if (!mask) return false;
            
            var opcode = wshc.dataBuffer[0] & 0b00001111;
            var payloadLength = (ulong)(wshc.dataBuffer[1] - 128);

            if (opcode == (int)Opcode.Close)
            {
                client.Close();
                client.Dispose();
                _clients.Remove(wshc);
                GetBehavior(wshc._path)?.OnDisconnect(wshc);
                return true;
            }

            if (opcode == (int)Opcode.Pong)
            {
                wshc._ping = true;
                return false;
            }
            

            // 126이면 다음 2 바이트
            if (payloadLength == 126)
            {
                payloadLength = BitConverter.ToUInt16(new[] { wshc.dataBuffer[3], wshc.dataBuffer[2] }, 0);
            }
            // 127이면 다음 8바이트 
            else if (payloadLength == 127)
            {
                var bytes = new byte[8];
                bytes[0] = wshc.dataBuffer[2];
                bytes[1] = wshc.dataBuffer[3];
                stream.Read(bytes, 2, bytes.Length -2);
                
                payloadLength =
                    BitConverter.ToUInt64(
                        new[]
                        {
                            bytes[7], bytes[6], bytes[5], bytes[4],
                            bytes[3], bytes[2], bytes[1], bytes[0]
                        }, 0);
            }
            
            
            var masks = new byte[4];
            if (payloadLength < 126)
            {
                masks[0] = wshc.dataBuffer[2];
                masks[1] = wshc.dataBuffer[3];
                stream.Read(masks, 2, masks.Length-2);
                
            } else
            {
                stream.Read(masks, 0, masks.Length);
            }
            
            var decoded = new byte[payloadLength];
            stream.Read(decoded, 0, decoded.Length);
            
            //Console.WriteLine("랜덤 바이트: "+masks[0]+" "+masks[1]+" "+masks[2]+" "+masks[3]);
            
            for (uint i = 0; i < payloadLength; ++i)
                decoded[i] = (byte)(decoded[i] ^ masks[i % 4]);

            if (fin)
            {
                if(wshc.CachedBuffer != null)
                    decoded = wshc.CachedBuffer;
                wshc.CachedBuffer = null;
            }
            else
            {
                wshc.CachedBuffer = wshc.CachedBuffer == null ? decoded : wshc.CachedBuffer.Concat(decoded).ToArray();
            }

            if (opcode == (int)Opcode.Binary)
            {
                wshc._stringData = null;
                wshc._rawData = decoded;
                wshc._isBinary = true;
                
                GetBehavior(wshc._path)?.OnMessage(wshc);
            }

            if (opcode == (int)Opcode.Text)
            {
                
                wshc._stringData = Encoding.UTF8.GetString(decoded);
                wshc._rawData = null;
                wshc._isBinary = false;
                
                GetBehavior(wshc._path)?.OnMessage(wshc);
            }

            return false;


        }
        
    }
}