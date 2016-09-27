using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ProtoBuf;

namespace ProtobufExample01.Model
{
    public class ProtoBufExample
    {
      public enum eType : byte { eError = 0, eFeedback, eBook, eFable };
 
      [ProtoContract]
      public class Header {
         [ProtoMember(1)] public eType objectType;
         [ProtoMember(2)] public readonly int serialMessageId;
 
         public object data;
         private static int _HeaderSerialId = 0;
 
         public Header(object xData, eType xObjectType, int xSerialMessageId = 0) {
            data = xData;
            serialMessageId = (xSerialMessageId == 0) ? Interlocked.Increment(ref _HeaderSerialId) : xSerialMessageId;
            objectType = xObjectType; // we could use "if typeof(T) ...", but it would be slower, harder to maintain and less legible
         } // constructor
 
         // parameterless constructor needed for Protobuf-net
         public Header() {
         } // constructor
 
      } // class
 
      [ProtoContract]
      public class ErrorMessage {
         [ProtoMember(1)]
         public string Text;
      } // class
 
      [ProtoContract]
      public class Book {
         [ProtoMember(1)] public string author;
         [ProtoMember(2, DataFormat = DataFormat.Group)] public List<Fable> stories;
         [ProtoMember(3)] public DateTime edition;
         [ProtoMember(4)] public int pages;
         [ProtoMember(5)] public double price;
         [ProtoMember(6)] public bool isEbook;
 
         public override string ToString() {
            StringBuilder s = new StringBuilder();
            s.Append("by "); s.Append(author);
            s.Append(", edition "); s.Append(edition.ToString("dd MMM yyyy"));
            s.Append(", pages "); s.Append(pages);
            s.Append(", price "); s.Append(price);
            s.Append(", isEbook "); s.Append(isEbook);
            s.AppendLine();
            if (stories != null) foreach (Fable lFable in stories) {
                  s.Append("title "); s.Append(lFable.title);
                  s.Append(", rating "); s.Append(lFable.customerRatings.Average()); // Average() is an extension method of "using System.Linq;"
                  s.AppendLine();
               }
 
            return s.ToString();
         } //
      } // class
 
      [ProtoContract]
      public class Fable {
         [ProtoMember(1)] public string title;
         [ProtoMember(2, DataFormat = DataFormat.Group)]
         public double[] customerRatings;
 
         public override string ToString() {
            return "title " + title + ", rating " + customerRatings.Average();
         } //
      } // class
 
      public static Book GetData() {
         return new Book {
            author = "Aesop",
            price = 1.99,
            isEbook = false,
            edition = new DateTime(1975, 03, 13),
            pages = 203,
            stories = new List<Fable>(new Fable[] {
                new Fable{ title = "The Fox & the Grapes", customerRatings = new double[]{ 0.7, 0.7, 0.8} },
                new Fable{ title = "The Goose that Laid the Golden Eggs", customerRatings = new double[]{ 0.6, 0.75, 0.5, 1.0} },
                new Fable{ title = "The Cat & the Mice", customerRatings = new double[]{ 0.1, 0.0, 0.3} },
                new Fable{ title = "The Mischievous Dog", customerRatings = new double[]{ 0.45, 0.5, 0.4, 0.0, 0.5} }
            })
         };
      } //
   } // class
 
   public class PendingFeedbacks {
      private readonly ConcurrentDictionary<int, ProtoBufExample.Header> _Messages = new ConcurrentDictionary<int, ProtoBufExample.Header>();
 
      public int Count { get { return _Messages.Count; } }
 
      public void Add(ProtoBufExample.Header xHeader) {
         if (xHeader == null) throw new Exception("cannot add a null header");
 
         if (!_Messages.TryAdd(xHeader.serialMessageId, xHeader)) {
            throw new Exception("there must be a programming error somewhere");
         }
      } //
 
      public void Remove(ProtoBufExample.Header xHeader) {
         ProtoBufExample.Header lHeader;
         if (!_Messages.TryRemove(xHeader.serialMessageId, out lHeader)) {
            throw new Exception("there must be a programming error somewhere");
         }
 
         switch (xHeader.objectType) {
            case ProtoBufExample.eType.eError:
               Console.WriteLine("error: " + ((ProtoBufExample.ErrorMessage)xHeader.data).Text);
               Console.WriteLine("the message that was sent out was: " + lHeader.objectType + " with serial id " + lHeader.serialMessageId);
               Console.WriteLine("please check the log files" + Environment.NewLine);
               break;
            case ProtoBufExample.eType.eFeedback:
               // all ok !
               break;
            default:
               Console.WriteLine("warning: This message type was not expected.");
               break;
         }
      } //
   } // class
 
