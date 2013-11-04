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
using System.Web;
using System.Text;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace LeagueRTMPSSharp
{
	public class LolRTMPSClient : RTMPSClient
	{
		public string Region { get; private set; }

		public string ClientVersion { get; private set; }

		public string Username { get; private set; }

		public string Password { get; private set; }

		private string _locale = "en_US";
		private string _ipAddress = null;
		private string _loginQueue = "https://lq.eu.lol.riotgames.com/";
		private string _authToken = null;
		private string _sessionToken = null;
		private int _accountID = -1;

		static void Main (string[] args)
		{
			var client = new LolRTMPSClient ("EUW", "3.13.xx", "wsc981", "");
			client.Test ();
		}

		private void Test ()
		{
			try {
				ConnectAndLogin ();

				if (IsConnected) {
					var result = Invoke ("summonerService", "getSummonerByName", new Object[] { "wolf1981" });
					Console.WriteLine (result);
				}
			} catch (Exception ex) {
				Console.WriteLine (ex);
			} finally {
				Close ();
			}
		}

		public LolRTMPSClient (string region, string clientVersion, string username, string password) : base()
		{
			this.Region = region.ToUpper ();
			this.ClientVersion = clientVersion;
			this.Username = username;
			this.Password = password;

			SetConnectionInfo ("prod.eu.lol.riotgames.com", 2099, "", "app:/mod_ser.dat", null);
		}

		private void ConnectAndLogin ()
		{
			try {
				Connect ();
				Login ();
			} catch (Exception ex) {
				throw ex;
			}
		}

		public string GetErrorMessage (TypedObject result)
		{
			// Works for clientVersion
			var cause = result.GetTO ("data").GetTO ("rootCause");
			var message = (string)cause ["message"];
			Console.WriteLine (result);
			return message;
		}

		private void Login ()
		{
			GetIPAddress ();
			GetAuthToken ();

			if (_authToken == null) {
				throw new Exception ("failed to get Auth token");
			}

			// Login 1
			var body = new TypedObject ("com.riotgames.platform.login.AuthenticationCredentials");
			body.Add ("username", Username);
			body.Add ("password", Password);
			body.Add ("authToken", _authToken);
			body.Add ("clientVersion", ClientVersion);
			body.Add ("ipAddress", _ipAddress);
			body.Add ("locale", _locale);
			body.Add ("domain", "lolclient.lol.riotgames.com");
			body.Add ("operatingSystem", "LoLRTMPSClient");
			body.Add ("securityAnswer", null);
			body.Add ("oldPassword", null);
			body.Add ("partnerCredentials", null);
			var result = Invoke ("loginService", "login", new Object[] { body });

			if (result ["result"].Equals ("_error")) {
				throw new IOException (GetErrorMessage (result));
			}

			var resultBody = result.GetTO ("data").GetTO ("body");
			_sessionToken = (string)resultBody ["token"];
			_accountID = resultBody.GetTO ("accountSummary").GetInt ("accountId").Value;

			// Login 2
			byte[] encbuff = null;
			var val = Username.ToLower () + ":" + _sessionToken;
			encbuff = Encoding.UTF8.GetBytes (val);
			body = WrapBody (Convert.ToBase64String (encbuff), "auth", 8);
			body.Type = "flex.messaging.messages.CommandMessage";
			Invoke (body);

			// Subscribe to the necessary items
			body = WrapBody (new Object[] { new TypedObject () }, "messagingDestination", 0);
			body.Type = "flex.messaging.messages.CommandMessage";
			var headers = body.GetTO ("headers");
			var key = "clientId";

			// bc
			headers.Add ("DSSubtopic", "bc");
			if (body.ContainsKey (key)) {
				body [key] = "bc-" + _accountID;
			} else {
				body.Add (key, "bc-" + _accountID);
			}
			Invoke (body);

			// cn
			headers ["DSSubtopic"] = "cn-" + _accountID;
			body [key] = "cn-" + _accountID;
			Invoke (body);

			// gn
			headers ["DSSubtopic"] = "gn-" + _accountID;
			body [key] = "gn-" + _accountID;
			Invoke (body);

			/*
			// Start the heartbeat
			new HeartbeatThread ();

			loggedIn = true;
			 */

		}

		private void GetIPAddress ()
		{
			// Don't need to retrieve IP address on reconnect (probably)
			if (_ipAddress != null)
				return;

			using (var client = new WebClient()) {
				var response = client.DownloadString ("http://ll.leagueoflegends.com/services/connection_info");

				if (response == null) {
					_ipAddress = "127.0.0.1";
					return;
				}

				var json = JObject.Parse (response);
				_ipAddress = (string)json.SelectToken ("ip_address");
				Console.WriteLine ("{0}", _ipAddress);
			}
		}

		public bool AcceptAllCertifications (object sender, X509Certificate certification, X509Chain chain, SslPolicyErrors sslPolicyErrors)
		{
			return true;
		}

		private JObject ReadURL (string url)
		{
			using (var client = new WebClient()) {
				var response = client.DownloadString (url);
				return JObject.Parse (response);
			}
		}

		private int hexToInt (string hex)
		{
			int total = 0;
			for (int i = 0; i < hex.Count(); i++) {
				char c = hex [i];
				if (c >= '0' && c <= '9') {
					total = total * 16 + c - '0';
				} else {
					total = total * 16 + c - 'a' + 10;
				}
			}

			return total;
		}

		private void GetAuthToken ()
		{
			var payload = String.Format ("user={0},password={1}", Username, Password);
			var query = String.Format ("payload={0}", WebUtility.UrlEncode (payload));
			var url = new Uri (_loginQueue + "login-queue/rest/queue/authenticate");

			if (_loginQueue.StartsWith ("https:")) {
				// Need to ignore certs (or use the one retrieved by RTMPSClient?)
				ServicePointManager.ServerCertificateValidationCallback = new RemoteCertificateValidationCallback (AcceptAllCertifications);
			}

			var request = (HttpWebRequest)WebRequest.Create (url);
			request.Method = "POST";

			using (var stream = request.GetRequestStream()) {
				var bytes = Encoding.UTF8.GetBytes (query);
				stream.Write (bytes, 0, bytes.Length);
			}

			JObject json = null;
			try {
				var response = (HttpWebResponse)request.GetResponse ();
				using (var responseStream = response.GetResponseStream()) {
					using (var reader = new StreamReader(response.GetResponseStream())) {
						var text = reader.ReadToEnd ();
						json = JObject.Parse (text);
					}
				}
			} catch (Exception ex) {
				Console.WriteLine (ex);
				// incorrect username or password?
			}

			if (json != null) {
				var status = (string)json.SelectToken ("status");
				if (status.Equals ("FAILED")) {
					var reason = (string)json.SelectToken ("reason");
					throw new Exception ("Error logging in: " + reason);
				}
			}

			var token = json.SelectToken ("token");
			if (token == null) {
				var node = (int)json.SelectToken ("node");
				var champ = (string)json.SelectToken ("champ");
				var rate = (int)json.SelectToken ("rate");
				var delay = (int)json.SelectToken ("delay");

				var id = 0;
				var cur = 0;
				var tickers = (JArray)json ["tickers"];

				foreach (var o in tickers) {
					var tnode = (int)o.SelectToken ("node");
					if (tnode != node) {
						continue;
					}

					id = (int)o.SelectToken ("id");
					cur = (int)o.SelectToken ("current");
					break;
				}

				Console.WriteLine ("In login queue for " + Region + ", #" + (id - cur) + " in line");

				while (id - cur > rate) {
					Thread.Sleep (delay);

					var response = ReadURL (_loginQueue + "login-queue/rest/queue/ticker/" + champ);
					if (response == null) {
						continue;
					}

					cur = hexToInt ((string)response ["nodeString"]);
					Console.WriteLine ("In login queue for " + Region + ", #" + (int)Math.Max (1, id - cur) + " in line");
				}

				while (token == null) {
					try {
						json = ReadURL (_loginQueue + "login-queue/rest/queue/authToken/" + Username.ToLower ());
						token = json.SelectToken ("token");
					} catch (Exception ex) {
						// we'll keep trying until we get a response - we'll silence the warning
						ex.ToString ();
					}

					if (token == null) {
						Thread.Sleep (delay / 10);
					}
				}
			}
			_authToken = (string)token;
		}
	}
}

