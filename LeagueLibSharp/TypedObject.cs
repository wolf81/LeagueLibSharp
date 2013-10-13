using System;
using System.Collections.Generic;

namespace LeagueRTMPSSharp
{
	public class TypedObject : Dictionary<string, object>
	{
		public string Type { get; private set; }

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
	}
}

