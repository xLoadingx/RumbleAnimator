using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEngine;
using CompressionLevel = System.IO.Compression.CompressionLevel;

namespace ReplayMod;

public static class ReplayAPI
{
    private static readonly List<Extension> _extensions = new();
    
    public static event Action<ReplaySerializer.ReplayHeader> ReplaySelected;
    public static event Action<ReplayInfo> ReplayStarted;
    public static event Action<ReplayInfo> ReplayEnded;
    public static event Action<float> ReplayTimeChanged;
    public static event Action<bool> ReplayPauseChanged;

    public static event Action<Frame> OnPlaybackFrame;
    public static event Action<Frame, bool> OnRecordFrame;

    public static event Action<ReplayInfo, bool, string> ReplaySaved;
    public static event Action<string> ReplayDeleted;
    public static event Action<ReplaySerializer.ReplayHeader, string> ReplayRenamed;

    internal static void ReplaySelectedInternal(ReplaySerializer.ReplayHeader info) => ReplaySelected?.Invoke(info);
    internal static void ReplayStartedInternal(ReplayInfo info) => ReplayStarted?.Invoke(info);
    internal static void ReplayEndedInternal(ReplayInfo info) => ReplayEnded?.Invoke(info);
    internal static void ReplayTimeChangedInternal(float time) => ReplayTimeChanged?.Invoke(time);
    internal static void ReplayPauseChangedInternal(bool paused) => ReplayPauseChanged?.Invoke(paused);

    internal static void OnPlaybackFrameInternal(Frame frame) => OnPlaybackFrame?.Invoke(frame);
    internal static void OnRecordFrameInternal(Frame frame, bool isBuffer) => OnRecordFrame?.Invoke(frame, isBuffer);
    
    internal static void ReplaySavedInternal(ReplayInfo info, bool isBuffer, string path) => ReplaySaved?.Invoke(info, isBuffer, path);
    internal static void ReplayDeletedInternal(string path) => ReplayDeleted?.Invoke(path);
    internal static void ReplayRenamedInternal(ReplaySerializer.ReplayHeader header, string newPath) => ReplayRenamed?.Invoke(header, newPath);

    public static bool IsRecording => Main.isRecording;
    public static bool IsBuffering => Main.isBuffering;
    public static bool IsPlaying => Main.isPlaying;
    public static bool IsPaused => Main.isPaused;
    public static float CurrentTime => Main.elapsedPlaybackTime;
    public static float Duration => Main.currentReplay?.Header?.Duration ?? 0f;

    public static Version FormatVersion => new (BuildInfo.FormatVersion);

    public static IReadOnlyList<Clone> Players => Main.instance.PlaybackPlayers;
    public static IReadOnlyList<GameObject> Structures => Main.instance.PlaybackStructures;
    public static ReplayInfo CurrentReplay => Main.currentReplay;

    public static void Play(string path) => Main.instance.LoadReplay(path);
    public static void Stop() => Main.instance.StopReplay();
    public static void TogglePlayback(bool playing) => Main.instance.TogglePlayback(playing);
    public static void Seek(float time) => Main.instance.SetPlaybackTime(time);
    public static void Seek(int frame) => Main.instance.SetPlaybackFrame(frame);
    public static void SetSpeed(float speed) => Main.instance.SetPlaybackSpeed(speed);

    private static readonly Dictionary<int, Action<BinaryReader, Frame>> _frameReaders = new();
    private static readonly Dictionary<int, Action<BinaryWriter, Frame>> _frameWriters = new();
    
    internal static IEnumerable<Extension> Extensions => _extensions;

    private static int ComputeStableId(string input)
    {
        unchecked
        {
            const int fnvPrime = 16777619;
            int hash = (int)2166136261;

            foreach (char c in input)
            {
                hash ^= c;
                hash *= fnvPrime;
            }

            return hash;
        }
    }
    
    internal static bool TryGetFrameReader(int id, out Action<BinaryReader, Frame> reader)
    {
        var ext = _extensions.FirstOrDefault(e => e.FrameExtensionId == id);

        if (ext != null && ext.OnReadFrame != null)
        {
            reader = ext.OnReadFrame;
            return true;
        }

        reader = null;
        return false;
    }
    
