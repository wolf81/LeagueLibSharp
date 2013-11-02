using System;
using System.Collections.Generic;

namespace LeagueRTMPSSharp
{
	public class TypedObject : Dictionary<string, object>
	{
		public string Type { get; set; }

		public TypedObject (string type)
		{
			this.Type = type;
		}

		public TypedObject GetTO (String key)
		{
			return (TypedObject)this [key];
		}

		public String GetString (String key)
		{
			return (String)this [key];
		}

		public static TypedObject MakeArrayCollection (Object[] data)
		{
			TypedObject ret = new TypedObject ("flex.messaging.io.ArrayCollection");
			ret.Add ("array", data);
			return ret;
		}

		public Int32? GetInt (String key)
		{
			object val = this [key];

			if (val == null) {
				return null;
			} else if (val is Int32) {
				return (Int32)val;
			} else {
				return Convert.ToInt32 ((Double)val);
			}
		}
	}
}