   public static class NetworkTest {
      public static void Test() {
         NetworkListener lServer = new NetworkListener("127.0.0.1", 65432, "Server");
         NetworkClient lClient = new NetworkClient("127.0.0.1", 65432, "Client");
 
         lServer.Connect();
         lServer.OnMessageBook += new NetworkListener.dOnMessageBook(OnMessageBook);
         lServer.OnMessageFable += new NetworkListener.dOnMessageFable(OnMessageFable);
 
         lClient.Connect();
 
         ProtoBufExample.Header lHeader;
 
         // send a book across the network
         ProtoBufExample.Book lBook = ProtoBufExample.GetData();
         lHeader = new ProtoBufExample.Header(lBook, ProtoBufExample.eType.eBook);
         lClient.Send(lHeader);
 
         System.Threading.Thread.Sleep(1000);  // remove this to see the asynchonous processing (the output will look terrible)
 
         // send a fable across the network
         lHeader = new ProtoBufExample.Header(lBook.stories[1], ProtoBufExample.eType.eFable);
         lClient.Send(lHeader);
 
         System.Threading.Thread.Sleep(1000);
 
         lClient.Disconnect();
         lServer.Disconnect();
 
         Console.ReadLine();
      }
 
      // demo: synchronous processing
      static void OnMessageFable(TcpClient xSender, ProtoBufExample.Header xHeader, ProtoBufExample.Fable xFable) {
         Console.WriteLine(Environment.NewLine + "received a fable: ");
         Console.WriteLine(xFable.ToString());
 
         // demo: we tell the server that something went wrong
         ProtoBufExample.ErrorMessage lErrorMessage = new ProtoBufExample.ErrorMessage() { Text = "The fable was rejected. It is far too short." };
         ProtoBufExample.Header lErrorHeader = new ProtoBufExample.Header(lErrorMessage, ProtoBufExample.eType.eError, xHeader.serialMessageId);
         NetworkListener.Send(xSender, lErrorHeader);
      } //
 
      // demo: asynchronous processing
      static void OnMessageBook(TcpClient xSender, ProtoBufExample.Header xHeader, ProtoBufExample.Book xBook) {
         Task.Factory.StartNew(() => {
            Console.WriteLine(Environment.NewLine + "received a book: ");
            Console.WriteLine(xBook.ToString());
 
            // send a feedback without any body to signal all was ok
            ProtoBufExample.Header lFeedback = new ProtoBufExample.Header(null, ProtoBufExample.eType.eFeedback, xHeader.serialMessageId);
            NetworkListener.Send(xSender, lFeedback);
            return;
         });
 
         Console.WriteLine("Book event was raised");
      } //
 
   } // class
 
   public class NetworkListener {
 
      private bool _ExitLoop = true;
      private TcpListener _Listener;
 
      public delegate void dOnMessageBook(TcpClient xSender, ProtoBufExample.Header xHeader, ProtoBufExample.Book xBook);
      public event dOnMessageBook OnMessageBook;
 
      public delegate void dOnMessageFable(TcpClient xSender, ProtoBufExample.Header xHeader, ProtoBufExample.Fable xFable);
      public event dOnMessageFable OnMessageFable;
 
      private List<TcpClient> _Clients = new List<TcpClient>();
 
      public int Port { get; private set; }
      public string IpAddress { get; private set; }
      public string ThreadName { get; private set; }
 
      public NetworkListener(string xIpAddress, int xPort, string xThreadName) {
         Port = xPort;
         IpAddress = xIpAddress;
         ThreadName = xThreadName;
      } //
 
      public bool Connect() {
         if (!_ExitLoop) {
            Console.WriteLine("Listener running already");
            return false;
         }
         _ExitLoop = false;
 
         try {
            _Listener = new TcpListener(IPAddress.Parse(IpAddress), Port);
            _Listener.Start();
 
            Thread lThread = new Thread(new ThreadStart(LoopWaitingForClientsToConnect));
            lThread.IsBackground = true;
            lThread.Name = ThreadName + "WaitingForClients";
            lThread.Start();
 
            return true;
         }
         catch (Exception ex) { Console.WriteLine(ex.Message); }
         return false;
      } //
 
