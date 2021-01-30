using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Spelunky2EventLogger
{
    /// <remarks>
    /// Adapted from https://www.codeproject.com/articles/716227/csharp-how-to-scan-a-process-memory
    /// </remarks>
    public class MemoryScanner : IDisposable
    {
        // REQUIRED CONSTS

        const int PROCESS_QUERY_INFORMATION = 0x0400;
        const int MEM_COMMIT = 0x00001000;
        const int PAGE_READWRITE = 0x04;
        const int PROCESS_WM_READ = 0x0010;

        // REQUIRED METHODS

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ReadProcessMemory(
            IntPtr hProcess,
            IntPtr lpBaseAddress,
            byte[] lpBuffer,
            Int32 nSize,
            out IntPtr lpNumberOfBytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ReadProcessMemory(
            IntPtr hProcess,
            IntPtr lpBaseAddress,
            IntPtr lpBuffer,
            Int32 nSize,
            out IntPtr lpNumberOfBytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern void GetSystemInfo(out SYSTEM_INFO lpSystemInfo);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern int VirtualQueryEx(IntPtr hProcess, IntPtr lpAddress, out MEMORY_BASIC_INFORMATION lpBuffer, uint dwLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern int VirtualQueryEx(IntPtr hProcess, IntPtr lpAddress, out MEMORY_BASIC_INFORMATION64 lpBuffer, uint dwLength);

        private static int VirtualQueryEx(IntPtr hProcess, IntPtr lpAddress, out MEMORY_BASIC_INFORMATION lpBuffer)
        {
            return VirtualQueryEx(hProcess, lpAddress, out lpBuffer, (uint) Marshal.SizeOf<MEMORY_BASIC_INFORMATION>());
        }

        private static int VirtualQueryEx64(IntPtr hProcess, IntPtr lpAddress, out MEMORY_BASIC_INFORMATION64 lpBuffer)
        {
            return VirtualQueryEx(hProcess, lpAddress, out lpBuffer, (uint)Marshal.SizeOf<MEMORY_BASIC_INFORMATION64>());
        }

        [DllImport("kernel32.dll")]
        private static extern int GetLastError();

        // REQUIRED STRUCTS

        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORY_BASIC_INFORMATION
        {
            public IntPtr BaseAddress;
            public IntPtr AllocationBase;
            public uint AllocationProtect;
            public IntPtr RegionSize;
            public uint State;
            public uint Protect;
            public uint Type;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORY_BASIC_INFORMATION64
        {
            public ulong BaseAddress;
            public ulong AllocationBase;
            public int AllocationProtect;
            public int __alignment1;
            public ulong RegionSize;
            public int State;
            public int Protect;
            public int Type;
            public int __alignment2;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SYSTEM_INFO
        {
            public ushort processorArchitecture;
            ushort reserved;
            public uint pageSize;
            public IntPtr minimumApplicationAddress;
            public IntPtr maximumApplicationAddress;
            public IntPtr activeProcessorMask;
            public uint numberOfProcessors;
            public uint processorType;
            public uint allocationGranularity;
            public ushort processorLevel;
            public ushort processorRevision;
        }

        private readonly SYSTEM_INFO _systemInfo;
        private IntPtr _processHandle;

        public MemoryScanner(Process process)
        {
            GetSystemInfo(out _systemInfo);
            _processHandle = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_WM_READ, false, process.Id);

            if (_processHandle == IntPtr.Zero)
            {
                throw new Exception($"Unable to open process: 0x{GetLastError():x8}.");
            }
        }

        private static int NextPowerOf2(int value)
        {
            int i;
            for (i = 1; i < value; i <<= 1) ;
            return i;
        }

        private static int FindBytes(byte[] needle, byte[] haystack, int haystackLength, int alignment = 1)
        {
            var lastIndex = haystackLength - needle.Length + 1;

            for (var i = 0; i < lastIndex; i += alignment)
            {
                var match = true;

                for (var j = 0; j < needle.Length; ++j)
                {
                    if (needle[j] != haystack[i + j])
                    {
                        match = false;
                        break;
                    }
                }

                if (match) return i;
            }

            return -1;
        }

        public IEnumerable<IntPtr> FindString(string str, Encoding encoding = null, int alignment = 1)
        {
            return FindBytes((encoding ?? Encoding.ASCII).GetBytes(str), alignment);
        }

        public IEnumerable<IntPtr> FindUInt32(uint value, int alignment = 1)
        {
            return FindBytes(BitConverter.GetBytes(value), alignment);
        }

        public IEnumerable<IntPtr> FindUInt64(ulong value, int alignment = 1)
        {
            return FindBytes(BitConverter.GetBytes(value), alignment);
        }

        public IEnumerable<IntPtr> FindBytes(byte[] bytes, int alignment = 1)
        {

            var minAddress = _systemInfo.minimumApplicationAddress.ToInt64();
            var maxAddress = _systemInfo.maximumApplicationAddress.ToInt64();

            byte[] buffer = null;

            while (minAddress < maxAddress)
            {
                if (VirtualQueryEx(_processHandle, (IntPtr) minAddress, out var memBasicInfo) == 0)
                {
                    throw new Exception($"VirtualQueryEx error: 0x{GetLastError():x8}.");
                }

                if (memBasicInfo.Protect == PAGE_READWRITE && memBasicInfo.State == MEM_COMMIT)
                {
                    if (buffer == null || buffer.Length < (int) memBasicInfo.RegionSize)
                    {
                        buffer = new byte[NextPowerOf2((int) memBasicInfo.RegionSize)];
                    }

                    if (!ReadProcessMemory(_processHandle, (IntPtr) minAddress, buffer, (int) memBasicInfo.RegionSize,
                        out var numBytesRead))
                    {
                        throw new Exception($"ReadProcessMemory error: 0x{GetLastError():x8}.");
                    }

                    var offset = FindBytes(bytes, buffer, numBytesRead.ToInt32());

                    if (offset != -1)
                    {
                        yield return memBasicInfo.BaseAddress + offset;
                    }
                }

                if (memBasicInfo.RegionSize.ToInt64() == 0)
                {
                    break;
                }

                minAddress = memBasicInfo.BaseAddress.ToInt64() + memBasicInfo.RegionSize.ToInt64();
            }
        }

        public bool ReadStructure<T>(IntPtr address, out T buffer)
            where T : struct
        {
            var size = Marshal.SizeOf<T>();
            var structPtr = Marshal.AllocHGlobal(size);

            try
            {
                if (!ReadProcessMemory(_processHandle, address, structPtr, size, out var numBytesRead) || numBytesRead.ToInt64() < size)
                {
                    buffer = default;
                    return false;
                }

                buffer = Marshal.PtrToStructure<T>(structPtr);
                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(structPtr);
            }
        }

        public void Dispose()
        {
            if (_processHandle == IntPtr.Zero)
            {
                return;
            }

            CloseHandle(_processHandle);
            _processHandle = IntPtr.Zero;
        }
    }
}
