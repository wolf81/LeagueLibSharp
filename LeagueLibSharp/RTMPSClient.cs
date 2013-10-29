using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Net.Sockets;
using System.Net.Security;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;

namespace LeagueRTMPSSharp
{
	public class RTMPSClient
	{
		public bool IsConnected { get; private set; }

		public string Server { get; private set; }

		public int Port { get; private set; }

		public string App { get; private set; }

		public string SwfUrl { get; private set; }

		public string PageUrl { get; private set; }

		protected String DSId;
		protected Random rand = new Random ();
		protected SslStream stream;
		protected AMF3Encoder aec = new AMF3Encoder ();
		protected static AMF3Decoder adc = new AMF3Decoder ();
		// TODO: Java version uses a synchronized map, so perhaps create a
		//	dictionary subclass with a sync lock instead?
		private Dictionary<int, TypedObject> results = new Dictionary<int, TypedObject> ();

		static void Main (string[] args)
		{
			var client = new RTMPSClient ("prod.na1.lol.riotgames.com", 2099, "", "app:/mod_ser.dat", null);

			try {
				client.Connect ();
				if (!client.IsConnected) {
					Console.WriteLine ("Connection failed");
				}
			} catch (Exception ex) {
				Console.WriteLine (ex);
			}
		}

		public RTMPSClient (string server, int port, string app, string swfUrl, string pageUrl)
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
				var client = new TcpClient (Server, Port);

				stream = new SslStream (client.GetStream (), true, IsValidCertificate);
				stream.AuthenticateAsClient (Server);
			} catch (Exception ex) {
				Console.WriteLine (ex);
			}

			DoHandshake ();

			StartReaderSenderThreads (stream);

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
			Console.WriteLine ("{0}", BitConverter.ToString (connect));
			stream.Write (connect, 0, connect.Length);
			stream.Flush ();


//			while (!results.ContainsKey (1)) {
//				Thread.Sleep (1000);
//			}
//
//			TypedObject result = results [1];
//			DSId = result.GetTO ("data").GetString ("id");

			IsConnected = true;
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

		public void Close ()
		{

		}

		static Thread readerThread = null;
		static Thread senderThread = null;

		public static void ReaderThread (Object str)
		{
			SslStream ssls = str as SslStream;
			while (true) {
				var result = ReadMessage (ssls);
				if (result != null) {
					Console.WriteLine ("Received: {0}", result);
				}

				/*
				if (message.ToLower ().Equals ("\\quit")) {
					ssls.Write (Encoding.UTF8.GetBytes ("\\quit<EOF>"));
					ssls.Flush ();
					break;
				}
				*/

			}
		}

		public static void SenderThread (Object str)
		{
			SslStream ssls = str as SslStream;
			while (true) {

				/*
				string message = Console.ReadLine ();
				if (readerThread.IsAlive) {
					ssls.Write (Encoding.UTF8.GetBytes (message + "<EOF>"));
					ssls.Flush ();
				}
				if (message.ToLower ().Equals ("\\quit"))
					break;
					*/

			}
		}

		public static void StartReaderSenderThreads (SslStream ssls)
		{
			ParameterizedThreadStart readerStarter =
				new ParameterizedThreadStart (ReaderThread);
			ParameterizedThreadStart senderStarter =
				new ParameterizedThreadStart (SenderThread);
			readerThread = new Thread (readerStarter);
//			senderThread = new Thread (senderStarter);
			readerThread.Start (ssls);
//			senderThread.Start (ssls);      
//			readerThread.Join ();
//			senderThread.Join ();
		}

		private static Dictionary<Int32, Packet> packets = new Dictionary<Int32, Packet> ();

		public static TypedObject ReadMessage (SslStream stream)
		{
			int b = -1;
			TypedObject result = null;
		
			while (true) {
				byte basicHeader = (byte)stream.ReadByte ();

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
					packets.Add (channel, new Packet ());
				} 
				Packet p = packets [channel];

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
						Console.WriteLine ("type: {0:X2}", p.Type);
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

				Console.WriteLine ("actual type: {0:X2}", p.Type);

				packets.Remove (channel);

				switch (p.Type) {
				case 0x14:
					Console.WriteLine ("connect");

					result = adc.DecodeConnect (p.Buffer);

					break;
				case 0x11:
					Console.WriteLine ("invoke");
					break;
				case 0x06:
					Console.WriteLine ("bandwidth");
					break;
				case 0x03:
					Console.WriteLine ("ack");
					break;
				default:
//					Console.WriteLine ("other type: {0:x2}", headerType);
					break;
				}

				break;
			}

			return result;
		}

		private class Packet
		{
			public byte[] Buffer { get; private set; }

			private int Position { get; set; }

			public int Type { get; set; }

			private int size;

			public int Size {
				get { return size; }
				set {
					size = value;
					Buffer = new byte[size];
				}
			}

			public bool IsComplete { get { return Position == Size; } }

			public Packet ()
			{
			}

			public void Add (Byte b)
			{
				Buffer [Position++] = b;
			}
		}
	}
}