      public void Disconnect() {
         _ExitLoop = true;
         lock (_Clients) {
            foreach (TcpClient lClient in _Clients) lClient.Close();
            _Clients.Clear();
         }
      } //        
 
      private void LoopWaitingForClientsToConnect() {
         try {
            while (!_ExitLoop) {
               Console.WriteLine("waiting for a client");
               TcpClient lClient = _Listener.AcceptTcpClient();
               string lClientIpAddress = lClient.Client.LocalEndPoint.ToString();
               Console.WriteLine("new client connecting: " + lClientIpAddress);
               if (_ExitLoop) break;
               lock (_Clients) _Clients.Add(lClient);
 
               Thread lThread = new Thread(new ParameterizedThreadStart(LoopRead));
               lThread.IsBackground = true;
               lThread.Name = ThreadName + "CommunicatingWithClient";
               lThread.Start(lClient);
            }
         }
         catch (Exception ex) { Console.WriteLine(ex.Message); }
         finally {
            _ExitLoop = true;
            if (_Listener != null) _Listener.Stop();
         }
      } // 
 
      private void LoopRead(object xClient) {
         TcpClient lClient = xClient as TcpClient;
         NetworkStream lNetworkStream = lClient.GetStream();
 
         while (!_ExitLoop) {
            try {
               ProtoBufExample.Header lHeader = ProtoBuf.Serializer.DeserializeWithLengthPrefix<ProtoBufExample.Header>(lNetworkStream, ProtoBuf.PrefixStyle.Fixed32);
               if (lHeader == null) break; // happens during shutdown process
               switch (lHeader.objectType) {
 
                  case ProtoBufExample.eType.eBook:
                     ProtoBufExample.Book lBook = ProtoBuf.Serializer.DeserializeWithLengthPrefix<ProtoBufExample.Book>(lNetworkStream, ProtoBuf.PrefixStyle.Fixed32);
                     if (lBook == null) break;
                     lHeader.data = lBook; // not necessary, but nicer                            
 
                     dOnMessageBook lEventBook = OnMessageBook;
                     if (lEventBook == null) continue;
                     lEventBook(lClient, lHeader, lBook);
                     break;
 
                  case ProtoBufExample.eType.eFable:
                     ProtoBufExample.Fable lFable = ProtoBuf.Serializer.DeserializeWithLengthPrefix<ProtoBufExample.Fable>(lNetworkStream, ProtoBuf.PrefixStyle.Fixed32);
                     if (lFable == null) break;
                     lHeader.data = lFable; // not necessary, but nicer                            
 
                     dOnMessageFable lEventFable = OnMessageFable;
                     if (lEventFable == null) continue;
                     lEventFable(lClient, lHeader, lFable);
                     break;
 
                  default:
                     Console.WriteLine("Mayday, mayday, we are in big trouble.");
                     break;
               }
            }
            catch (System.IO.IOException) {
               if (_ExitLoop) Console.WriteLine("user requested client shutdown");
               else Console.WriteLine("disconnected");
            }
            catch (Exception ex) { Console.WriteLine(ex.Message); }
         }
         Console.WriteLine("server: listener is shutting down");
      } //
 
      public static void Send(TcpClient xClient, ProtoBufExample.Header xHeader) {
         if (xHeader == null) return;
         if (xClient == null) return;
 
         lock (xClient) {
            try {
               NetworkStream lNetworkStream = xClient.GetStream();
 
               // send header (most likely a simple feedback)
               ProtoBuf.Serializer.SerializeWithLengthPrefix<ProtoBufExample.Header>(lNetworkStream, xHeader, ProtoBuf.PrefixStyle.Fixed32);
 
               // send errors
               if (xHeader.objectType != ProtoBufExample.eType.eError) return;
               ProtoBuf.Serializer.SerializeWithLengthPrefix<ProtoBufExample.ErrorMessage>(lNetworkStream, (ProtoBufExample.ErrorMessage)xHeader.data, ProtoBuf.PrefixStyle.Fixed32);
            }
            catch (Exception ex) { Console.WriteLine(ex.Message); }
         }
 
      } //
 
   } // class
 
