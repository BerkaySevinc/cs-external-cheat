using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Formatters.Binary;
using System.Security.Policy;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;

namespace BekoS
{
    #region Advanced Process

    public class AdvancedProcess : IDisposable
    {
        public bool IsExists => process is not null && !process.HasExited;

        public string? Name => process?.ProcessName;
        public int? Id => process?.Id;
        public IntPtr? Handle => process?.Handle;
        public ProcessModule[] Modules => process?.Modules.Cast<ProcessModule>().ToArray() ?? Array.Empty<ProcessModule>();

        Bit? _bits;
        public Bit? Bits => _bits ??= GetBits();

        ProcessMemory? _memory;
        public ProcessMemory Memory => _memory ??= new ProcessMemory(process!);

        ProcessOverlay? _overlay;
        public ProcessOverlay Overlay => _overlay ??= new ProcessOverlay(process!, 3000);


        #region Constructer

        private Process? process;
        public AdvancedProcess(Process process)
        {
            TryConnectIfOpen(process);
        }

        public AdvancedProcess(int processId) : this(Process.GetProcessById(processId)) { }

        private string? ctorParameterProcessName;
        public AdvancedProcess(string processName)
            : this(Process.GetProcessesByName(processName.Replace(".exe", "")).FirstOrDefault()!)
        {
            ctorParameterProcessName = processName.Replace(".exe", "");
        }

        public AdvancedProcess() : this(Process.GetCurrentProcess()) { }

        #endregion

        #region Connect / Wait For Open

        public Task<bool> WaitForOpenAsync(int interval)
        {
            if (IsExists) return Task.FromResult(true);

            string? targetName = ctorParameterProcessName ?? process?.ProcessName;
            if (targetName is null) Task.FromResult(false);

            return Task.Run(() =>
            {
                while (true)
                {
                    if (TryConnectIfOpen(targetName!)) break;
                    Thread.Sleep(interval);
                }

                return true;
            });
        }

        public bool TryConnectIfOpen()
        {
            if (IsExists) return true;

            string? targetName = ctorParameterProcessName ?? process?.ProcessName;
            if (targetName is null) return false;

            return TryConnectIfOpen(targetName);
        }

        private bool TryConnectIfOpen(string processName)
        {
            return TryConnectIfOpen(Process.GetProcessesByName(processName).FirstOrDefault());
        }

        private bool TryConnectIfOpen(Process? process)
        {
            this.process = process;
            if (!IsExists) return false;

            return true;
        }

        #endregion

        #region Bring To Front

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        public void BringToFront()
        {
            if (!IsExists || process!.MainWindowHandle == IntPtr.Zero) return;

            SetForegroundWindow(process.MainWindowHandle);
        }

        #endregion

        #region Kill

        public void Kill()
        {
            process?.Kill();

            process?.Dispose();
            process = null;
        }

        #endregion

        #region Dispose

        public void Dispose()
        {
            process?.Dispose();
            _memory?.Dispose();
            _overlay?.Dispose();
        }

        #endregion


        #region Utilities

        #region Process Bits

