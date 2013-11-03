using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using System.Threading;
using System.Net.Sockets;
using System.Net.Security;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Security.Cryptography.X509Certificates;

namespace LeagueRTMPSSharp
{
	public class RTMPSClient
	{
		public  bool IsConnected { get; private set; }

		public  bool IsReconnecting { get; private set; }

		public string Server { get; private set; }

		public int Port { get; private set; }

		public string App { get; private set; }

		public string SwfUrl { get; private set; }

		public string PageUrl { get; private set; }

		private static Thread _readerThread = null;
		protected int _invokeID = 2;
		protected String DSId = null;
		protected Random rand = new Random ();
		protected SslStream stream = null;
		protected AMF3Encoder aec = new AMF3Encoder ();
		protected AMF3Decoder adc = new AMF3Decoder ();
		private TcpClient _client = null;
		private ConcurrentDictionary<int, TypedObject> results = new ConcurrentDictionary<int, TypedObject> ();
		protected ConcurrentDictionary<int, TaskCompletionSource<TypedObject>> pendingInvokes = new ConcurrentDictionary<int, TaskCompletionSource<TypedObject>> ();

		public void SetConnectionInfo (string server, int port, string app, string swfUrl, string pageUrl)
		{
			this.Server = server;
			this.Port = port;
			this.App = app;
			this.SwfUrl = swfUrl;
			this.PageUrl = pageUrl;
		}

		public void Connect ()
		{
			try {
				_client = new TcpClient (Server, Port);
				stream = new SslStream (_client.GetStream (), true, IsValidCertificate);
				stream.AuthenticateAsClient (Server);
			} catch (Exception ex) {
				Console.WriteLine (ex);
			}

			DoHandshake ();

			StartReaderThread (stream);

			// Connect
			var parameters = new Dictionary<String, Object> ();
			parameters.Add ("app", App);
			parameters.Add ("flashVer", "WIN 10,1,85,3");
			parameters.Add ("swfUrl", SwfUrl);
			parameters.Add ("tcUrl", "rtmps://" + Server + ":" + Port);
			parameters.Add ("fpad", false);
			parameters.Add ("capabilities", 239);
			parameters.Add ("audioCodecs", 3191);
			parameters.Add ("videoCodecs", 252);
			parameters.Add ("videoFunction", 1);
			parameters.Add ("pageUrl", PageUrl);
			parameters.Add ("objectEncoding", 3);

			byte[] connect = aec.EncodeConnect (parameters);
			stream.Write (connect, 0, connect.Length);
			stream.Flush ();

			TypedObject result = null;
			while (true) {
				results.TryGetValue (1, out result);
				if (result != null) { 
					break;
				}

				Thread.Sleep (100);
			}

			DSId = result.GetTO ("data").GetString ("id");

			IsConnected = true;
			Console.WriteLine ("Connected");
		}

		private bool IsValidCertificate (object sender, X509Certificate cert, X509Chain chain, SslPolicyErrors errors)
		{
			return true;
		}

		private void DoHandshake ()
		{
			byte[] message;

			// C0
			byte C0 = 0x03;
			stream.WriteByte (C0);

			// C1
			var timestampC1 = DateTime.Now.ToFileTimeUtc ();
			var randC1 = new byte[1528];
			rand.NextBytes (randC1);

			message = BitConverter.GetBytes ((int)timestampC1);
			stream.Write (message);

			message = BitConverter.GetBytes (0);
			stream.Write (message);
			stream.Write (randC1, 0, randC1.Length);
			stream.Flush ();

			// S0
			var S0 = stream.ReadByte ();
			if (S0 != 3) {
				throw new Exception (String.Format ("Server returned incorrect version in handshake: {0}", S0));
			}

			// S1
			var S1 = new byte[1536];
			stream.Read (S1, 0, 1536);

			// C2
			var timestampS1 = DateTime.Now.ToFileTimeUtc ();
			message = BitConverter.GetBytes ((int)timestampS1);
			stream.Write (S1, 0, 4);
			stream.Write (message);
			stream.Write (S1, 8, 1528);
			stream.Flush ();

			// S2
			var S2 = new byte[1536];
			stream.Read (S2, 0, 1536);

			// Validate handshake
			var valid = true;
			for (int i = 8; i < 1536; i++) {
				if (randC1 [i - 8] != S2 [i]) {
					valid = false;
					break;
				}
			}

			if (!valid) {
				throw new IOException ("Server returned invalid handshake");
			}
		}

		public void DoReconnect ()
		{
			throw new NotImplementedException ();
		}

		public void Close ()
		{
			_readerThread.Abort ();

			IsConnected = false;

			try {
				_client.Close ();
			} catch (SocketException ex) {
				throw ex;
			}

			Console.WriteLine ("Disconnected");
		}

		public Task<TypedObject> InvokeAsync (TypedObject packet)
		{
			int id = NextInvokeID ();

			Console.WriteLine ("invoking: ", id);

			var tcs = new TaskCompletionSource<TypedObject> (); 

			try {
				byte[] data = aec.EncodeInvoke (id, packet);
				stream.Write (data, 0, data.Length);
				stream.Flush ();
			} catch (Exception ex) {
				tcs.SetException (ex);	
			}

			// if we did succesfully start request, we can add the pendingInvoke to handle result
			if (!pendingInvokes.TryAdd (id, tcs)) {
				Console.WriteLine ("failed to add invoke with id: " + id);
			}

			return tcs.Task;
		}

