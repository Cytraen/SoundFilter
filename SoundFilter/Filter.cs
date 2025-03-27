﻿using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;

namespace SoundFilter;

internal unsafe class Filter : IDisposable
{
    private static class Signatures
    {
        internal const string PlaySpecificSound =
            "48 89 5C 24 ?? 48 89 74 24 ?? 57 48 83 EC 20 33 F6 8B DA 48 8B F9 0F BA E2 0F";

        internal const string GetResourceSync = "E8 ?? ?? ?? ?? 48 8B D8 8B C7";
        internal const string GetResourceAsync = "E8 ?? ?? ?? ?? 48 8B D8 EB 07 F0 FF 83";
        internal const string LoadSoundFile = "E8 ?? ?? ?? ?? 48 85 C0 75 12 B0 F6";

        internal const string MusicManagerOffset =
            "48 89 87 ?? ?? ?? ?? 49 8B CC E8 ?? ?? ?? ?? 48 8B 8F";
    }

    // Updated: 5.55
    private const int ResourceDataPointerOffset = 0xB0;
    private const int MusicManagerStreamingOffset = 0x32;

    #region Delegates

    private delegate void* PlaySpecificSoundDelegate(long a1, int idx);

    private delegate void* GetResourceSyncPrototype(
        IntPtr pFileManager,
        uint* pCategoryId,
        char* pResourceType,
        uint* pResourceHash,
        char* pPath,
        void* pUnknown
    );

    private delegate void* GetResourceAsyncPrototype(
        IntPtr pFileManager,
        uint* pCategoryId,
        char* pResourceType,
        uint* pResourceHash,
        char* pPath,
        void* pUnknown,
        bool isUnknown
    );

    private delegate IntPtr LoadSoundFileDelegate(IntPtr resourceHandle, uint a2);

    #endregion

    #region Hooks

    private Hook<PlaySpecificSoundDelegate>? PlaySpecificSoundHook { get; set; }

    private Hook<GetResourceSyncPrototype>? GetResourceSyncHook { get; set; }

    private Hook<GetResourceAsyncPrototype>? GetResourceAsyncHook { get; set; }

    private Hook<LoadSoundFileDelegate>? LoadSoundFileHook { get; set; }

    #endregion

    private Plugin Plugin { get; }
    private bool WasStreamingEnabled { get; }

    private ConcurrentDictionary<IntPtr, string> Scds { get; } = new();

    internal ConcurrentQueue<string> Recent { get; } = new();

    private IntPtr NoSoundPtr { get; }
    private IntPtr InfoPtr { get; }

    private IntPtr MusicManager
    {
        get
        {
            if (
                !Services.SigScanner.TryScanText(
                    Signatures.MusicManagerOffset,
                    out var instructionPtr
                )
            )
            {
                Services.PluginLog.Warning("Could not find music manager");
                return IntPtr.Zero;
            }

            var offset = *(int*)(instructionPtr + 3);
            return *(IntPtr*)((IntPtr)Framework.Instance() + offset);
        }
    }

    private bool Streaming
    {
        get
        {
            var manager = MusicManager;
            if (manager == IntPtr.Zero)
            {
                return false;
            }

            return *(byte*)(manager + MusicManagerStreamingOffset) > 0;
        }
        set
        {
            var manager = MusicManager;
            if (manager == IntPtr.Zero)
            {
                return;
            }

            *(byte*)(manager + MusicManagerStreamingOffset) = (byte)(value ? 1 : 0);
        }
    }

    internal Filter(Plugin plugin)
    {
        Plugin = plugin;

        WasStreamingEnabled = Streaming;
        Streaming = false;

        var (noSoundPtr, infoPtr) = SetUpNoSound();
        NoSoundPtr = noSoundPtr;
        InfoPtr = infoPtr;
    }

