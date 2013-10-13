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
		// TODO: Java version uses a synchronized map, so perhaps create a
		//	dictionary subclass with a sync lock instead?
		private Dictionary<int, TypedObject> results = new Dictionary<int, TypedObject> ();

		static void Main (string[] args)
		{
			var client = new RTMPSClient ("prod.na1.lol.riotgames.com", 2099, "", "app:/mod_ser.dat", null);

			try {
				client.Connect ();
				if (client.IsConnected) {
					Console.WriteLine ("Success");
				} else {
					Console.WriteLine ("Failure");
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

				Console.WriteLine ("canRead: {0}, canWrite: {1}", stream.CanRead, stream.CanWrite);
			} catch (Exception ex) {
				Console.WriteLine (ex);
			}

			DoHandshake ();

			var packetReader = new RTMPPacketReader (stream);

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

			while (!results.ContainsKey (1)) {
				Thread.Sleep (10);
			}

			TypedObject result = results [1];
			DSId = result.GetTO ("data").GetString ("id");

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

		private class RTMPPacketReader
		{
			public SslStream Stream { get; private set; }

			public RTMPPacketReader (SslStream stream)
			{
				this.Stream = stream;
			}
		}
	}
}