        [DllImport("kernel32.dll", SetLastError = true, CallingConvention = CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWow64Process([In] IntPtr process, [Out] out bool wow64Process);

        private Bit GetBits()
        {
            return (Environment.Is64BitOperatingSystem && IsWow64Process(process!.Handle, out var lpSystemInfo) && !lpSystemInfo)
                 ? Bit.Bit64
                 : Bit.Bit32;
        }

        public enum Bit
        {
            Bit64,
            Bit32
        }

        #endregion

        #endregion
    }

    #endregion

    #region Process Memory

    public class ProcessMemory : IDisposable
    {

        public bool IsConnected
        {
            get
            {
                try
                {
                    return process is not null && process.Responding && process.MainModule is not null;
                }
                catch
                {
                    return false;
                }
            }
        }


        private Process? process;
        public ProcessMemory(Process process)
        {
            if (!IsAdministrator()) throw new UnauthorizedAccessException("Need Administrator Permissions");

            TryConnectProcess(process);
        }

        public ProcessMemory(int processId) : this(Process.GetProcessById(processId)) { }

        private string? ctorParameterProcessName;
        public ProcessMemory(string processName)
            : this(Process.GetProcessesByName(processName.Replace(".exe", "")).FirstOrDefault()!)
        {
            ctorParameterProcessName = processName.Replace(".exe", "");
        }

        public ProcessMemory() : this(Process.GetCurrentProcess()) { }


        #region Connect Process

        [DllImport("kernel32.dll", SetLastError = true, CallingConvention = CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWow64Process([In] IntPtr process, [Out] out bool wow64Process);


        private bool isProcess64Bit;

        public Task WaitForConnectAsync(int interval)
        {
            if (IsConnected) return Task.FromResult(true);

            string? targetName = ctorParameterProcessName ?? process?.ProcessName;
            if (targetName is null) Task.FromResult(false);

            return Task.Run(() =>
            {
                while (true)
                {
                    if (TryConnectProcess(targetName!)) break;
                    Thread.Sleep(interval);
                }

                return true;
            });
        }

        public bool TryConnectProcess()
        {
            if (IsConnected) return true;

            string? targetName = ctorParameterProcessName ?? process?.ProcessName;
            if (targetName is null) return false;

            return TryConnectProcess(targetName);
        }

        private bool TryConnectProcess(string processName)
        {
            processName = processName.Replace(".exe", "");

            return TryConnectProcess(Process.GetProcessesByName(processName).FirstOrDefault()!);
        }

        private bool TryConnectProcess(Process process)
        {
            this.process = process;
            if (!IsConnected) return false;

            isProcess64Bit = Environment.Is64BitOperatingSystem && IsWow64Process(process!.Handle, out var lpSystemInfo) && !lpSystemInfo;

            return true;
        }

        #endregion


        #region Get Address

        public UIntPtr GetModuleAddressByName(string moduleName)
        {
            if (!IsConnected) return UIntPtr.Zero;

            return (UIntPtr)(ulong)GetModuleAddressByName(process!, moduleName);
        }

        public UIntPtr GetMainModuleAddress()
        {
            if (!IsConnected) return UIntPtr.Zero;

            return (UIntPtr)(ulong)process!.MainModule!.BaseAddress;
        }

        #endregion

        #region Read

        // Read
        public T? Read<T>(string address, bool isOffsetsHexdecimal = false)
        {
            if (!ReaderHelper(address, isOffsetsHexdecimal, out UIntPtr organizedAddress))
                return default;

            return ReadAddress<T>(process!, organizedAddress);
        }
        public T? Read<T>(UIntPtr address)
        {
            if (!IsConnected) return default;

            return ReadAddress<T>(process!, address);
        }

        // Read Bytes
        public byte[]? ReadBytes(string address, int length, bool isOffsetsHexdecimal = false)
        {
            if (!ReaderHelper(address, isOffsetsHexdecimal, out UIntPtr organizedAddress))
                return default;

            return ReadBytesAddress(process!, organizedAddress, length);
        }
        public byte[]? ReadBytes(UIntPtr address, int length)
        {
            if (IsConnected) return default;

            return ReadBytesAddress(process!, address, length);
        }

        // Read String
        public string? ReadString(string address, int bytesLength, bool isOffsetsHexdecimal = false, bool zeroTerminated = true, Encoding encoding = null!)
        {
            if (!ReaderHelper(address, isOffsetsHexdecimal, out UIntPtr organizedAddress))
                return null;

            encoding ??= Encoding.UTF8;

            return ReadStringAddress(process!, organizedAddress, bytesLength, zeroTerminated, encoding);
        }
        public string? ReadString(UIntPtr address, int bytesLength, bool zeroTerminated = true, Encoding encoding = null!)
        {
            if (!IsConnected) return null;

            return ReadStringAddress(process!, address, bytesLength, zeroTerminated, encoding);
        }

        // Read Pointer
        public UIntPtr ReadPointer(string address, bool isOffsetsHexdecimal = false)
        {
            if (!ReaderHelper(address, isOffsetsHexdecimal, out UIntPtr organizedAddress))
                return UIntPtr.Zero;

            return ReadPointerAddress(process!, isProcess64Bit, organizedAddress);
        }
        public UIntPtr ReadPointer(UIntPtr address)
        {
            if (!IsConnected) return UIntPtr.Zero;

            return ReadPointerAddress(process!, isProcess64Bit, address);
        }

        // Reader Helper ( String Address Organizer )
        private bool ReaderHelper(string address, bool isOffsetsHexdecimal, out UIntPtr organizedAddress)
        {
            organizedAddress = UIntPtr.Zero;

            if (!IsConnected) return false;

            address = address.Replace(" ", "");

            if (string.IsNullOrEmpty(address)) return false;

            organizedAddress = GetAddress(process!, isProcess64Bit, address, isOffsetsHexdecimal);
            if (organizedAddress == UIntPtr.Zero || organizedAddress.ToUInt64() < 65536) return false;

            return true;
        }

        #endregion

        #region Subscribe

        private ConcurrentDictionary<UIntPtr, CancellationTokenSource> BindTokens = new ConcurrentDictionary<UIntPtr, CancellationTokenSource>();

        // Subscribe
        public bool Subscribe<T>(string address, Action<T?> action, int interval, bool isOffsetsHexdecimal = false)
        {
            if (!SubscribeHelper(address, action, isOffsetsHexdecimal, out UIntPtr organizedAddress))
                return false;

            SubscribeLoop(organizedAddress, action, interval);

            return true;
        }
        public bool Subscribe<T>(UIntPtr address, Action<T?> action, int interval, bool isOffsetsHexdecimal = false)
        {
            if (!IsConnected) return false;

            SubscribeLoop(address, action, interval);

            return true;
        }
        private void SubscribeLoop<T>(UIntPtr address, Action<T?> action, int interval)
        {
            if (BindTokens.ContainsKey(address)) return;

            var cts = new CancellationTokenSource();
            BindTokens.TryAdd(address, cts);

            Task.Factory.StartNew(() =>
            {
                while (!cts!.Token.IsCancellationRequested)
                {
                    if (!IsConnected)
                    {
                        try
                        {
                            lock (BindTokens)
                            {
                                BindTokens[address].Cancel();
                                BindTokens.TryRemove(address, out var _);
                            }
                        }
                        catch { }
                        return;
                    }

                    T? value = ReadAddress<T>(process!, address);
                    action(value);
                    Thread.Sleep(interval);
                }
            },
            cts!.Token);
        }

        // Subscribe Bytes
        public bool SubscribeBytes(string address, int length, Action<byte[]?> action, int interval, bool isOffsetsHexdecimal = false)
        {
            if (!SubscribeHelper(address, action, isOffsetsHexdecimal, out UIntPtr organizedAddress))
                return false;

            SubscribeBytesLoop(organizedAddress, length, action, interval);

            return true;
        }
        public bool SubscribeBytes(UIntPtr address, int length, Action<byte[]?> action, int interval, bool isOffsetsHexdecimal = false)
        {
            if (!IsConnected) return false;

            SubscribeBytesLoop(address, length, action, interval);

            return true;
        }
        private void SubscribeBytesLoop(UIntPtr address, int length, Action<byte[]?> action, int interval)
        {
            if (BindTokens.ContainsKey(address)) return;

            var cts = new CancellationTokenSource();
            BindTokens.TryAdd(address, cts);

            Task.Factory.StartNew(() =>
            {
                while (!cts!.Token.IsCancellationRequested)
                {
                    if (!IsConnected)
                    {
                        try
                        {
                            lock (BindTokens)
                            {
                                BindTokens[address].Cancel();
                                BindTokens.TryRemove(address, out var _);
                            }
                        }
                        catch { }
                        return;
                    }

                    byte[]? value = ReadBytesAddress(process!, address, length);
                    action(value);
                    Thread.Sleep(interval);
                }
            },
            cts!.Token);
        }

        // Subscribe String
        public bool SubscribeString(string address, int bytesLength, Action<string?> action, int interval, bool isOffsetsHexdecimal = false, bool zeroTerminated = true, Encoding encoding = null!)
        {
            if (!SubscribeHelper(address, action, isOffsetsHexdecimal, out UIntPtr organizedAddress))
                return false;

            SubscribeStringLoop(organizedAddress, bytesLength, action, interval, zeroTerminated, encoding);

            return true;
        }
        public bool SubscribeString(UIntPtr address, int bytesLength, Action<string?> action, int interval, bool isOffsetsHexdecimal = false, bool zeroTerminated = true, Encoding encoding = null!)
        {
            if (!IsConnected) return false;

            SubscribeStringLoop(address, bytesLength, action, interval, zeroTerminated, encoding);

            return true;
        }
        private void SubscribeStringLoop(UIntPtr address, int bytesLength, Action<string?> action, int interval, bool zeroTerminated = true, Encoding encoding = null!)
        {
            if (BindTokens.ContainsKey(address)) return;

            var cts = new CancellationTokenSource();
            BindTokens.TryAdd(address, cts);

            Task.Factory.StartNew(() =>
            {
                while (!cts!.Token.IsCancellationRequested)
                {
                    if (!IsConnected)
                    {
                        try
                        {
                            lock (BindTokens)
                            {
                                BindTokens[address].Cancel();
                                BindTokens.TryRemove(address, out var _);
                            }
                        }
                        catch { }
                        return;
                    }

                    string? value = ReadStringAddress(process!, address, bytesLength, zeroTerminated, encoding);
                    action(value);
                    Thread.Sleep(interval);
                }
            },
            cts!.Token);
        }

        // Subscribe Pointer
        public bool SubscribePointer(string address, Action<UIntPtr> action, int interval, bool isOffsetsHexdecimal = false)
        {
            if (!SubscribeHelper(address, action, isOffsetsHexdecimal, out UIntPtr organizedAddress))
                return false;

            SubscribePointerLoop(organizedAddress, action, interval);

            return true;
        }
        public bool SubscribePointer(UIntPtr address, Action<UIntPtr> action, int interval)
        {
            if (!IsConnected) return false;

            SubscribePointerLoop(address, action, interval);

            return true;
        }
        private void SubscribePointerLoop(UIntPtr address, Action<UIntPtr> action, int interval)
        {
            if (BindTokens.ContainsKey(address)) return;

            var cts = new CancellationTokenSource();
            BindTokens.TryAdd(address, cts);

            Task.Factory.StartNew(() =>
            {
                while (!cts!.Token.IsCancellationRequested)
                {
                    if (!IsConnected)
                    {
                        try
                        {
                            lock (BindTokens)
                            {
                                BindTokens[address].Cancel();
                                BindTokens.TryRemove(address, out var _);
                            }
                        }
                        catch { }
                        return;
                    }

                    UIntPtr value = ReadPointerAddress(process!, isProcess64Bit, address);
                    action(value);
                    Thread.Sleep(interval);
                }
            },
            cts!.Token);
        }

        // Subscribe Helper ( String Address Organizer )
        private bool SubscribeHelper<T>(string address, Action<T> action, bool isOffsetsHexdecimal, out UIntPtr organizedAddress)
        {
            organizedAddress = UIntPtr.Zero;

            if (!IsConnected) return false;

            address = address.Replace(" ", "");

            if (string.IsNullOrEmpty(address) || action is null) return false;

            organizedAddress = GetAddress(process!, isProcess64Bit, address, isOffsetsHexdecimal);
            if (organizedAddress == UIntPtr.Zero || organizedAddress.ToUInt64() < 65536) return false;

            return true;
        }

        #endregion

        #region Unsubscribe

        public bool Unsubscribe(string address, bool isOffsetsHexdecimal = false)
        {
            if (!IsConnected) return false;

            address = address.Replace(" ", "");

            if (string.IsNullOrEmpty(address)) return false;

            UIntPtr organizedAddress = GetAddress(process!, isProcess64Bit, address, isOffsetsHexdecimal);
            if (organizedAddress == UIntPtr.Zero || organizedAddress.ToUInt64() < 65536) return false;

            try
            {
                lock (BindTokens)
                {
                    BindTokens[organizedAddress].Cancel();
                    BindTokens.TryRemove(organizedAddress, out var _);
                }
            }
            catch { }

            return true;
        }
        public bool Unsubscribe(UIntPtr address)
        {
            if (!IsConnected) return false;

            try
            {
                lock (BindTokens)
                {
                    BindTokens[address].Cancel();
                    BindTokens.TryRemove(address, out var _);
                }
            }
            catch { }

            return true;
        }

        #endregion

        #region Write

        public bool Write<T>(string address, T value, bool isOffsetsHexdecimal = false)
        {
            if (!IsConnected) return false;

            address = address.Replace(" ", "");

            if (string.IsNullOrEmpty(address) || value is null) return false;

            UIntPtr organizedAddress = GetAddress(process!, isProcess64Bit, address, isOffsetsHexdecimal);
            if (organizedAddress == UIntPtr.Zero || organizedAddress.ToUInt64() < 65536) return false;

            return WriteToAddress(process!, organizedAddress, value);
        }
        public bool Write<T>(UIntPtr address, T value)
        {
            if (!IsConnected) return false;

            return WriteToAddress(process!, address, value);
        }

        #endregion

        #region Freeze

        private ConcurrentDictionary<UIntPtr, CancellationTokenSource> FreezeTokens = new ConcurrentDictionary<UIntPtr, CancellationTokenSource>();

        public bool Freeze<T>(string address, T value, int interval, bool isOffsetsHexdecimal = false)
        {
            if (!FreezeHelper(address, value, isOffsetsHexdecimal, out UIntPtr organizedAddress))
                return false;

            FreezeLoop(organizedAddress, value, interval);

            return true;
        }
        public bool Freeze<T>(UIntPtr address, T value, int interval, bool isOffsetsHexdecimal = false)
        {
            if (!IsConnected) return false;

            FreezeLoop(address, value, interval);

            return true;
        }

        private void FreezeLoop<T>(UIntPtr address, T value, int interval)
        {
            if (BindTokens.ContainsKey(address)) return;

            var cts = new CancellationTokenSource();
            BindTokens.TryAdd(address, cts);

            Task.Factory.StartNew(() =>
            {
                while (!cts!.Token.IsCancellationRequested)
                {
                    if (!IsConnected)
                    {
                        try
                        {
                            lock (FreezeTokens)
                            {
                                FreezeTokens[address].Cancel();
                                FreezeTokens.TryRemove(address, out var _);
                            }
                        }
                        catch { }
                        return;
                    }

                    WriteToAddress(process!, address, value);
                    Thread.Sleep(interval);
                }
            },
            cts!.Token);
        }

        // Freeze Helper ( String Address Organizer )
        private bool FreezeHelper<T>(string address, T value, bool isOffsetsHexdecimal, out UIntPtr organizedAddress)
        {
            organizedAddress = UIntPtr.Zero;

            if (!IsConnected) return false;

            address = address.Replace(" ", "");

            if (string.IsNullOrEmpty(address) || value is null) return false;

            organizedAddress = GetAddress(process!, isProcess64Bit, address, isOffsetsHexdecimal);
            if (organizedAddress == UIntPtr.Zero || organizedAddress.ToUInt64() < 65536) return false;

            return true;
        }

        #endregion

        #region Unfreeze

        public bool Unfreeze(string address, bool isOffsetsHexdecimal = false)
        {
            if (!IsConnected) return false;

            address = address.Replace(" ", "");

            if (string.IsNullOrEmpty(address)) return false;

            UIntPtr organizedAddress = GetAddress(process!, isProcess64Bit, address, isOffsetsHexdecimal);
            if (organizedAddress == UIntPtr.Zero || organizedAddress.ToUInt64() < 65536) return false;

            try
            {
                lock (FreezeTokens)
                {
                    FreezeTokens[organizedAddress].Cancel();
                    FreezeTokens.TryRemove(organizedAddress, out var _);
                }
            }
            catch { }

            return true;
        }
        public bool Unfreeze(UIntPtr address, bool isOffsetsHexdecimal = false)
        {
            if (!IsConnected) return false;

            try
            {
                lock (FreezeTokens)
                {
                    FreezeTokens[address].Cancel();
                    FreezeTokens.TryRemove(address, out var _);
                }
            }
            catch { }

            return true;
        }

        #endregion

        #region Dispose

        public void Dispose()
        {
            process?.Dispose();
        }

        #endregion


        #region Utilities

        #region Read Address

        [DllImport("kernel32.dll")]
        private static extern bool ReadProcessMemory(IntPtr hProcess, UIntPtr lpBaseAddress, [Out] byte[] lpBuffer, UIntPtr nSize, IntPtr lpNumberOfBytesRead);

        private static byte[]? ReadBytesAddress(Process process, UIntPtr address, int length)
        {
            byte[] data = new byte[length];
            if (ReadProcessMemory(process.Handle, address, data, (UIntPtr)length, IntPtr.Zero))
            {
                return data;
            }

            return null;
        }

        private static string? ReadStringAddress(Process process, UIntPtr address, int length, bool zeroTerminated, Encoding encoding)
        {
            byte[] data = new byte[length];
            if (ReadProcessMemory(process.Handle, address, data, (UIntPtr)length, IntPtr.Zero))
            {
                return
                    zeroTerminated
                    ? encoding.GetString(data).Split(default(char))[0]
                    : encoding.GetString(data);
            }

            return null;
        }

        private static UIntPtr ReadPointerAddress(Process process, bool is64Bit, UIntPtr address)
        {
            int byteSize = is64Bit ? 16 : 8;

            byte[] data = new byte[byteSize];
            if (ReadProcessMemory(process.Handle, address, data, (UIntPtr)byteSize, IntPtr.Zero))
            {
                return (UIntPtr)BitConverter.ToUInt32(data, 0);
            }

            return UIntPtr.Zero;
        }

        private static T? ReadAddress<T>(Process process, UIntPtr address)
        {
            Type type = typeof(T);

            // Bool
            if (type == typeof(bool))
            {
                int byteSize = 1;

                byte[] data = new byte[byteSize];
                if (ReadProcessMemory(process.Handle, address, data, (UIntPtr)4uL, IntPtr.Zero))
                {
                    object obj = Convert.ToBoolean(data[0]);
                    return (T?)Convert.ChangeType(obj, typeof(T?));
                }

                return default;
            }

            // Byte
            else if (type == typeof(byte))
            {
                int byteSize = 1;

                byte[] data = new byte[byteSize];
                if (ReadProcessMemory(process.Handle, address, data, (UIntPtr)1uL, IntPtr.Zero))
                {
                    object obj = data[0];
                    return (T?)Convert.ChangeType(obj, typeof(T?));
                }

                return default;
            }

            // Int
            else if (type == typeof(int))
            {
                int byteSize = 4;

                byte[] data = new byte[byteSize];
                if (ReadProcessMemory(process.Handle, address, data, (UIntPtr)4uL, IntPtr.Zero))
                {
                    object obj = BitConverter.ToInt32(data, 0);
                    return (T?)Convert.ChangeType(obj, typeof(T?));
                }

                return default;
            }

            // Long
            else if (type == typeof(long))
            {
                int byteSize = 8;

                byte[] data = new byte[byteSize];
                if (ReadProcessMemory(process.Handle, address, data, (UIntPtr)8uL, IntPtr.Zero))
                {
                    object obj = BitConverter.ToInt64(data, 0);
                    return (T?)Convert.ChangeType(obj, typeof(T?));
                }

                return default;
            }

            // Float
            else if (type == typeof(float))
            {
                int byteSize = 4;

                byte[] data = new byte[byteSize];
                if (ReadProcessMemory(process.Handle, address, data, (UIntPtr)4uL, IntPtr.Zero))
                {
                    object obj = BitConverter.ToSingle(data, 0);
                    return (T?)Convert.ChangeType(obj, typeof(T?));
                }

                return default;
            }

            // Double
            else if (type == typeof(double))
            {
                int byteSize = 8;

                byte[] data = new byte[byteSize];
                if (ReadProcessMemory(process.Handle, address, data, (UIntPtr)8uL, IntPtr.Zero))
                {
                    object obj = BitConverter.ToDouble(data, 0);
                    return (T?)Convert.ChangeType(obj, typeof(T?));
                }

                return default;
            }

            else return default;
        }

        #endregion

        #region Write To Address

        [DllImport("kernel32.dll")]
        private static extern bool WriteProcessMemory(IntPtr hProcess, UIntPtr lpBaseAddress, byte[] lpBuffer, UIntPtr nSize, IntPtr lpNumberOfBytesWritten);


        private static bool WriteToAddress<T>(Process process, UIntPtr address, T value)
        {
            Type type = typeof(T);
            byte[]? data;

            // Bool
            if (type == typeof(bool))
            {
                var newValue = (bool)Convert.ChangeType(value, type)!;

                data = BitConverter.GetBytes(newValue);
            }

            // Byte
            else if (type == typeof(byte))
            {
                var newValue = (byte)Convert.ChangeType(value, type)!;

                data = new byte[1] { newValue };
            }

            // Short
            else if (type == typeof(short))
            {
                var newValue = (short)Convert.ChangeType(value, type)!;

                data = BitConverter.GetBytes(newValue);
            }

            // Int
            else if (type == typeof(int))
            {
                var newValue = (int)Convert.ChangeType(value, type)!;

                data = BitConverter.GetBytes(newValue);
            }

            // Float
            else if (type == typeof(float))
            {
                var newValue = (float)Convert.ChangeType(value, type)!;

                data = BitConverter.GetBytes(newValue);
            }

            // Long
            else if (type == typeof(long))
            {
                var newValue = (long)Convert.ChangeType(value, type)!;

                data = BitConverter.GetBytes(newValue);
            }

            // Double
            else if (type == typeof(double))
            {
                var newValue = (double)Convert.ChangeType(value, type)!;

                data = BitConverter.GetBytes(newValue);
            }

            // String
            else if (type == typeof(string))
            {
                var newValue = (string)Convert.ChangeType(value, type)!;

                data = Encoding.UTF8.GetBytes(newValue);
            }

            else data = ObjectToByteArray(value!);

            return WriteProcessMemory(process!.Handle, address, data, (UIntPtr)(ulong)data.Length, IntPtr.Zero);
        }

        private static byte[] ObjectToByteArray(object obj)
        {
            var size = Marshal.SizeOf(obj);
            // Both managed and unmanaged buffers required.
            var bytes = new byte[size];
            var ptr = Marshal.AllocHGlobal(size);
            // Copy object byte-to-byte to unmanaged memory.
            Marshal.StructureToPtr(obj, ptr, false);
            // Copy data from unmanaged memory to managed buffer.
            Marshal.Copy(ptr, bytes, 0, size);
            // Release unmanaged memory.
            Marshal.FreeHGlobal(ptr);

            return bytes;
        }


        #endregion

        #region Get Address

        private static UIntPtr GetAddress(Process process, bool is64Bit, string code, bool isOffsetsHexdecimal)
        {
            // Return If Basic Address
            if (!code.Contains('+') && !code.Contains(',')) return GetBaseAddress(process, code, isOffsetsHexdecimal);

            // Split Code
            string baseAddress;
            string[]? offsets;
            string[] splittedCode = code.Split(new char[] { ',' }, 2);
            if (splittedCode.Length == 1)
            {
                baseAddress = code;
                offsets = null;
            }
            else
            {
                baseAddress = splittedCode[0];
                offsets = splittedCode[1].Split(',');
            }

            // Get Base Address
            UIntPtr baseAddressPtr;
            string[] splittedBaseAddress = baseAddress.Split(new char[] { '+' }, 2);
            if (splittedBaseAddress.Length == 1)
            {
                baseAddressPtr = GetBaseAddress(process, baseAddress, isOffsetsHexdecimal);
            }
            else
            {
                try
                {
                    baseAddressPtr = UIntPtr.Add
                        (
                            GetBaseAddress(process, splittedBaseAddress[0], isOffsetsHexdecimal),

                            isOffsetsHexdecimal
                            ? Convert.ToInt32(splittedBaseAddress[1], 16)
                            : Int32.Parse(splittedBaseAddress[1])
                        );
                }
                catch
                {
                    return UIntPtr.Zero;
                }
            }

            // If No Offsets Found
            if (offsets is null) return baseAddressPtr;

            // Convert Offsets
            var offsetArray = offsets?.Select(offset =>
            {
                if (!isOffsetsHexdecimal) return Int32.Parse(offset);

                int organizedOffset = Convert.ToInt32(offset.Replace("-", ""), 16);
                if (offset.Contains("-")) organizedOffset *= -1;

                return organizedOffset;
            }
            ).ToArray();

            // Get First Address
            ulong size = is64Bit ? 16uL : 8uL;
            byte[] data = new byte[size];

            ReadProcessMemory
            (
            process!.Handle,
            baseAddressPtr,
            data,
            (UIntPtr)size,
            IntPtr.Zero
            );

            uint pointedAddress = BitConverter.ToUInt32(data, 0);

            UIntPtr uIntPtr = (UIntPtr)0uL;
            for (int i = 0; i < offsetArray!.Length; i++)
            {
                uIntPtr = new UIntPtr(Convert.ToUInt32(pointedAddress + offsetArray[i]));

                ReadProcessMemory
                    (process!.Handle,
                    uIntPtr,
                    data,
                    (UIntPtr)size,
                    IntPtr.Zero);

                pointedAddress = BitConverter.ToUInt32(data, 0);
            }

            return uIntPtr;
        }

        private static UIntPtr GetBaseAddress(Process process, string code, bool isHexdecimal)
        {
            string codeLowered = code.ToLower();

            // If Only Module
            if (codeLowered.EndsWith(".exe") || codeLowered.EndsWith(".dll") || codeLowered.EndsWith(".bin"))
            {
                return (UIntPtr)(long)GetModuleAddressByName(process, code);
            }

            // If Main Or Base
            if (codeLowered == "main" || codeLowered == "base")
            {
                return (UIntPtr)(ulong)process!.MainModule!.BaseAddress;
            }

            // If Only Hexdecimal
            try
            {
                return
                    isHexdecimal
                    ? new UIntPtr(Convert.ToUInt64(code, 16))
                    : (UIntPtr)UInt32.Parse(code);
            }
            catch
            {
                return UIntPtr.Zero;
            }
        }

        #endregion

        #region Is Administrator

        private static bool IsAdministrator()
        {
            try
            {
                WindowsIdentity user = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(user);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { }

            return false;
        }

        #endregion

        #region Get Module Address By Name

        [DllImport("kernel32.dll")]
        private static extern bool Module32First(IntPtr hSnapshot, ref MODULEENTRY32 lpme);

        [DllImport("kernel32.dll")]
        private static extern bool Module32Next(IntPtr hSnapshot, ref MODULEENTRY32 lpme);

        [DllImport("kernel32", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle([In] IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(SnapshotFlags dwFlags, IntPtr th32ProcessID);


        private const Int64 INVALID_HANDLE_VALUE = -1;
        [Flags]

        private enum SnapshotFlags : uint
        {
            HeapList = 0x00000001,
            Process = 0x00000002,
            Thread = 0x00000004,
            Module = 0x00000008,
            Module32 = 0x00000010,
            Inherit = 0x80000000,
            All = 0x0000001F,
            NoHeaps = 0x40000000
        }

        [StructLayout(LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Ansi)]
        private struct MODULEENTRY32
        {
            internal uint dwSize;
            internal uint th32ModuleID;
            internal uint th32ProcessID;
            internal uint GlblcntUsage;
            internal uint ProccntUsage;
            internal IntPtr modBaseAddr;
            internal uint modBaseSize;
            internal IntPtr hModule;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            internal string szModule;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            internal string szExePath;
        }


        private static IntPtr GetModuleAddressByName(Process process, string moduleName)
        {
            IntPtr procId = (IntPtr)process.Id;
            IntPtr modBaseAddr = IntPtr.Zero;
            IntPtr hSnap = CreateToolhelp32Snapshot(SnapshotFlags.Module | SnapshotFlags.Module32, procId);

            if (hSnap.ToInt64() != INVALID_HANDLE_VALUE)
            {
                MODULEENTRY32 modEntry = new MODULEENTRY32();
                modEntry.dwSize = (uint)Marshal.SizeOf(typeof(MODULEENTRY32));

                if (Module32First(hSnap, ref modEntry))
                {
                    do
                    {
                        if (modEntry.szModule.Equals(moduleName))
                        {
                            modBaseAddr = modEntry.modBaseAddr;
                            break;
                        }
                    }
                    while (Module32Next(hSnap, ref modEntry));
                }
            }
            CloseHandle(hSnap);

            return modBaseAddr;
        }

        #endregion

        #endregion
    }

    #endregion

    #region Process Overlay

    public class ProcessOverlay : IDisposable
    {
        public bool IsConnected
        {
            get
            {
                if (process is null && process!.HasExited) return false;

                GetWindowRect(window, out windowRect);

                return
                    !(
                    windowRect.Top == 0
                    && windowRect.Left == 0
                    && windowRect.Right == 0
                    && windowRect.Bottom == 0
                    );
            }
        }

        #region Constructer

        [DllImport("user32.dll", SetLastError = true)]
        static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);


        private Form overlayForm = new Form()
        {
            BackColor = Color.Wheat,
            TransparencyKey = Color.Wheat,
            TopMost = true,
            TopLevel = true,
            FormBorderStyle = FormBorderStyle.None,
            ShowInTaskbar = false,
            Size = new Size(0, 0)
        };

        private Process? process;
        private int adjustInterval;
        public ProcessOverlay(Process process, int adjustInterval)
        {
            this.adjustInterval = adjustInterval;

            int initialStyle = GetWindowLong(overlayForm.Handle, -20);
            SetWindowLong(overlayForm.Handle, -20, initialStyle | 0x80000 | 0x20);

            TryConnectProcess(process);
        }

        public ProcessOverlay(int processId, int adjustInterval) : this(Process.GetProcessById(processId), adjustInterval) { }

        private string? ctorParameterProcessName;
        public ProcessOverlay(string processName, int adjustInterval)
            : this(
                  Process.GetProcessesByName(processName.Replace(".exe", "")).FirstOrDefault()!,
                  adjustInterval
                  )
        {
            ctorParameterProcessName = processName.Replace(".exe", "");
        }

        public ProcessOverlay(int adjustInterval) : this(Process.GetCurrentProcess(), adjustInterval) { }

        #endregion

        #region Connect / Wait For Connect

        public Task<bool> WaitForConnectAsync(int interval)
        {
            if (IsConnected) return Task.FromResult(true);

            string? targetName = ctorParameterProcessName ?? process?.ProcessName;
            if (targetName is null) Task.FromResult(false);

            return Task.Run(() =>
            {
                while (true)
                {
                    if (TryConnectProcess(targetName!)) break;
                    Thread.Sleep(interval);
                }

                return true;
            });
        }

        public bool TryConnectProcess()
        {
            if (IsConnected) return true;

            string? targetName = ctorParameterProcessName ?? process?.ProcessName;
            if (targetName is null) return false;

            return TryConnectProcess(targetName);
        }

        private bool TryConnectProcess(string processName)
        {
            return TryConnectProcess(Process.GetProcessesByName(processName).FirstOrDefault());
        }

        private IntPtr window;
        private RECT windowRect;
        private bool TryConnectProcess(Process? process)
        {
            this.process = null;
            if (process is null || process.HasExited) return false;

            window = FindWindow(null!, process!.MainWindowTitle);
            if (window == IntPtr.Zero) return false;

            this.process = process;
            if (!IsConnected) return false;

            AdjustOverlay();

            return true;
        }

        #endregion

        #region Adjust Overlay

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hwnd, out RECT rectangle);

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();


        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }


        private void AdjustOverlay()
        {
            LocateOverlay();

            Task.Run(() =>
            {
                Thread.Sleep(adjustInterval);

                while (IsConnected)
                {
                    LocateOverlay();

                    Thread.Sleep(adjustInterval);
                }

                overlayForm.Hide();
            });
        }

        bool isOn = false;
        Graphics? graphics;
        private void LocateOverlay()
        {
            // If Focused
            var focusedWindow = GetForegroundWindow();
            if (focusedWindow != window)
            {
                if (isOn)
                {
                    isOn = false;
                    overlayForm.Hide();
                }
                return;
            }

            overlayForm.Size = new Size(windowRect.Right - windowRect.Left, windowRect.Bottom - windowRect.Top);
            overlayForm.Top = windowRect.Top;
            overlayForm.Left = windowRect.Left;

            if (!isOn) overlayForm.Show();
            isOn = true;

            graphics?.Dispose();
            graphics = null;
            graphics = overlayForm.CreateGraphics();
        }

        #endregion

        #region Dispose

        public void Dispose()
        {
            process?.Dispose();
            graphics?.Dispose();
            overlayForm.Dispose();
        }

        #endregion


        public void Reset()
        {
            graphics?.FillRectangle(Brushes.Wheat, 0, 0, overlayForm.Width, overlayForm.Height);
        }
        public void DrawPoint(Color color, Point point)
        {
            graphics?.FillEllipse(new SolidBrush(color), point.X, point.Y, 5, 5);
        }
        public void DrawRectangle(Color color, Rectangle rectangle)
        {
            graphics?.DrawRectangle(new Pen(color, 3), rectangle);
        }

    }

    #endregion
}