    private static byte[] GetNoSoundScd()
    {
        var noSound = Resourcer.Resource.AsStream("Resources/gaya_nosound.scd");

        using var memoryStream = new MemoryStream();
        noSound.CopyTo(memoryStream);

        return memoryStream.ToArray();
    }

    private static (IntPtr noSoundPtr, IntPtr infoPtr) SetUpNoSound()
    {
        // get the data of an empty scd
        var noSound = GetNoSoundScd();

        // allocate unmanaged memory for this data and copy the data into the memory
        var noSoundPtr = Marshal.AllocHGlobal(noSound.Length);
        Marshal.Copy(noSound, 0, noSoundPtr, noSound.Length);

        // allocate some memory for feeding into the play sound function
        var infoPtr = Marshal.AllocHGlobal(256);
        // write a pointer to the empty scd
        Marshal.WriteIntPtr(infoPtr + 8, noSoundPtr);
        // specify where the game should offset from for the sound index
        Marshal.WriteInt32(infoPtr + 0x88, 0x54);
        // specify the number of sounds in the file
        Marshal.WriteInt16(infoPtr + 0x94, 0);

        return (noSoundPtr, infoPtr);
    }

    internal void Enable()
    {
        if (
            PlaySpecificSoundHook == null
            && Services.SigScanner.TryScanText(Signatures.PlaySpecificSound, out var playPtr)
        )
        {
            PlaySpecificSoundHook =
                Services.GameInteropProvider.HookFromAddress<PlaySpecificSoundDelegate>(
                    playPtr,
                    PlaySpecificSoundDetour
                );
        }

        if (
            GetResourceSyncHook == null
            && Services.SigScanner.TryScanText(Signatures.GetResourceSync, out var syncPtr)
        )
        {
            GetResourceSyncHook =
                Services.GameInteropProvider.HookFromAddress<GetResourceSyncPrototype>(
                    syncPtr,
                    GetResourceSyncDetour
                );
        }

        if (
            GetResourceAsyncHook == null
            && Services.SigScanner.TryScanText(Signatures.GetResourceAsync, out var asyncPtr)
        )
        {
            GetResourceAsyncHook =
                Services.GameInteropProvider.HookFromAddress<GetResourceAsyncPrototype>(
                    asyncPtr,
                    GetResourceAsyncDetour
                );
        }

        if (
            LoadSoundFileHook == null
            && Services.SigScanner.TryScanText(Signatures.LoadSoundFile, out var soundPtr)
        )
        {
            LoadSoundFileHook = Services.GameInteropProvider.HookFromAddress<LoadSoundFileDelegate>(
                soundPtr,
                LoadSoundFileDetour
            );
        }

        PlaySpecificSoundHook?.Enable();
        LoadSoundFileHook?.Enable();
        GetResourceSyncHook?.Enable();
        GetResourceAsyncHook?.Enable();
    }

    internal void Disable()
    {
        PlaySpecificSoundHook?.Disable();
        LoadSoundFileHook?.Disable();
        GetResourceSyncHook?.Disable();
        GetResourceAsyncHook?.Disable();
    }

    public void Dispose()
    {
        PlaySpecificSoundHook?.Dispose();
        LoadSoundFileHook?.Dispose();
        GetResourceSyncHook?.Dispose();
        GetResourceAsyncHook?.Dispose();

        Marshal.FreeHGlobal(InfoPtr);
        Marshal.FreeHGlobal(NoSoundPtr);

        Streaming = WasStreamingEnabled;
    }

    private void* PlaySpecificSoundDetour(long a1, int idx)
    {
        try
        {
            var shouldFilter = PlaySpecificSoundDetourInner(a1, idx);
            if (shouldFilter)
            {
                a1 = InfoPtr;
                idx = 0;
            }
        }
        catch (Exception ex)
        {
            Services.PluginLog.Error(ex, "Error in PlaySpecificSoundDetour");
        }

        return PlaySpecificSoundHook!.Original(a1, idx);
    }

