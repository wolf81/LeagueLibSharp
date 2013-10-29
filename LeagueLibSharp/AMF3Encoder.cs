using System;
using System.Net;
using System.Text;
using System.Collections.Generic;

namespace LeagueRTMPSSharp
{
	public class AMF3Encoder
	{
		private static Random rand = new Random ();
		private long startTime = System.DateTime.Now.ToFileTime ();

		public AMF3Encoder ()
		{
		}

		public byte[] AddHeaders (byte[] data)
		{
			var result = new List<Byte> ();

			result.Add ((byte)0x03);

			long timeDiff = System.DateTime.Now.ToFileTime () - startTime;
			result.Add ((byte)((timeDiff & 0xFF0000) >> 16));
			result.Add ((byte)((timeDiff & 0x00FF00) >> 8));
			result.Add ((byte)(timeDiff & 0x0000FF));

			// Body size
			result.Add ((byte)((data.Length & 0xFF0000) >> 16));
			result.Add ((byte)((data.Length & 0x00FF00) >> 8));
			result.Add ((byte)(data.Length & 0x0000FF));

			result.Add ((byte)0x11);

			result.Add ((byte)0x00);
			result.Add ((byte)0x00);
			result.Add ((byte)0x00);
			result.Add ((byte)0x00);

			for (int i = 0; i < data.Length; i++) {
				result.Add (data [i]);
				if (i % 128 == 127 && i != (data.Length - 1)) {
					result.Add ((byte)0xC3);
				}
			}

			byte[] ret = new byte[result.Count];
			for (int i = 0; i < ret.Length; i++) {
				ret [i] = result [i];
			}

			return ret;
		}

		public byte[] EncodeConnect (Dictionary<String, Object> parameters)
		{
			var result = new List<Byte> ();

			WriteStringAMF0 (result, "connect");
			WriteIntAMF0 (result, 1); // invokeId

			// Write params
			result.Add ((byte)0x11); // AMF3 object
			result.Add ((byte)0x09); // Array
			WriteAssociativeArray (result, parameters);

			// Write service call args
			result.Add ((byte)0x01);
			result.Add ((byte)0x00); // false
			WriteStringAMF0 (result, "nil"); // "nil"
			WriteStringAMF0 (result, ""); // ""

			// Set up CommandMessage
			TypedObject cm = new TypedObject ("flex.messaging.messages.CommandMessage");
			cm.Add ("messageRefType", null);
			cm.Add ("operation", 5);
			cm.Add ("correlationId", "");
			cm.Add ("clientId", null);
			cm.Add ("destination", "");
			var id = RandomUID ();
			cm.Add ("messageId", id);
			cm.Add ("timestamp", 0d);
			cm.Add ("timeToLive", 0d);
			cm.Add ("body", new TypedObject (null));
			var headers = new Dictionary<string, object> ();
			headers.Add ("DSMessagingVersion", 1d);
			headers.Add ("DSId", "my-rtmps");
			cm.Add ("headers", headers);

			// Write CommandMessage
			result.Add ((byte)0x11); // AMF3 object
			encode (result, cm);

			byte[] ret = new byte[result.Count];
			for (int i = 0; i < ret.Length; i++) {
				ret [i] = result [i];
			}

			var result2 = BitConverter.ToString (ret);

			ret = AddHeaders (ret);
			ret [7] = (byte)0x14; // Change message type

			return ret;		
		}

		public byte[] encode (Object obj)
		{
			throw new NotImplementedException ();
		}

		public void encode (List<Byte> ret, Object obj)
		{
			if (obj == null) {
				ret.Add ((byte)0x01);
			} else if (obj is Boolean) {
				bool val = (Boolean)obj;
				if (val) {
					ret.Add ((byte)0x03);
				} else {
					ret.Add ((byte)0x02);
				}
			} else if (obj is int) {
				ret.Add ((byte)0x04);
				WriteInt (ret, (int)obj);
			} else if (obj is Double) {
				ret.Add ((byte)0x05);
				WriteDouble (ret, (Double)obj);
			} else if (obj is String) {
				ret.Add ((byte)0x06);
				WriteString (ret, (String)obj);
			} else if (obj is DateTime) {
				ret.Add ((byte)0x08);
				WriteDate (ret, (DateTime)obj);
			}
			// Must precede Object[] check
			else if (obj is Byte[]) {
				ret.Add ((byte)0x0C);
				WriteByteArray (ret, (byte[])obj);
			} else if (obj is Object[]) {
				ret.Add ((byte)0x09);
				WriteArray (ret, (Object[])obj);
			}
			// Must precede Map check
			else if (obj is TypedObject) {
				ret.Add ((byte)0x0A);
				WriteObject (ret, (TypedObject)obj);
			} else if (obj is Dictionary<String,Object>) {
				ret.Add ((byte)0x09);
				WriteAssociativeArray (ret, (Dictionary<String, Object>)obj);
			} else {
				throw new Exception ("Unexpected object type: " + obj.GetType ());
			}
		}

