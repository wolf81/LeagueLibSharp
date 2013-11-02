using System;
using System.IO;
using System.Net;
using System.Web;
using System.Text;
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

			try {
				client.ConnectAndLogin ();
			} catch (Exception ex) {
				Console.WriteLine (ex);
			}

			if (client.IsConnected) {
				System.Threading.Thread.Sleep (15000);
				client.Close ();
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
			TypedObject cause = result.GetTO ("data").GetTO ("rootCause");
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

			TypedObject body;

			// Login 1
			body = new TypedObject ("com.riotgames.platform.login.AuthenticationCredentials");
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
			int id = Invoke ("loginService", "login", new Object[] { body }, (TypedObject result) => {
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

				// TODO: might need to do this differently ... perhaps need to be converted to bytes instead of string?
				body = WrapBody (Convert.ToBase64String (encbuff), "auth", 8);
				body.Type = "flex.messaging.messages.CommandMessage";

				Invoke (body, (TypedObject r2) => {
					// Subscribe to the necessary items
					body = WrapBody (new Object[] { new TypedObject () }, "messagingDestination", 0);
					body.Type = "flex.messaging.messages.CommandMessage";
					TypedObject headers = body.GetTO ("headers");
					var key = "clientId";

					return;

					// bc
					headers.Add ("DSSubtopic", "bc");
					if (body.ContainsKey (key)) {
						body [key] = "bc-" + _accountID;
					} else {
						body.Add (key, "bc-" + _accountID);
					}

					Invoke (body, (TypedObject r3) => {

						// cn
						headers ["DSSubtopic"] = "cn-" + _accountID;
						body [key] = "cn-" + _accountID;

						Invoke (body, (TypedObject r4) => {

							// gn
							headers ["DSSubtopic"] = "gn-" + _accountID;
							body [key] = "gn-" + _accountID;

							Invoke (body, (TypedObject r5) => {
								/*
								// Start the heartbeat
								new HeartbeatThread ();

								loggedIn = true;
								*/
							});
						});
					});
				});
			});
		}

		private void GetIPAddress ()
		{
			// Don't need to retrieve IP address on reconnect (probably)
			if (_ipAddress != null)
				return;

			using (WebClient client = new WebClient()) {
				string response = client.DownloadString ("http://ll.leagueoflegends.com/services/connection_info");

				if (response == null) {
					_ipAddress = "127.0.0.1";
					return;
				}

				JObject json = JObject.Parse (response);
				_ipAddress = (string)json.SelectToken ("ip_address");
				Console.WriteLine ("{0}", _ipAddress);
			}
		}

		public bool AcceptAllCertifications (object sender, X509Certificate certification, X509Chain chain, SslPolicyErrors sslPolicyErrors)
		{
			return true;
		}

		private void GetAuthToken ()
		{
			string payload = String.Format ("user={0},password={1}", Username, Password);
			string query = String.Format ("payload={0}", WebUtility.UrlEncode (payload));
			Uri url = new Uri (_loginQueue + "login-queue/rest/queue/authenticate");

			if (_loginQueue.StartsWith ("https:")) {
				// Need to ignore certs (or use the one retrieved by RTMPSClient?)
				ServicePointManager.ServerCertificateValidationCallback = new RemoteCertificateValidationCallback (AcceptAllCertifications);
			}

			var request = (HttpWebRequest)WebRequest.Create (url);
			request.Method = "POST";

			var bytes = Encoding.UTF8.GetBytes (query);

			using (var stream = request.GetRequestStream()) {
				stream.Write (bytes, 0, bytes.Length);
			}

			JObject json = null;
			try {
				var response = (HttpWebResponse)request.GetResponse ();
				using (var responseStream = response.GetResponseStream()) {
					using (StreamReader reader =new StreamReader(response.GetResponseStream())) {
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
				// TODO: implement handling of the login queue.

				/*
				int node = result.getInt("node"); // Our login queue ID
				String nodeStr = "" + node;
				String champ = result.getString("champ"); // The name of our login queue
				int rate = result.getInt("rate"); // How many tickets are processed every queue update
				int delay = result.getInt("delay"); // How often the queue status updates

				int id = 0;
				int cur = 0;
				Object[] tickers = result.getArray("tickers");
				for (Object o : tickers)
				{
					TypedObject to = (TypedObject)o;

					// Find our queue
					int tnode = to.getInt("node");
					if (tnode != node)
						continue;

					id = to.getInt("id"); // Our ticket in line
					cur = to.getInt("current"); // The current ticket being processed
					break;
				}

				// Let the user know
				System.out.println("In login queue for " + region + ", #" + (id - cur) + " in line");

				// Request the queue status until there's only 'rate' left to go
				while (id - cur > rate)
				{
					sleep(delay); // Sleep until the queue updates
					response = readURL(loginQueue + "login-queue/rest/queue/ticker/" + champ);
					result = (TypedObject)JSON.parse(response);
					if (result == null)
						continue;

					cur = hexToInt(result.getString(nodeStr));
					System.out.println("In login queue for " + region + ", #" + (int)Math.max(1, id - cur) + " in line");
				}

				// Then try getting our token repeatedly
				response = readURL(loginQueue + "login-queue/rest/queue/authToken/" + user.toLowerCase());
				result = (TypedObject)JSON.parse(response);
				while (response == null || !result.containsKey("token"))
				{
					sleep(delay / 10);
					response = readURL(loginQueue + "login-queue/rest/queue/authToken/" + user.toLowerCase());
					result = (TypedObject)JSON.parse(response);
				}
				*/

			}
			_authToken = (string)token;
		}
	}
}

