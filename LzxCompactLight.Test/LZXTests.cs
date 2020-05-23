using System;
using LZXCompactLightEngine;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LzxCompactLight.Test
{
	[TestClass]
	public class LZXTests
	{
		[TestMethod]
		public void PhysicalFileSize()
		{
			ulong fSize = DriveUtils.GetPhysicalFileSize("c:\\Temp\\log20200312.txt");

			Assert.IsTrue(fSize > 0);
		}

		[TestMethod]
		public void GetDiskOccupiedSpace()
		{
			ulong fSize = DriveUtils.GetDiskOccupiedSpace(2323, "c:\\Temp\\log20200312.txt");

			Assert.IsTrue(fSize > 0);
		}
	}
}
