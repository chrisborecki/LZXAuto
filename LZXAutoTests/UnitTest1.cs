using LZXAutoEngine;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Concurrent;

namespace LZXAutoTests
{
	[TestClass]
	public class UnitTest1
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

		[TestMethod]
		public void WriteDict()
		{
			string fileName = "test.db";
			LZXAutoEngine.LZXAutoEngine eng = new LZXAutoEngine.LZXAutoEngine();
			ConcurrentDictionary<int, uint> dict1 = new ConcurrentDictionary<int, uint>();
			dict1[1234] = 12345;
			dict1[12345] = 123456;
			eng.SaveDictToFile(fileName, dict1);

			ConcurrentDictionary<int, uint> dict2 = eng.LoadDictFromFile(fileName);
			Assert.IsTrue(dict1[1234] == dict2[12345]);
			Assert.IsTrue(dict1[12345] == dict2[123456]);
		}
	}
}
