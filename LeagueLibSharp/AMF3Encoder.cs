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
using System.Net;
using System.Text;
using System.Collections.Generic;

namespace LeagueRTMPSSharp
{
	internal class AMF3Encoder
	{
		private static Random rand = new Random ();
		private long startTime = System.DateTime.Now.ToFileTime ();

		public byte[] AddHeaders (byte[] data)
		{
			var result = new List<byte> ();

			result.Add ((byte)0x03);

			var timeDiff = System.DateTime.Now.ToFileTime () - startTime;
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

			var ret = new byte[result.Count];
			for (int i = 0; i < ret.Length; i++) {
				ret [i] = result [i];
			}

			return ret;
		}

		public byte[] EncodeConnect (Dictionary<string, object> parameters)
		{
			var result = new List<byte> ();

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
			var cm = new TypedObject ("flex.messaging.messages.CommandMessage");
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

			var ret = new byte[result.Count];
			for (int i = 0; i < ret.Length; i++) {
				ret [i] = result [i];
			}

			ret = AddHeaders (ret);
			ret [7] = (byte)0x14; // Change message type

			return ret;		
		}

		public byte[] EncodeInvoke (int id, object data)
		{
			var result = new List<byte> ();

			result.Add ((byte)0x00); // version
			result.Add ((byte)0x05); // type?
			WriteIntAMF0 (result, id); // invoke ID
			result.Add ((byte)0x05); // ???
			result.Add ((byte)0x11); // AMF3 object
			encode (result, data);

			var ret = new byte[result.Count];
			for (int i = 0; i < ret.Length; i++) {
				ret [i] = result [i];
			}

			ret = AddHeaders (ret);

			return ret;
		}

		public byte[] encode (object obj)
		{
			throw new NotImplementedException ();
		}

		public void encode (List<byte> ret, object obj)
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

		private void WriteObject (List<byte> ret, TypedObject val)
		{
			if (val.Type == null || val.Type.Equals ("")) {
				ret.Add ((byte)0x0B); // Dynamic class
				ret.Add ((byte)0x01); // No class name

				foreach (var key in val.Keys) {
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

				var keyOrder = new List<string> ();
				foreach (var key in val.Keys) {
					WriteString (ret, key);
					keyOrder.Add (key);
				}

				foreach (var key in keyOrder) {
					encode (ret, val [key]);
				}
			}
		}

		private void WriteDate (List<byte> ret, DateTime val)
		{
			throw new NotImplementedException ();
		}

		private void WriteByteArray (List<byte> ret, byte[] val)
		{
			throw new NotImplementedException ();
		}

		private void WriteArray (List<byte> ret, object[] val)
		{
			WriteInt (ret, (val.Length << 1) | 1);

			ret.Add ((byte)0x01);

			foreach (var obj in val) {
				encode (ret, obj);
			}
		}

		private void WriteStringAMF0 (List<byte> ret, string val)
		{
			var temp = Encoding.UTF8.GetBytes (val);

			ret.Add ((byte)0x02);
			ret.Add ((byte)((temp.Length & 0xFF00) >> 8));
			ret.Add ((byte)(temp.Length & 0x00FF));

			foreach (byte b in temp) {
				ret.Add (b);
			}
		}

		private void WriteIntAMF0 (List<byte> ret, int val)
		{
			ret.Add ((byte)0x00);

			var temp = BitConverter.GetBytes ((double)val);
			Array.Reverse (temp);

			foreach (byte b in temp) {
				ret.Add (b);
			}
		}

		private void WriteInt (List<byte> ret, int val)
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
					ret.Add ((byte)(((val >> 7) & 0x7f) | 0x80));
				}
				ret.Add ((byte)(val & 0x7f));
			}
		}

		private void WriteDouble (List<byte> ret, double val)
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
				var temp = BitConverter.GetBytes (val);
				foreach (byte b in temp) {
					ret.Add (b);
				}
			}
		}

		private void WriteString (List<byte> ret, string val)
		{
			var temp = Encoding.UTF8.GetBytes (val);

			WriteInt (ret, (temp.Length << 1) | 1);

			foreach (byte b in temp) {
				ret.Add (b);
			}
		}

		private void WriteAssociativeArray (List<byte> ret, Dictionary<string, object> val)
		{
			ret.Add ((byte)0x01);

			foreach (var key in val.Keys) {
				WriteString (ret, key);
				encode (ret, val [key]);
			}

			ret.Add ((byte)0x01);
		}

		public static string RandomUID ()
		{
			var bytes = new byte[16];
			rand.NextBytes (bytes);

			var ret = new StringBuilder ();
			for (int i = 0; i < bytes.Length; i++) {
				if (i == 4 || i == 6 || i == 8 || i == 10) {
					ret.Append ('-');
				}
				ret.Append (String.Format ("{0:X2}", bytes [i]));
			}

			return ret.ToString ();
		}
	}
}