    private bool PlaySpecificSoundDetourInner(long a1, int idx)
    {
        if (a1 == 0)
        {
            return false;
        }

        var scdData = *(byte**)(a1 + 8);
        if (scdData == null)
        {
            return false;
        }

        // check cached scds for path
        if (!Scds.TryGetValue((IntPtr)scdData, out var path))
        {
            return false;
        }

        path = path.ToLowerInvariant();
        var specificPath = $"{path}/{idx}";

        var shouldFilter = Plugin
            .Config.Globs.Where(entry => entry.Value)
            .Any(entry => entry.Key.IsMatch(specificPath));

        if (Plugin.Config.LogEnabled && (!shouldFilter || Plugin.Config.LogFiltered))
        {
            Recent.Enqueue(specificPath);
            while (Recent.Count > Plugin.Config.LogEntries)
            {
                Recent.TryDequeue(out _);
            }
        }

        return shouldFilter;
    }

    private void* GetResourceSyncDetour(
        IntPtr pFileManager,
        uint* pCategoryId,
        char* pResourceType,
        uint* pResourceHash,
        char* pPath,
        void* pUnknown
    )
    {
        return ResourceDetour(
            true,
            pFileManager,
            pCategoryId,
            pResourceType,
            pResourceHash,
            pPath,
            pUnknown,
            false
        );
    }

    private void* GetResourceAsyncDetour(
        IntPtr pFileManager,
        uint* pCategoryId,
        char* pResourceType,
        uint* pResourceHash,
        char* pPath,
        void* pUnknown,
        bool isUnknown
    )
    {
        return ResourceDetour(
            false,
            pFileManager,
            pCategoryId,
            pResourceType,
            pResourceHash,
            pPath,
            pUnknown,
            isUnknown
        );
    }

    private void* ResourceDetour(
        bool isSync,
        IntPtr pFileManager,
        uint* pCategoryId,
        char* pResourceType,
        uint* pResourceHash,
        char* pPath,
        void* pUnknown,
        bool isUnknown
    )
    {
        var ret = CallOriginalResourceHandler(
            isSync,
            pFileManager,
            pCategoryId,
            pResourceType,
            pResourceHash,
            pPath,
            pUnknown,
            isUnknown
        );

        var path = Util.ReadTerminatedString((byte*)pPath);
        if (ret != null && path.EndsWith(".scd"))
        {
            var scdData = Marshal.ReadIntPtr((IntPtr)ret + ResourceDataPointerOffset);
            // if we immediately have the scd data, cache it, otherwise add it to a waiting list to hopefully be picked up at sound play time
            if (scdData != IntPtr.Zero)
            {
                Scds[scdData] = path;
            }
        }

        return ret;
    }

    private void* CallOriginalResourceHandler(
        bool isSync,
        IntPtr pFileManager,
        uint* pCategoryId,
        char* pResourceType,
        uint* pResourceHash,
        char* pPath,
        void* pUnknown,
        bool isUnknown
    )
    {
        return isSync
            ? GetResourceSyncHook!.Original(
                pFileManager,
                pCategoryId,
                pResourceType,
                pResourceHash,
                pPath,
                pUnknown
            )
            : GetResourceAsyncHook!.Original(
                pFileManager,
                pCategoryId,
                pResourceType,
                pResourceHash,
                pPath,
                pUnknown,
                isUnknown
            );
    }

    private IntPtr LoadSoundFileDetour(IntPtr resourceHandle, uint a2)
    {
        var ret = LoadSoundFileHook!.Original(resourceHandle, a2);

        try
        {
            var handle = (ResourceHandle*)resourceHandle;
            var name = handle->FileName.ToString();
            if (name.EndsWith(".scd"))
            {
                var dataPtr = Marshal.ReadIntPtr(resourceHandle + ResourceDataPointerOffset);
                Scds[dataPtr] = name;
            }
        }
        catch (Exception ex)
        {
            Services.PluginLog.Error(ex, "Error in LoadSoundFileDetour");
        }

        return ret;
    }
}
