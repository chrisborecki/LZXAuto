using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace LZXCompactLightEngine
{
    public static class DriveUtils
    {
        private static readonly string[] BinaryPrefix = { "bytes", "KB", "MB", "GB", "TB" }; // , "PB", "EB", "ZB", "YB"

        private static uint lpSectorsPerCluster, lpBytesPerSector, lpNumberOfFreeClusters, lpTotalNumberOfClusters;


        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern bool GetDiskFreeSpace(string lpRootPathName, out uint lpSectorsPerCluster, out uint lpBytesPerSector, out uint lpNumberOfFreeClusters, out uint lpTotalNumberOfClusters);

        private static void ReadDiskParams(string path)
        {
            GetDiskFreeSpace(Path.GetPathRoot(path), out lpSectorsPerCluster, out lpBytesPerSector, out lpNumberOfFreeClusters, out lpTotalNumberOfClusters);
        }

        public static long GetDriveFreeSpace(string path)
        {
            System.IO.DriveInfo d = new DriveInfo(Path.GetPathRoot(path));
            return d.AvailableFreeSpace;
        }

        public static long GetDriveCapacity(string path)
        {
            System.IO.DriveInfo d = new DriveInfo(Path.GetPathRoot(path));
            return d.TotalSize;
        }

        public static long GetDriveUsedSpace(string path)
        {
            return GetDriveCapacity(path) - GetDriveFreeSpace(path);
        }

        public static ulong GetDiskOccupiedSpace(ulong logicalFileSize, string path)
        {
            if (lpBytesPerSector == 0)
                ReadDiskParams(path);

            ulong clustersize = lpBytesPerSector * lpSectorsPerCluster;
            return (ulong)(clustersize * (((ulong)logicalFileSize + clustersize - 1) / clustersize));
        }

        [DllImport("kernel32.dll")]
        private static extern uint GetCompressedFileSizeW([In, MarshalAs(UnmanagedType.LPWStr)] string lpFileName, [Out, MarshalAs(UnmanagedType.U4)] out uint lpFileSizeHigh);

        public static ulong GetPhysicalFileSize(string fileName)
        {
            uint high, low;
            low = GetCompressedFileSizeW(fileName, out high);
            int error = Marshal.GetLastWin32Error();
            if (low == 0xFFFFFFFF && error != 0)
                return 0;
                //throw new Win32Exception(error, "Error while getting physical file size");
            else
                return ((ulong)high << 32) + low;
        }

        public static string GetMemoryString(this long bytes)
        {
            int index = 0;
            double value = bytes;
            string text;
            do
            {
                text = value.ToString("0.0") + " " + BinaryPrefix[index];
                value /= 1024;
                index++;
            }
            while (Math.Floor(value) > 0 && index < BinaryPrefix.Length);
            return text;
        }

        public static string GetMemoryString(this ulong bytes)
        {
            return GetMemoryString((long)bytes);
        }
    }
}