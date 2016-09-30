using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace UltraHook
{
	public class UltraHookException : Exception
	{
		public UltraHookException()
			: base()
		{
		}

		public UltraHookException(string message)
			: base(message)
		{
		}

		public UltraHookException(string message, Exception innerException)
			: base(message, innerException)
		{
		}

		public UltraHookException(SerializationInfo info, StreamingContext context)
			: base(info, context)
		{
		}
	}
}
