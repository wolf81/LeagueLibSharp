using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;

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

		protected Random rand = new Random ();
		protected SslStream stream;

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
			var timestampC1 = CurrentTimeMillis ();
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
			Console.WriteLine (S0);
			if (S0 != 3) {
				throw new Exception (String.Format ("Server returned incorrect version in handshake: {0}", S0));
			}

			// S1
			var S1 = new byte[1536];
			stream.Read (S1, 0, 1536);

			// C2
			var timestampS1 = CurrentTimeMillis ();
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

		#region Time synchronization

		private static readonly DateTime Jan1st1970 = new DateTime (1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

		public static long CurrentTimeMillis ()
		{
			return (long)(DateTime.UtcNow - Jan1st1970).TotalMilliseconds;
		}

		#endregion

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

