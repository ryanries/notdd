// notdd v1.0 - Writes bytes to raw disk sectors.
// Written by Ryan Ries 2014 - myotherpcisacloud.com

using System;
using System.Collections.Generic;
using System.Management;
using System.Reflection;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace notdd
{
    class notdd
    {
        private enum WinErrorCodes : int
        {
            ERROR_RESOURCE_NOT_PRESENT = 0x10DC,
            ERROR_BAD_ARGUMENTS = 0xA0,
            ERROR_INVALID_NAME = 0x7B,
            ERROR_WMI_DP_FAILED = 0x1071,
            ERROR_DEV_NOT_EXIST = 0x37,
            ERROR_FILE_TOO_LARGE = 0xDF,
            ERROR_INVALID_BLOCK = 0x9,
            ERROR_READ_FAULT = 0x1E,
            ERROR_WRITE_FAULT = 0x1D
        };

        static void Main(string[] args)
        {
            ManagementScope scope = new ManagementScope(@"\\.\root\CIMv2");
            SelectQuery query = new SelectQuery("SELECT * FROM Win32_DiskDrive");
            List<WritablePhysicalDevice> allDevices = new List<WritablePhysicalDevice>();
            WritablePhysicalDevice myDevice = null;
            bool force = false;
            bool listOnly = false;
            int devNum = -1;
            byte[] imageBytes = null;
            string imageFile = string.Empty;
            UInt64 startingsector = UInt64.MaxValue;
            uint bytesWritten = 0;
            string helpText = HelpText.Print();

            if (helpText.Length < 1)
            {
                Environment.ExitCode = (int)WinErrorCodes.ERROR_RESOURCE_NOT_PRESENT;
                return;
            }

            if (args.Length < 1)
            {
                Console.WriteLine(helpText);
                Environment.ExitCode = (int)WinErrorCodes.ERROR_BAD_ARGUMENTS;
                return;
            }
            foreach (string arg in args)
            {
                string formattedArg = arg.ToLower().Trim();
                if (formattedArg == "force")
                {
                    force = true;
                    continue;
                }
                if (formattedArg == "list")
                {
                    listOnly = true;
                    break;
                }
                if (!formattedArg.Contains("=") || formattedArg.Split('=').Length != 2)
                {
                    Console.WriteLine(helpText);
                    Environment.ExitCode = (int)WinErrorCodes.ERROR_BAD_ARGUMENTS;
                    return;
                }
                if (formattedArg.Split('=')[0] == "image")
                {
                    imageFile = formattedArg.Split('=')[1];
                    if (!File.Exists(imageFile))
                    {
                        Environment.ExitCode = (int)WinErrorCodes.ERROR_INVALID_NAME;
                        Console.WriteLine("Error: The supplied image path does not exist.");
                        return;
                    }
                }
                else if (formattedArg.Split('=')[0] == "device")
                {
                    if (!int.TryParse(formattedArg.Split('=')[1], out devNum))
                    {
                        Environment.ExitCode = (int)WinErrorCodes.ERROR_BAD_ARGUMENTS;
                        Console.WriteLine("Error: Could not validate device number argument.");
                        return;
                    }
                }
                else if (formattedArg.Split('=')[0] == "startingsector")
                {
                    if (!UInt64.TryParse(formattedArg.Split('=')[1], out startingsector))
                    {
                        Environment.ExitCode = (int)WinErrorCodes.ERROR_BAD_ARGUMENTS;
                        Console.WriteLine("Error: Could not validate startingsector argument.");
                        return;
                    }
                }
                else
                {
                    Console.WriteLine(helpText);
                    Environment.ExitCode = (int)WinErrorCodes.ERROR_BAD_ARGUMENTS;
                    Console.WriteLine("Error: Invalid argument.");
                    return;
                }
            }
            if ((devNum < 0 || imageFile.Length < 1) & !listOnly)
            {
                Console.WriteLine(helpText);
                Environment.ExitCode = (int)WinErrorCodes.ERROR_BAD_ARGUMENTS;
                return;
            }
            
            try
            {                
                scope.Connect();
            }
            catch (Exception ex)
            {
                Environment.ExitCode = (int)WinErrorCodes.ERROR_WMI_DP_FAILED;
                Console.WriteLine("Error: " + ex.Message);
                return;
            }

            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(scope, query))
                using (ManagementObjectCollection queryCollection = searcher.Get())
                {
                    foreach (ManagementObject mObject in queryCollection)
                    {
                        // 4 = Supports Writing - Win32_DiskDrive: msdn.microsoft.com/en-us/library/aa394132(v=vs.85).aspx
                        UInt16[] capabilities = (UInt16[])mObject["Capabilities"];
                        if (capabilities.Contains((UInt16)4) & mObject["DeviceID"].ToString().Contains("PHYSICALDRIVE"))                        
                            allDevices.Add(new WritablePhysicalDevice((string)mObject["DeviceID"], (UInt32)mObject["BytesPerSector"], (UInt64)mObject["TotalSectors"], (UInt64)mObject["Size"]));                        
                    }
                }                
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
                Environment.ExitCode = (int)WinErrorCodes.ERROR_WMI_DP_FAILED;
                return;
            }            
            
            if (listOnly)
            {
                foreach (WritablePhysicalDevice dev in allDevices)
                {
                    Console.WriteLine("\n     DeviceID: " + dev.deviceID);
                    Console.WriteLine(" Bytes/Sector: " + dev.bytesPerSector);
                    Console.WriteLine("Total Sectors: " + dev.totalSectors);
                    Console.WriteLine("   Total Size: " + dev.size);
                }
                return;
            }

            foreach (WritablePhysicalDevice dev in allDevices)            
                if (dev.deviceNum == devNum)                
                    myDevice = dev;
            
            if (myDevice == null)
            {
                Environment.ExitCode = (int)WinErrorCodes.ERROR_DEV_NOT_EXIST;
                Console.WriteLine("Error: Device number " + devNum + " does not exist!");
                return;
            }

            if (startingsector > myDevice.totalSectors)
            {
                Environment.ExitCode = (int)WinErrorCodes.ERROR_INVALID_BLOCK;
                Console.WriteLine("Error: Starting sector was greater than drive's total sectors!");
                return;
            }

            if (!force)
            {
                Console.WriteLine("\nPress Y if you are sure you want to write the data in");
                Console.WriteLine(imageFile);
                Console.Write("to " + myDevice.deviceID + " starting at sector " + startingsector + ": ");
                if (Console.ReadKey().Key != ConsoleKey.Y)
                    return;

                Console.WriteLine();
            }

            try
            {
                imageBytes = File.ReadAllBytes(imageFile);
                Console.WriteLine("Read " + imageBytes.Length + " bytes from source image.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error reading source file: " + ex.Message);
                Environment.ExitCode = (int)WinErrorCodes.ERROR_READ_FAULT;
                return;
            }

            if (imageBytes.Length % myDevice.bytesPerSector != 0)
            {
                Console.WriteLine("The image provided is not a multiple of " + myDevice.bytesPerSector + " bytes.");
                Console.WriteLine("Data will be padded with zeroes to the nearest sector...");
                do
                {
                    byte[] newArray = new byte[imageBytes.Length + 1];
                    imageBytes.CopyTo(newArray, 0);
                    newArray[newArray.Length - 1] = 0;
                    imageBytes = newArray;                    
                }
                while (imageBytes.Length % myDevice.bytesPerSector != 0);
                Console.WriteLine("Adjusted image size is " + imageBytes.Length + " bytes.");
            }

            if ((ulong)imageBytes.Length > (myDevice.size - (startingsector * myDevice.bytesPerSector)))
            {
                Console.WriteLine("Error: Image is larger than the space available on the drive!");
                Environment.ExitCode = (int)WinErrorCodes.ERROR_FILE_TOO_LARGE;
                return;
            }

            Console.WriteLine("Opening handle to " + myDevice.deviceID + "...");
            try
            {
                using (SafeFileHandle safeHandle = NativeMethods.CreateFile(myDevice.deviceID, FileAccess.ReadWrite, FileShare.ReadWrite, IntPtr.Zero, FileMode.Open, FileAttributes.Normal, IntPtr.Zero))
                {
                    if (safeHandle.IsClosed || safeHandle.IsInvalid)
                        throw new Exception("Device handle could not be opened! (Check Administrator privileges.)");
                    
                    Console.WriteLine("Seeking to sector " + startingsector + " (byte " + startingsector * myDevice.bytesPerSector + ")...");
                    unsafe
                    {
                        UInt64 n = 0;
                        IntPtr ptr = new IntPtr(&n);
                        int ret = 0;

                        NativeMethods.SetFilePointerEx(safeHandle, (long)(startingsector * myDevice.bytesPerSector), ptr, NativeMethods.EMoveMethod.Begin);
                        ret = Marshal.GetLastWin32Error();
                        if (ret != 0)
                            throw new Exception("Error during file seek! Win32 code: " + ret);

                        var nativeOverlap = new System.Threading.NativeOverlapped();
                        NativeMethods.WriteFile(safeHandle, imageBytes, (uint)imageBytes.Length, out bytesWritten, ref nativeOverlap);
                        ret = Marshal.GetLastWin32Error();
                        if (ret != 0)
                            throw new Exception("Error during file write! Win32 code: " + ret);
                        
                        Console.WriteLine(bytesWritten + " bytes were written to " + myDevice.deviceID + ".");
                    }
                }
            }
            catch (Exception ex)
            {
                Environment.ExitCode = (int)WinErrorCodes.ERROR_WRITE_FAULT;
                Console.WriteLine("Error: " + ex.Message);
            }
        }
    }
    
    class WritablePhysicalDevice
    {
        public string deviceID;
        public int deviceNum;
        public UInt32 bytesPerSector;
        public UInt64 totalSectors;
        public UInt64 size;

        public WritablePhysicalDevice(string deviceID, UInt32 bytesPerSector, UInt64 totalSectors, UInt64 size)
        {
            this.deviceID = deviceID;
            this.bytesPerSector = bytesPerSector;
            this.totalSectors = totalSectors;
            this.size = size;
            this.deviceNum = int.Parse(deviceID.Split('E')[1].Trim());
        }
    }

    class NativeMethods
    {
        public enum EMoveMethod : uint
        {
            Begin = 0,
            Current = 1,
            End = 2
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WriteFile(
            SafeFileHandle hFile,
            byte[] lpBuffer,
            uint nNumberOfBytesToWrite,
            out uint lpNumberOfBytesWritten,
            [In] ref System.Threading.NativeOverlapped lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern bool SetFilePointerEx(
            SafeFileHandle hFile,
            long liDistanceToMove,
            IntPtr lpNewFilePointer,
            EMoveMethod dwMoveMethod);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto, ThrowOnUnmappableChar = true)]
        public static extern SafeFileHandle CreateFile(
            [MarshalAs(UnmanagedType.LPWStr)] string lpFileName,
            [MarshalAs(UnmanagedType.U4)] FileAccess dwDesiredAccess,
            [MarshalAs(UnmanagedType.U4)] FileShare dwShareMode,
            IntPtr lpSecurityAttributes,
            [MarshalAs(UnmanagedType.U4)] FileMode dwCreationDisposition,
            [MarshalAs(UnmanagedType.U4)] FileAttributes dwFlagsAndAttributes,
            IntPtr hTemplateFile);
    }

    static class HelpText
    {
        public static string Print()
        {
            return @"
notdd v1.0 - Writes bytes to raw disk sectors.
Written by Ryan Ries 2014 - myotherpcisacloud.com

Usage: C:\>notdd image=<image>
              device=<physical_device_number>
              startingsector=<starting_sector>
              [force]
              [list]

Example:
  C:\>notdd image=C:\Data\image.bin device=0 startingsector=0
  Writes the image.bin file to \\.\PHYSICALDRIVE0 starting at sector 0.

Example:
  C:\>notdd image=""C:\Long Path\image.bin"" device=0 startingsector=0
  If the image file path has spaces in it, use quotation marks.

Example:
  C:\>notdd image=C:\Data\image.bin device=1 startingsector=0 force
  Use the force parameter to not be asked for confirmation.

Example:
  C:\>notdd list
  List available, writable physical devices.

WARNING: This program can ruin the existing formatting and 
filesystem of the drive!";
        }
    }
}