    public static ReplayExtension RegisterExtension(
        string id, 
        Action<ArchiveBuilder> onBuild = null, 
        Action<ArchiveReader> onRead = null,
        Action<BinaryWriter, Frame> onWriteFrame = null,
        Action<BinaryReader, Frame> onReadFrame = null)
    {
        if (_extensions.Any(e => e.Id == id))
        {
            Main.instance.LoggerInstance.Error($"Replay Mod: Extension '{id}' already registered");
            return null;
        }
        
        _extensions.Add(new Extension(
            id, 
            onBuild, 
            onRead,
            ComputeStableId(id),
            onWriteFrame, 
            onReadFrame)
        );
        
        Main.instance.LoggerInstance.Msg($"Replay Mod: Extension '{id}' created");

        return new ReplayExtension(id);
    }

    internal static void InvokeArchiveBuild(ZipArchive zip)
    {
        foreach (var ext in Extensions)
        {
            var builder = new ArchiveBuilder(zip, ext.Id);
            ext.OnBuild?.Invoke(builder);
        }
    }
    
    internal static void InvokeArchiveRead(ZipArchive zip)
    {
        foreach (var ext in Extensions)
        {
            var reader = new ArchiveReader(zip, ext.Id);
            ext.OnRead?.Invoke(reader);
        }
    }

    internal sealed class Extension
    {
        public string Id { get; }
        
        public Action<ArchiveBuilder> OnBuild { get; }
        public Action<ArchiveReader> OnRead { get; }

        public int FrameExtensionId { get; }
        
        public Action<BinaryWriter, Frame> OnWriteFrame { get; }
        public Action<BinaryReader, Frame> OnReadFrame { get; }

        public Extension(
            string id, 
            Action<ArchiveBuilder> onBuild, 
            Action<ArchiveReader> onRead,
            int frameExtensionId,
            Action<BinaryWriter, Frame> onWriteFrame,
            Action<BinaryReader, Frame> onReadFrame)
        {
            Id = id;
            OnBuild = onBuild;
            OnRead = onRead;
            FrameExtensionId = frameExtensionId;
            OnWriteFrame = onWriteFrame;
            OnReadFrame = onReadFrame;
        }
    }

    public sealed class ReplayExtension
    {
        private readonly string _modId;

        internal ReplayExtension(string modId)
        {
            _modId = modId;
        }

        public bool AddMarker(string name, float time)
        {
            if (!Main.isRecording || !Main.isBuffering)
                return false;
            
            Main.instance.Markers.Add(new Marker
            {
                name = $"{_modId}.{name}",
                time = time
            });

            return true;
        }
    }
    
    public sealed class ArchiveBuilder
    {
        private readonly ZipArchive _zip;
        private readonly string _modId;

        internal ArchiveBuilder(ZipArchive zip, string modId)
        {
            _zip = zip;
            _modId = modId;
        }

        public void AddFile(string path, byte[] data, CompressionLevel level = CompressionLevel.Optimal)
        {
            var entry = _zip.CreateEntry($"extensions/{_modId}/{path}", level);

            using var stream = entry.Open();
            stream.Write(data, 0, data.Length);
        }
    }
    
    public sealed class ArchiveReader
    {
        private readonly ZipArchive _zip;
        private readonly string _modId;

        internal ArchiveReader(ZipArchive zip, string modId)
        {
            _zip = zip;
            _modId = modId;
        }

        private string GetFullPath(string relativePath) => $"extensions/{_modId}/{relativePath}";

        public bool TryGetFile(string relativePath, out byte[] data)
        {
            var entry = _zip.GetEntry(GetFullPath(relativePath));

            if (entry == null)
            {
                data = null;
                return false;
            }

            using var stream = entry.Open();
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            data = ms.ToArray();
            return true;
        }

        public bool FileExists(string relativePath) => _zip.GetEntry(GetFullPath(relativePath)) != null;
    }
}