		public Task<TypedObject> InvokeAsync (string destination, object operation, object body)
		{
			var to = WrapBody (body, destination, operation);
			return InvokeAsync (to);
		}

		protected TypedObject WrapBody (Object body, String destination, Object operation)
		{
			TypedObject headers = new TypedObject ();
			headers.Add ("DSRequestTimeout", 60);
			headers.Add ("DSId", DSId);
			headers.Add ("DSEndpoint", "my-rtmps");

			TypedObject ret = new TypedObject ("flex.messaging.messages.RemotingMessage");
			ret.Add ("destination", destination);
			ret.Add ("operation", operation);
			ret.Add ("source", null);
			ret.Add ("timestamp", 0);
			ret.Add ("messageId", AMF3Encoder.RandomUID ());
			ret.Add ("timeToLive", 0);
			ret.Add ("clientId", null);
			ret.Add ("headers", headers);
			ret.Add ("body", body);

			return ret;
		}

		protected int NextInvokeID ()
		{
			return _invokeID++;
		}

		public void StartReaderThread (SslStream ssls)
		{
			_readerThread = new Thread (() => {
				Console.WriteLine ("starting reader thread ...");
				while (true) {
					ReadMessage (ssls);
				}
			});
			_readerThread.Name = "LeagueLib reader thread";
			_readerThread.Start (ssls);
		}

		private static Dictionary<Int32, RTMPPacket> packets = new Dictionary<Int32, RTMPPacket> ();

		public void ReadMessage (SslStream stream)
		{	
			try {
				int val = 0;
				while ((val = stream.ReadByte ()) != -1) {
					byte basicHeader = (byte)val;
					int channel = basicHeader & 0x2F;
					int headerType = basicHeader & 0xC0;

					int headerSize = 0;
					if (headerType == 0x00) {
						headerSize = 12;
					} else if (headerType == 0x40) {
						headerSize = 8;
					} else if (headerType == 0x80) {
						headerSize = 4;
					} else if (headerType == 0xC0) {
						headerSize = 1;
					}

					if (! packets.ContainsKey (channel)) {
						packets.Add (channel, new RTMPPacket ());
					} 
					RTMPPacket p = packets [channel];

					if (headerSize > 1) {
						byte[] header = new byte[headerSize - 1];
						for (int i = 0; i < header.Length; i++) {
							header [i] = (byte)stream.ReadByte ();
						}

						if (headerSize >= 8) {
							int size = 0;
							for (int i = 3; i < 6; i++) {
								size = size * 256 + (header [i] & 0xFF);
							}
							p.Size = size;
							p.Type = header [6];
						}
					}

					for (int i = 0; i < 128; i++) {
						byte sb = (byte)stream.ReadByte ();
						p.Add (sb);

						if (p.IsComplete) {
							break;
						}
					}

					if (!p.IsComplete) {
						continue;
					}

					packets.Remove (channel);

					TypedObject result = null;

					switch (p.Type) {
					case 0x14:
						Console.WriteLine ("connect");
						result = adc.DecodeConnect (p.Buffer);
						break;
					case 0x11:
						result = adc.DecodeInvoke (p.Buffer);
						Console.WriteLine ("invoke, id: " + result ["invokeId"]);
						break;
					case 0x06:
						Console.WriteLine ("bandwidth");
						break;
					case 0x03:
						Console.WriteLine ("ack");
						break;
					default:
						Console.WriteLine ("unknown type: {0:x2}", headerType);
						break;
					}

					if (result == null) {
						continue;
					}

					// Store result
					int? id = result.GetInt ("invokeId");
					if (id == null || id == 0) {

					} else if (pendingInvokes.ContainsKey (id.Value)) {
						TaskCompletionSource<TypedObject> tcs;
						if (pendingInvokes.TryGetValue (id.Value, out tcs)) {
							tcs.SetResult (result);
						} else {
							Console.WriteLine ("callback with id " + id.Value + " not found");
						}
					} else {
						Console.WriteLine ("adding key: {0}", id.Value);
						results.TryAdd (id.Value, result);
					}
				}				
			} catch (IOException ex) {
				if (!IsReconnecting && IsConnected) {
					Console.WriteLine ("Error while reading from stream");
					Console.WriteLine (ex.StackTrace);
				}
			} catch (ObjectDisposedException ex) {
				// TODO: very ugly solution. Use readAsync instead and a cancellationToken
				//	http://stackoverflow.com/a/15358314/250164
				Console.WriteLine ("stream closed: {0}", ex);
			}

			if (!IsReconnecting && IsConnected) {
				DoReconnect ();
			}
		}

		private class RTMPPacket
		{
			public byte[] Buffer { get; private set; }

			public int Type { get; set; }

			public int Size {
				get { return _size; }
				set {
					_size = value;
					Buffer = new byte[_size];
				}
			}

			private int _size;
			private int _position;

			public bool IsComplete { get { return _position == Size; } }

			public void Add (Byte b)
			{
				Buffer [_position++] = b;
			}
		}
	}
}