   public class NetworkClient {
      public int Port { get; private set; }
      public string IpAddress { get; private set; }
      public string ThreadName { get; private set; }
      private NetworkStream _NetworkStream = null;
      private TcpClient _Client = null;
      private bool _ExitLoop = true;
      private BlockingCollection<ProtoBufExample.Header> _Queue = new BlockingCollection<ProtoBufExample.Header>();
      private readonly PendingFeedbacks _PendingFeedbacks = new PendingFeedbacks();
 
      public NetworkClient(string xIpAddress, int xPort, string xThreadName) {
         Port = xPort;
         IpAddress = xIpAddress;
         ThreadName = xThreadName;
      } //
 
      public void Connect() {
         if (!_ExitLoop) return; // running already
         _ExitLoop = false;
 
         _Client = new TcpClient();
         _Client.Connect(IpAddress, Port);
         _NetworkStream = _Client.GetStream();
 
         Thread lLoopWrite = new Thread(new ThreadStart(LoopWrite));
         lLoopWrite.IsBackground = true;
         lLoopWrite.Name = ThreadName + "Write";
         lLoopWrite.Start();
 
         Thread lLoopRead = new Thread(new ThreadStart(LoopRead));
         lLoopRead.IsBackground = true;
         lLoopRead.Name = ThreadName + "Read";
         lLoopRead.Start();
      } //
 
      public void Disconnect() {
         _ExitLoop = true;
         _Queue.Add(null);
         if (_Client != null) _Client.Close();
         //if (_NetworkStream != null) _NetworkStream.Close();
      } //
 
      public void Send(ProtoBufExample.Header xHeader) {
         if (xHeader == null) return;
         _PendingFeedbacks.Add(xHeader);
         _Queue.Add(xHeader);
      } //
 
 
      private void LoopWrite() {
         while (!_ExitLoop) {
            try {
               ProtoBufExample.Header lHeader = _Queue.Take();
               if (lHeader == null) break;
 
               // send header
               ProtoBuf.Serializer.SerializeWithLengthPrefix<ProtoBufExample.Header>(_NetworkStream, lHeader, ProtoBuf.PrefixStyle.Fixed32);
 
               // send data
               switch (lHeader.objectType) {
                  case ProtoBufExample.eType.eBook:
                     ProtoBuf.Serializer.SerializeWithLengthPrefix<ProtoBufExample.Book>(_NetworkStream, (ProtoBufExample.Book)lHeader.data, ProtoBuf.PrefixStyle.Fixed32);
                     break;
                  case ProtoBufExample.eType.eFable:
                     ProtoBuf.Serializer.SerializeWithLengthPrefix<ProtoBufExample.Fable>(_NetworkStream, (ProtoBufExample.Fable)lHeader.data, ProtoBuf.PrefixStyle.Fixed32);
                     break;
                  default:
                     break;
               }
            }
            catch (System.IO.IOException) {
               if (_ExitLoop) Console.WriteLine("user requested client shutdown.");
               else Console.WriteLine("disconnected");
            }
            catch (Exception ex) { Console.WriteLine(ex.Message); }
         }
         _ExitLoop = true;
         Console.WriteLine("client: writer is shutting down");
      } //
 
 
      private void LoopRead() {
         while (!_ExitLoop) {
            try {
               ProtoBufExample.Header lHeader = ProtoBuf.Serializer.DeserializeWithLengthPrefix<ProtoBufExample.Header>(_NetworkStream, ProtoBuf.PrefixStyle.Fixed32);
               if (lHeader == null) break;
               if (lHeader.objectType == ProtoBufExample.eType.eError) {
                  ProtoBufExample.ErrorMessage lErrorMessage = ProtoBuf.Serializer.DeserializeWithLengthPrefix<ProtoBufExample.ErrorMessage>(_NetworkStream, ProtoBuf.PrefixStyle.Fixed32);
                  lHeader.data = lErrorMessage;
               }
               _PendingFeedbacks.Remove(lHeader);
            }
            catch (System.IO.IOException) {
               if (_ExitLoop) Console.WriteLine("user requested client shutdown");
               else Console.WriteLine("disconnected");
            }
            catch (Exception ex) { Console.WriteLine(ex.Message); }
         }
         Console.WriteLine("client: reader is shutting down");
      } //
}
}