

using System.Collections.Generic;
using System.Text;

namespace DotWebSocket.Server
{
    public class DotSocketBehavior
    {
        public DotSocketServer Wss;
        public List<ConnectedClient> ConnectClients => Wss._clients;
        public string Path => _path;
        private string _path;

        public int Port
        {
            get
            {
                if (Wss == null) return 80;
                return Wss.Port;
            }
        }

        public void Init(DotSocketServer wss, string path)
        {
            Wss = wss;
            _path = path;
        }
        
        public void SendAll(string message)
        {
            foreach (var t in Wss._clients)
                t.Send(Encoding.UTF8.GetBytes(message), Opcode.Text, true);
        }

        public void SendAll(byte[] binary)
        {
            foreach (var t in Wss._clients)
                t.Send(binary, Opcode.Binary, true);
        }
        
        public virtual void OnOpen(){}

        public virtual void OnConnect(ConnectedClient wshc){}
        
        public virtual void OnMessage(ConnectedClient wshc){}
        
        public virtual void OnDisconnect(ConnectedClient wshc){}
        
    }
}