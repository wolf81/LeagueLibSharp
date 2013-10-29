using System;
using System.Collections.Generic;

namespace LeagueRTMPSSharp
{
	public class ClassDefinition
	{
		public string Type { get; set; }

		public bool Externalizable { get; set; }

		public bool Dynamic { get; set; }

		public List<String> Members = new List<String> ();

		public ClassDefinition ()
		{
			Dynamic = false;
			Externalizable = false;
		}
	}
}

