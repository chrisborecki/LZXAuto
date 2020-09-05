using System;
using System.Collections.Generic;
using System.Text;

namespace LZXAutoEngine
{
	public static class ThreadUtils
	{
		public static object lockObject = new object();

		public static void InterlockedAdd(ref ulong variable, ulong value)
		{
			lock (lockObject)
			{
				variable += value;
			}
		}

		public static void InterlockedIncrement(ref ulong variable)
		{
			lock (lockObject)
			{
				variable++;
			}
		}

		public static void InterlockedIncrement(ref uint variable)
		{
			lock (lockObject)
			{
				variable++;
			}
		}


	}
}
