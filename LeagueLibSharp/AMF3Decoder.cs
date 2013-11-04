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
using System.Text;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace LeagueRTMPSSharp
{
	public class AMF3Decoder
	{
		private int _dataPos = 0;
		private byte[] _dataBuffer = null;
		private List<string> _stringReferences = new List<string> ();
		private List<object> _objectReferences = new List<object> ();
		private List<ClassDefinition> _classDefinitions = new List<ClassDefinition> ();

		private void Reset ()
		{
			_stringReferences.Clear ();
			_objectReferences.Clear ();
			_classDefinitions.Clear ();
		}

		public TypedObject DecodeConnect (byte[] data)
		{
			Reset ();

			_dataBuffer = data;
			_dataPos = 0;

			var result = new TypedObject ("Invoke");
			result.Add ("result", DecodeAMF0 ());
			result.Add ("invokeId", DecodeAMF0 ());
			result.Add ("serviceCall", DecodeAMF0 ());
			result.Add ("data", DecodeAMF0 ());

			if (_dataPos != _dataBuffer.Length) {
				var message = String.Format ("Did not consume entire buffer: {0} of {1}", _dataPos, _dataBuffer.Length);
				throw new Exception (message);
			}

			return result;
		}

		public TypedObject DecodeInvoke (byte[] data)
		{
			Reset ();

			_dataBuffer = data;
			_dataPos = 0;

			var result = new TypedObject ("Invoke");
			if (_dataBuffer [0] == 0x00) {
				_dataPos++;
				result.Add ("version", 0x00);
			}
			result.Add ("result", DecodeAMF0 ());
			result.Add ("invokeId", DecodeAMF0 ());
			result.Add ("serviceCall", DecodeAMF0 ());
			result.Add ("data", DecodeAMF0 ());

			if (_dataPos != _dataBuffer.Length) {
				var message = "Did not consume entire buffer: " + _dataPos + " of " + _dataBuffer.Length;
				throw new Exception (message);
			}

			return result;
		}

		private byte ReadByte ()
		{
			byte ret = _dataBuffer [_dataPos];
			_dataPos++;
			return ret;
		}

		private int ReadByteAsInt ()
		{
			int ret = ReadByte ();
			if (ret < 0) {
				ret += 256;
			}
			return ret;
		}

		private byte[] ReadBytes (int length)
		{
			byte[] ret = new byte[length];
			for (int i = 0; i < length; i++) {
				ret [i] = _dataBuffer [_dataPos];
				_dataPos++;
			}
			return ret;
		}

		private double ReadDouble ()
		{
			long value = 0;
			for (int i = 0; i < 8; i++) {
				value = (value << 8) + ReadByteAsInt ();
			}

			return BitConverter.Int64BitsToDouble (value);
		}

		private int ReadIntAMF0 ()
		{
			return (int)ReadDouble ();
		}

		private string ReadStringAMF0 ()
		{
			int length = (ReadByteAsInt () << 8) + ReadByteAsInt ();
			if (length == 0) {
				return "";
			}

			var data = ReadBytes (length);
			return Encoding.UTF8.GetString (data);
		}

		private TypedObject ReadObjectAMF0 ()
		{
			var body = new TypedObject ("Body");
			string key;
			while (!(key = ReadStringAMF0 ()).Equals ("")) {
				byte b = ReadByte ();
				if (b == 0x00) {
					body.Add (key, ReadDouble ());
				} else if (b == 0x02) {
					body.Add (key, ReadStringAMF0 ());
				} else if (b == 0x05) {
					body.Add (key, null);
				} else {
					var message = "AMF0 type not supported: " + b;
					throw new NotImplementedException (message);
				}
			}
			ReadByte (); // Skip object end marker

			return body;
		}

		private int ReadInt ()
		{
			int ret = ReadByteAsInt ();
			int tmp;

			if (ret < 128) {
				return ret;
			} else {
				ret = (ret & 0x7f) << 7;
				tmp = ReadByteAsInt ();
				if (tmp < 128) {
					ret = ret | tmp;
				} else {
					ret = (ret | tmp & 0x7f) << 7;
					tmp = ReadByteAsInt ();
					if (tmp < 128) {
						ret = ret | tmp;
					} else {
						ret = (ret | tmp & 0x7f) << 8;
						tmp = ReadByteAsInt ();
						ret = ret | tmp;
					}
				}
			}

			// Sign extend
			int mask = 1 << 28;
			int r = -(ret & mask) | ret;
			return r;
		}

		private string ReadString ()
		{
			var handle = ReadInt ();
			var inline = ((handle & 1) != 0);
			handle = handle >> 1;

			if (inline) {
				if (handle == 0) {
					return "";
				}

				var data = ReadBytes (handle);
				var str = Encoding.UTF8.GetString (data);

				_stringReferences.Add (str);

				return str;
			} else {
				return _stringReferences [handle];
			}
		}

		private string ReadXML ()
		{
			throw new NotImplementedException ("Reading of XML is not implemented");
		}

		private DateTime ReadDate ()
		{
			var handle = ReadInt ();
			var inline = ((handle & 1) != 0);
			handle = handle >> 1;

			if (inline) {
				var ms = (long)ReadDouble ();
				var d = new DateTime (ms);

				_objectReferences.Add (d);

				return d;
			} else {
				return (DateTime)_objectReferences [handle];
			}
		}

		private object[] ReadArray ()
		{
			var handle = ReadInt ();
			var inline = ((handle & 0x1) != 0);
			handle = handle >> 1;

			if (inline) {
				var key = ReadString ();
				if (key != null && !key.Equals ("")) {
					throw new NotImplementedException ("Associative arrays are not supported");
				}
				var ret = new object[handle];

				_objectReferences.Add (ret);

				for (int i = 0; i < handle; i++) {
					ret [i] = Decode ();
				}

				return ret;
			} else {
				return (object[])_objectReferences [handle];
			}
		}
		// TODO: figure out if we really need to enforce `Int32` type here or
		//	can safely use the `int` alias
		private List<Int32> ReadFlags ()
		{
			var flags = new List<Int32> ();
			int flag;
			do {
				flag = ReadByteAsInt ();
				flags.Add (flag);
			} while ((flag & 0x80) != 0);

			return flags;
		}

		private byte[] ReadByteArray ()
		{
			var handle = ReadInt ();
			var inline = ((handle & 0x1) != 0);
			handle = handle >> 1;

			if (inline) {
				var ret = ReadBytes (handle);

				_objectReferences.Add (ret);

				return ret;
			} else {
				return (byte[])_objectReferences [handle];
			}
		}

		private string ReadXMLString ()
		{
			throw new NotImplementedException ("Reading of XML strings is not implemented");
		}

		private void ReadRemaining (int flag, int bits)
		{
			// For forwards compatibility, read in any other flagged objects to
			// preserve the integrity of the input stream...
			if ((flag >> bits) != 0) {
				for (int o = bits; o < 6; o++) {
					if (((flag >> o) & 1) != 0) {
						Decode ();
					}
				}
			}
		}

		private string ByteArrayToID (byte[] data)
		{
			var ret = new StringBuilder ();
			for (int i = 0; i < data.Length; i++) {
				if (i == 4 || i == 6 || i == 8 || i == 10) {
					ret.Append ("-");
				}
				ret.Append (String.Format ("{0:X2}", data [i]));
			}

			return ret.ToString ();
		}

		private TypedObject ReadDSA ()
		{
			var ret = new TypedObject ("DSA");

			int flag;
			var flags = ReadFlags ();
			for (int i = 0; i < flags.Count; i++) {
				flag = flags [i];
				var bits = 0;
				if (i == 0) {
					if ((flag & 0x01) != 0)
						ret.Add ("body", Decode ());
					if ((flag & 0x02) != 0)
						ret.Add ("clientId", Decode ());
					if ((flag & 0x04) != 0)
						ret.Add ("destination", Decode ());
					if ((flag & 0x08) != 0)
						ret.Add ("headers", Decode ());
					if ((flag & 0x10) != 0)
						ret.Add ("messageId", Decode ());
					if ((flag & 0x20) != 0)
						ret.Add ("timeStamp", Decode ());
					if ((flag & 0x40) != 0)
						ret.Add ("timeToLive", Decode ());
					bits = 7;
				} else if (i == 1) {
					if ((flag & 0x01) != 0) {
						ReadByte ();
						var temp = ReadByteArray ();
						ret.Add ("clientIdBytes", temp);
						ret.Add ("clientId", ByteArrayToID (temp));
					}
					if ((flag & 0x02) != 0) {
						ReadByte ();
						var temp = ReadByteArray ();
						ret.Add ("messageIdBytes", temp);
						ret.Add ("messageId", ByteArrayToID (temp));
					}
					bits = 2;
				}

				ReadRemaining (flag, bits);
			}

			flags = ReadFlags ();
			for (int i = 0; i < flags.Count; i++) {
				flag = flags [i];
				var bits = 0;

				if (i == 0) {
					if ((flag & 0x01) != 0)
						ret.Add ("correlationId", Decode ());
					if ((flag & 0x02) != 0) {
						ReadByte ();
						var temp = ReadByteArray ();
						ret.Add ("correlationIdBytes", temp);
						ret.Add ("correlationId", ByteArrayToID (temp));
					}
					bits = 2;
				}

				ReadRemaining (flag, bits);
			}

			return ret;
		}

		private TypedObject ReadDSK ()
		{
			// DSK is just a DSA + extra set of flags/objects
			var ret = ReadDSA ();
			ret.Type = "DSK";

			var flags = ReadFlags ();
			for (int i = 0; i < flags.Count; i++) {
				ReadRemaining (flags [i], 0);
			}

			return ret;
		}

		private object ReadObject ()
		{
			var handle = ReadInt ();
			var inline = ((handle & 1) != 0);
			handle = handle >> 1;

			if (inline) {
				var inlineDefine = ((handle & 1) != 0);
				handle = handle >> 1;

				ClassDefinition cd;
				if (inlineDefine) {
					cd = new ClassDefinition ();
					cd.Type = ReadString ();

					cd.Externalizable = ((handle & 1) != 0);
					handle = handle >> 1;
					cd.Dynamic = ((handle & 1) != 0);
					handle = handle >> 1;

					for (int i = 0; i < handle; i++) {
						cd.Members.Add (ReadString ());
					}

					_classDefinitions.Add (cd);
				} else {
					cd = _classDefinitions [handle];
				}

				var ret = new TypedObject (cd.Type);

				// Need to add reference here due to circular references
				_objectReferences.Add (ret);

				if (cd.Externalizable) {
					if (cd.Type.Equals ("DSK")) {
						ret = ReadDSK ();
					} else if (cd.Type.Equals ("DSA")) {
						ret = ReadDSA ();
					} else if (cd.Type.Equals ("flex.messaging.io.ArrayCollection")) {
						var obj = Decode ();
						ret = TypedObject.MakeArrayCollection ((Object[])obj);
					} else if (cd.Type.Equals ("com.riotgames.platform.systemstate.ClientSystemStatesNotification") ||
					           cd.Type.Equals ("com.riotgames.platform.broadcast.BroadcastNotification")) {
						var size = 0;
						for (int i = 0; i < 4; i++) {
							size = size * 256 + ReadByteAsInt ();
						}

						var json = Encoding.UTF8.GetString (ReadBytes (size));
						ret = (TypedObject)JsonConvert.DeserializeObject (json);
						ret.Type = cd.Type;
					} else {
						var message = "Externalizable not handled for " + cd.Type;
						throw new Exception (message);
					}
				} else {
					for (int i = 0; i < cd.Members.Count; i++) {
						var key = cd.Members [i];
						var value = Decode ();
						ret.Add (key, value);
					}

					if (cd.Dynamic) {
						string key;
						while ((key = ReadString ()).Length != 0) {
							var value = Decode ();
							ret.Add (key, value);
						}
					}
				}

				return ret;
			} else {
				return _objectReferences [handle];
			}
		}

		private object Decode ()
		{
			byte type = ReadByte ();
			switch (type) {
			case 0x00:
				throw new Exception ("Undefined data type");
			case 0x01:
				return null;
			case 0x02:
				return false;
			case 0x03:
				return true;
			case 0x04:
				return ReadInt ();
			case 0x05:
				return ReadDouble ();
			case 0x06:
				return ReadString ();
			case 0x07:
				return ReadXML ();
			case 0x08:
				return ReadDate ();
			case 0x09:
				return ReadArray ();
			case 0x0A:
				return ReadObject ();
			case 0x0B:
				return ReadXMLString ();
			case 0x0C:
				return ReadByteArray ();
			}

			var message = "Unexpected AMF3 data type: " + type;
			throw new Exception (message);
		}

		private object DecodeAMF0 ()
		{
			int type = ReadByte ();

			switch (type) {
			case 0x00:
				return ReadIntAMF0 ();
			case 0x02:
				return ReadStringAMF0 ();
			case 0x03:
				return ReadObjectAMF0 ();
			case 0x05:
				return null;
			case 0x11: // AMF3
				return Decode ();			
			}

			var message = String.Format ("AMF0 type not supported: {0:X2}", type);
			throw new NotImplementedException (message);
		}
	}
}

