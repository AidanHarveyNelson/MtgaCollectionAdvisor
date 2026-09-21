using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MtgaCollectionAdvisor.Core.Memory;

public sealed record MemoryRegion(long BaseAddress, long Size);

/// <summary>
/// Read-only access to another process's memory (the running MTGA client), used to
/// locate the collection table that the current client no longer writes to Player.log.
/// </summary>
public sealed class ProcessMemoryReader : IDisposable
{
    private readonly IntPtr _handle;

    public int ProcessId { get; }

    private ProcessMemoryReader(IntPtr handle, int processId)
    {
        _handle = handle;
        ProcessId = processId;
    }

    public static ProcessMemoryReader Open(string processName)
    {
        var process = Process.GetProcessesByName(processName).FirstOrDefault()
            ?? throw new MemoryScanException(
                $"The \"{processName}\" process is not running. Start MTG Arena before scanning.");

        var handle = NativeMethods.OpenProcess(
            NativeMethods.PROCESS_QUERY_INFORMATION | NativeMethods.PROCESS_VM_READ, false, process.Id);

        if (handle == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            throw new MemoryScanException(
                "Could not open the MTG Arena process for reading " +
                $"({new Win32Exception(error).Message}). Try running this app as Administrator.");
        }

        return new ProcessMemoryReader(handle, process.Id);
    }

    /// <summary>
    /// Committed, writable regions only - the collection lives on the heap, and skipping
    /// read-only/image pages cuts the amount scanned by an order of magnitude.
    /// </summary>
    public IEnumerable<MemoryRegion> EnumerateWritableRegions()
    {
        var address = 0L;
        var infoSize = (IntPtr)Marshal.SizeOf<NativeMethods.MEMORY_BASIC_INFORMATION>();
        const long maxUserAddress = 0x7FFFFFFFFFFF;

        while (address < maxUserAddress)
        {
            if (NativeMethods.VirtualQueryEx(_handle, (IntPtr)address, out var info, infoSize) == IntPtr.Zero)
            {
                break;
            }

            var regionSize = (long)info.RegionSize;
            if (regionSize <= 0) break;

            var isCommitted = info.State == NativeMethods.MEM_COMMIT;
            var isScannableType = info.Type is NativeMethods.MEM_PRIVATE or NativeMethods.MEM_MAPPED;
            var protect = info.Protect;
            var isGuarded = (protect & NativeMethods.PAGE_GUARD) != 0 || (protect & NativeMethods.PAGE_NOACCESS) != 0;
            var isWritable = (protect & (NativeMethods.PAGE_READWRITE | NativeMethods.PAGE_WRITECOPY |
                                         NativeMethods.PAGE_EXECUTE_READWRITE | NativeMethods.PAGE_EXECUTE_WRITECOPY)) != 0;

            if (isCommitted && isScannableType && isWritable && !isGuarded)
            {
                yield return new MemoryRegion((long)info.BaseAddress, regionSize);
            }

            address = (long)info.BaseAddress + regionSize;
        }
    }

    public bool TryRead(long address, byte[] buffer, int count)
    {
        var ok = NativeMethods.ReadProcessMemory(_handle, (IntPtr)address, buffer, (IntPtr)count, out var read);
        return ok && (long)read == count;
    }

    /// <summary>
    /// Reads whatever is readable at <paramref name="address"/>, returning how many bytes
    /// were actually retrieved (0 when the range is not readable at all).
    /// </summary>
    public int ReadPartial(long address, byte[] buffer, int count)
    {
        NativeMethods.ReadProcessMemory(_handle, (IntPtr)address, buffer, (IntPtr)count, out var read);
        return (int)Math.Clamp((long)read, 0, count);
    }

    public void Dispose()
    {
        if (_handle != IntPtr.Zero) NativeMethods.CloseHandle(_handle);
    }
}

public sealed class MemoryScanException(string message) : Exception(message);
