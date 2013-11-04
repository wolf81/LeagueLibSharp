// /*
//  * Copyright (c) 2013 Wolfgang Schreurs <wolfgang.schreurs@gmail.com>
//  *
//  * Permission to use, copy, modify, and distribute this software for any
//  * purpose with or without fee is hereby granted, provided that the above
//  * copyright notice and this permission notice appear in all copies.
//  *
//  * THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
//  * WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
//  * MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR
//  * ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
//  * WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN
//  * ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF
//  * OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
//  */
//
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
		private int _invokeID = 2;
		private String _DSId = null;
		private Random _rand = new Random ();
		private TcpClient _client = null;
		private SslStream _stream = null;
		private Thread _readerThread = null;
		private AMF3Encoder _aec = new AMF3Encoder ();
		private AMF3Decoder _adc = new AMF3Decoder ();
		private Dictionary<int, RTMPPacket> _packets = new Dictionary<int, RTMPPacket> ();
		private ConcurrentDictionary<int, TypedObject> _results = new ConcurrentDictionary<int, TypedObject> ();
		private ConcurrentDictionary<int, TaskCompletionSource<TypedObject>> _pendingInvokes = new ConcurrentDictionary<int, TaskCompletionSource<TypedObject>> ();

		public bool IsConnected { get; private set; }

		public bool IsReconnecting { get; private set; }

		public string Server { get; private set; }

		public int Port { get; private set; }

		public string App { get; private set; }

		public string SwfUrl { get; private set; }

		public string PageUrl { get; private set; }

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
				_stream = new SslStream (_client.GetStream (), true, IsValidCertificate);
				_stream.AuthenticateAsClient (Server);
			} catch (Exception ex) {
				Console.WriteLine (ex);
			}

			DoHandshake ();

			StartReaderThread (_stream);

			// Connect
			var parameters = new Dictionary<string, object> ();
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

			var result = ConnectTask (parameters).Result;
			_DSId = result.GetTO ("data").GetString ("id");

			IsConnected = true;
			Console.WriteLine ("Connected");
		}

		private Task<TypedObject> ConnectTask (Dictionary <string, object> parameters)
		{
			var id = 1;
			var tcs = new TaskCompletionSource<TypedObject> (); 

			if (!_pendingInvokes.TryAdd (id, tcs)) {
				Console.WriteLine ("failed to add invoke with id: " + id);
			}

			try {
				var data = _aec.EncodeConnect (parameters);
				_stream.Write (data, 0, data.Length);
				_stream.Flush ();
			} catch (Exception ex) {
				_pendingInvokes.TryRemove (id, out tcs);
				tcs.SetException (ex);	
			}

			return tcs.Task;
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
			_stream.WriteByte (C0);

			// C1
			var timestampC1 = DateTime.Now.ToFileTimeUtc ();
			var randC1 = new byte[1528];
			_rand.NextBytes (randC1);

			message = BitConverter.GetBytes ((int)timestampC1);
			_stream.Write (message);

			message = BitConverter.GetBytes (0);
			_stream.Write (message);
			_stream.Write (randC1, 0, randC1.Length);
			_stream.Flush ();

			// S0
			var S0 = _stream.ReadByte ();
			if (S0 != 3) {
				throw new Exception ("Server returned incorrect version in handshake: " + S0);
			}

			// S1
			var S1 = new byte[1536];
			_stream.Read (S1, 0, 1536);

			// C2
			var timestampS1 = DateTime.Now.ToFileTimeUtc ();
			message = BitConverter.GetBytes ((int)timestampS1);
			_stream.Write (S1, 0, 4);
			_stream.Write (message);
			_stream.Write (S1, 8, 1528);
			_stream.Flush ();

			// S2
			var S2 = new byte[1536];
			_stream.Read (S2, 0, 1536);

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

		public TypedObject Invoke (TypedObject packet)
		{
			return InvokeTask (packet).Result;
		}

		public Task<TypedObject> InvokeTask (TypedObject packet)
		{
			var id = NextInvokeID ();
			var tcs = new TaskCompletionSource<TypedObject> (); 

			if (!_pendingInvokes.TryAdd (id, tcs)) {
				Console.WriteLine ("failed to add invoke with id: " + id);
			}

			try {
				var data = _aec.EncodeInvoke (id, packet);
				_stream.Write (data, 0, data.Length);
				_stream.Flush ();
			} catch (Exception ex) {
				_pendingInvokes.TryRemove (id, out tcs);
				tcs.SetException (ex);	
			}

			return tcs.Task;
		}

		public TypedObject Invoke (string destination, object operation, object body)
		{
			return InvokeTask (destination, operation, body).Result;
		}

		public Task<TypedObject> InvokeTask (string destination, object operation, object body)
		{
			var to = WrapBody (body, destination, operation);
			return InvokeTask (to);
		}

		protected TypedObject WrapBody (object body, string destination, object operation)
		{
			var headers = new TypedObject ();
			headers.Add ("DSRequestTimeout", 60);
			headers.Add ("DSId", _DSId);
			headers.Add ("DSEndpoint", "my-rtmps");

			var ret = new TypedObject ("flex.messaging.messages.RemotingMessage");
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

					if (! _packets.ContainsKey (channel)) {
						_packets.Add (channel, new RTMPPacket ());
					} 
					var p = _packets [channel];

					if (headerSize > 1) {
						var header = new byte[headerSize - 1];
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
						var sb = (byte)stream.ReadByte ();
						p.Add (sb);

						if (p.IsComplete) {
							break;
						}
					}

					if (!p.IsComplete) {
						continue;
					}

					_packets.Remove (channel);

					TypedObject result = null;

					switch (p.Type) {
					case 0x14:
						Console.WriteLine ("connect");
						result = _adc.DecodeConnect (p.Buffer);
						break;
					case 0x11:
						Console.WriteLine ("invoke");
						result = _adc.DecodeInvoke (p.Buffer);
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
					var id = result.GetInt ("invokeId");
					Console.WriteLine ("finished " + id.Value);
					if (id == null || id == 0) {
						// don't do anything ...
					} else if (_pendingInvokes.ContainsKey (id.Value)) {
						TaskCompletionSource<TypedObject> tcs;
						if (_pendingInvokes.TryRemove (id.Value, out tcs)) {
							tcs.SetResult (result);
						} else {
							Console.WriteLine ("callback with id " + id.Value + " not found");
						}
					} else {
						Console.WriteLine ("adding key: {0}", id.Value);
						_results.TryAdd (id.Value, result);
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
	}
}

