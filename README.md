# DotWebSocket
라이브러리 쓰기 싫어서 만든 웹소켓            

# Example
## Server
```cs
public class TestWS: DotSocketBehavior {
  public override void OnOpen() {
    Console.WriteLine("열림!");
  }

  public override void OnConnect(ConnectedClient wshc) {
    Console.WriteLine("누가 접속함! " + wshc.ClientPort);
  }

  public override void OnMessage(ConnectedClient wshc) {
    Console.WriteLine("메시지 받음! " + wshc.StringData+" "+wshc.RawData);

    if (!wshc.IsBinary && wshc.StringData == "hello")
      SendAll("hi");

    if (!wshc.IsBinary && wshc.StringData == "bye")
      wshc.Send("hi");
  }

  public override void OnDisconnect(ConnectedClient wshc) {
    Console.WriteLine("누군가 나감! " + wshc.ClientIP);
  }
}
```
```cs
var s = new DotSocketServer(1234);
s.AddBehavior < TestWS > ("/");
s.Start();
```

## Client
```cs
var s = new DotSocketClient("ws://sans.tv/");
s.Connect();
s.OnConnect += () => {
  Console.WriteLine("연결됨!");
};
s.OnMessage += (binary, data) => {
  Console.WriteLine("메시지 받음!! " + data);

  if (!binary) {
    var text = Encoding.UTF8.GetString(data);
    if (text == "hello")
      s.Send("Bye!");
  }
};
s.OnServerClose += () => {
  Console.WriteLine("닫힘!");
};
```

