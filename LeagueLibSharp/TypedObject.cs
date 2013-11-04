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

namespace LeagueRTMPSSharp
{
	public class TypedObject : Dictionary<string, object>
	{
		public string Type { get; set; }

		public TypedObject () : this(null)
		{
		}

		public TypedObject (string type) : base()
		{
			this.Type = type;
		}

		public TypedObject GetTO (string key)
		{
			return (TypedObject)this [key];
		}

		public string GetString (string key)
		{
			return (string)this [key];
		}

		public static TypedObject MakeArrayCollection (object[] data)
		{
			var ret = new TypedObject ("flex.messaging.io.ArrayCollection");
			ret.Add ("array", data);
			return ret;
		}

		public Int32? GetInt (string key)
		{
			var val = this [key];

			if (val == null) {
				return null;
			} else if (val is Int32) {
				return (Int32)val;
			} else {
				return Convert.ToInt32 ((Double)val);
			}
		}

		public override string ToString ()
		{
			if (Type == null) {
				return base.ToString ();
			} else if (Type.Equals ("flex.messaging.io.ArrayCollection")) {
				var sb = new StringBuilder ();
				object[] data = (object[])this ["array"];

				sb.Append ("ArrayCollection:[");
				for (int i = 0; i < data.Length; i++) {
					sb.Append (data [i]);
					if (i < data.Length - 1) {
						sb.Append (", ");
					}
				}
				sb.Append (']');
				return sb.ToString ();
			} else {
				var builder = new StringBuilder (Type + ":" + base.ToString ());
				foreach (var key in Keys) {
					var val = this [key];
					builder.Append ("\n\t" + key + " : " + val);
				}
				return builder.ToString ();
			}
		}
	}
}