		private void WriteObject (List<Byte> ret, TypedObject val)
		{
			if (val.Type == null || val.Type.Equals ("")) {
				ret.Add ((byte)0x0B); // Dynamic class

				ret.Add ((byte)0x01); // No class name
				foreach (String key in val.Keys) {
					WriteString (ret, key);
					encode (ret, val [key]);
				}
				ret.Add ((byte)0x01); // End of dynamic
			} else if (val.Type.Equals ("flex.messaging.io.ArrayCollection")) {
				ret.Add ((byte)0x07); // Externalizable
				WriteString (ret, val.Type);

				encode (ret, val ["array"]);
			} else {
				WriteInt (ret, (val.Count << 4) | 3); // Inline + member count
				WriteString (ret, val.Type);

				List<String> keyOrder = new List<String> ();
				foreach (String key in val.Keys) {
					WriteString (ret, key);
					keyOrder.Add (key);
				}

				foreach (String key in keyOrder)
					encode (ret, val [key]);
			}
		}

		private void WriteDate (List<Byte> ret, DateTime val)
		{
			throw new NotImplementedException ();
		}

		private void WriteByteArray (List<Byte> ret, byte[] val)
		{
			throw new NotImplementedException ();
		}

		private void WriteArray (List<Byte> ret, Object[] val)
		{
			throw new NotImplementedException ();
		}

		private void WriteStringAMF0 (List<Byte> ret, String val)
		{
			byte[] temp = null;

			try {
				temp = System.Text.Encoding.UTF8.GetBytes (val);
			} catch (Exception e) {
				Console.WriteLine (e);
				throw new Exception ("Unable to encode string as UTF-8: " + val);
			}

			ret.Add ((byte)0x02);
			ret.Add ((byte)((temp.Length & 0xFF00) >> 8));
			ret.Add ((byte)(temp.Length & 0x00FF));

			foreach (byte b in temp) {
				ret.Add (b);
			}
		}

		private void WriteIntAMF0 (List<Byte> ret, int val)
		{
			ret.Add ((byte)0x00);

			var temp = BitConverter.GetBytes ((double)val);
			Array.Reverse (temp);

			foreach (byte b in temp) {
				ret.Add (b);
			}
		}

		private void WriteInt (List<Byte> ret, int val)
		{
			if (val < 0 || val >= 0x200000) {
				ret.Add ((byte)(((val >> 22) & 0x7f) | 0x80));
				ret.Add ((byte)(((val >> 15) & 0x7f) | 0x80));
				ret.Add ((byte)(((val >> 8) & 0x7f) | 0x80));
				ret.Add ((byte)(val & 0xff));
			} else {
				if (val >= 0x4000) { // 16384
					ret.Add ((byte)(((val >> 14) & 0x7f) | 0x80));
				}
				if (val >= 0x80) { // 128
					var val3 = String.Format ("{0:X}", val); 
					ret.Add ((byte)(((val >> 7) & 0x7f) | 0x80));
				}
				ret.Add ((byte)(val & 0x7f));
			}
		}

		private void WriteDouble (List<Byte> ret, double val)
		{
			if (Double.IsNaN (val)) {
				ret.Add ((byte)0x7F);
				ret.Add ((byte)0xFF);
				ret.Add ((byte)0xFF);
				ret.Add ((byte)0xFF);
				ret.Add ((byte)0xE0);
				ret.Add ((byte)0x00);
				ret.Add ((byte)0x00);
				ret.Add ((byte)0x00);
			} else {
				byte[] temp = BitConverter.GetBytes (val);
				foreach (byte b in temp) {
					ret.Add (b);
				}
			}
		}

		private void WriteString (List<Byte> ret, String val)
		{
			byte[] temp = null;
			try {
				temp = System.Text.Encoding.UTF8.GetBytes (val);
			} catch (Exception e) {
				Console.WriteLine (e);
				throw new Exception ("Unable to encode string as UTF-8: " + val);
			}

			WriteInt (ret, (temp.Length << 1) | 1);

			foreach (byte b in temp) {
				ret.Add (b);
			}
		}

		private void WriteAssociativeArray (List<Byte> ret, Dictionary<String, Object> val)
		{
			ret.Add ((byte)0x01);
			foreach (String key in val.Keys) {
				WriteString (ret, key);
				Console.WriteLine ("{0}", key);
				encode (ret, val [key]);
			}
			ret.Add ((byte)0x01);
		}

		public static String RandomUID ()
		{
			byte[] bytes = new byte[16];
			rand.NextBytes (bytes);

			StringBuilder ret = new StringBuilder ();
			for (int i = 0; i < bytes.Length; i++) {
				if (i == 4 || i == 6 || i == 8 || i == 10)
					ret.Append ('-');
				ret.Append (String.Format ("{0:X2}", bytes [i]));
			}

			return ret.ToString ();
		}
	}
}

