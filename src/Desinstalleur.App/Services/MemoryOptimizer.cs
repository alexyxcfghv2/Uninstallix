using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Desinstalleur.App.Services;

/// <summary>
/// Libère de la mémoire vive : réduit la mémoire de travail des programmes ouverts
/// et vide la liste « en attente » (standby) de Windows. L'effet est temporaire :
/// Windows réutilise ensuite la mémoire libre selon ses besoins.
/// </summary>
public static class MemoryOptimizer
{
    public readonly record struct MemoryStatus(ulong TotalBytes, ulong AvailableBytes)
    {
        public ulong UsedBytes => TotalBytes - AvailableBytes;
        public double UsedPercent => TotalBytes == 0 ? 0 : UsedBytes * 100.0 / TotalBytes;
    }

    public static MemoryStatus GetStatus()
    {
        var info = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref info)
            ? new MemoryStatus(info.ullTotalPhys, info.ullAvailPhys)
            : new MemoryStatus(0, 0);
    }

    private static int _trimming;

    /// <summary>
    /// Allège Uninstallix lui-même : ramasse-miettes complet puis rend à Windows les pages inutilisées.
    /// Appelé quand la fenêtre est réduite et après les gros chargements.
    /// </summary>
    public static void TrimSelfInBackground()
    {
        if (Interlocked.Exchange(ref _trimming, 1) == 1)
            return;
        Task.Run(() =>
        {
            try
            {
                System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                using var self = Process.GetCurrentProcess();
                EmptyWorkingSet(self.Handle);
            }
            catch
            {
                // sans importance : simple optimisation
            }
            finally
            {
                Interlocked.Exchange(ref _trimming, 0);
            }
        });
    }

    /// <summary>Renvoie la mémoire libérée en octets (peut être 0).</summary>
    public static long Optimize()
    {
        var before = GetStatus().AvailableBytes;

        // 1. Réduire la mémoire de travail de chaque processus accessible.
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var handle = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_SET_QUOTA, false, process.Id);
                if (handle != IntPtr.Zero)
                {
                    EmptyWorkingSet(handle);
                    CloseHandle(handle);
                }
            }
            catch
            {
                // processus protégé ou terminé entre-temps
            }
            finally
            {
                process.Dispose();
            }
        }

        // 2. Vider la liste de pages en attente (nécessite les droits administrateur).
        try
        {
            if (EnablePrivilege("SeProfileSingleProcessPrivilege"))
            {
                var command = MemoryPurgeStandbyList;
                NtSetSystemInformation(SystemMemoryListInformation, ref command, sizeof(int));
            }
        }
        catch (Exception ex)
        {
            Journal.Write("Vidage de la mémoire en attente impossible : " + ex.Message);
        }

        var after = GetStatus().AvailableBytes;
        var freed = after > before ? (long)(after - before) : 0;
        Journal.Write($"RAM optimisée : {freed / (1024 * 1024)} Mo libérés.");
        return freed;
    }

    /// <summary>Vide le cache DNS (équivalent de « ipconfig /flushdns »).</summary>
    public static bool FlushDns()
    {
        try
        {
            var ok = DnsFlushResolverCache();
            Journal.Write(ok ? "Cache DNS vidé." : "Échec du vidage du cache DNS.");
            return ok;
        }
        catch (Exception ex)
        {
            Journal.Write("Cache DNS : " + ex.Message);
            return false;
        }
    }

    // ----- Win32 -----

    private const uint PROCESS_QUERY_INFORMATION = 0x0400;
    private const uint PROCESS_SET_QUOTA = 0x0100;
    private const int SystemMemoryListInformation = 80;
    private const int MemoryPurgeStandbyList = 4;
    private const uint SE_PRIVILEGE_ENABLED = 0x2;
    private const uint TOKEN_ADJUST_PRIVILEGES = 0x20;
    private const uint TOKEN_QUERY = 0x8;

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        public LUID Luid;
        public uint Attributes;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int processId);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("psapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyWorkingSet(IntPtr process);

    [DllImport("ntdll.dll")]
    private static extern int NtSetSystemInformation(int infoClass, ref int info, int length);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValue(string? system, string name, out LUID luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TOKEN_PRIVILEGES newState,
        int bufferLength, IntPtr previousState, IntPtr returnLength);

    [DllImport("dnsapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DnsFlushResolverCache();

    private static bool EnablePrivilege(string name)
    {
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out var token))
            return false;
        try
        {
            if (!LookupPrivilegeValue(null, name, out var luid))
                return false;
            var privileges = new TOKEN_PRIVILEGES { PrivilegeCount = 1, Luid = luid, Attributes = SE_PRIVILEGE_ENABLED };
            return AdjustTokenPrivileges(token, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero)
                   && Marshal.GetLastWin32Error() == 0;
        }
        finally
        {
            CloseHandle(token);
        }
    }
}
