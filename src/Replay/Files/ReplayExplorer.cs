using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ReplayMod.Replay.Serialization;
using ReplayMod.Replay.UI;
using static UnityEngine.Mathf;

namespace ReplayMod.Replay.Files;

public class ReplayExplorer
{
    public string RootPath { get; }
    public string CurrentFolderPath { get; private set; }

    public List<string> currentReplayPaths = new();
    public List<Entry> currentReplayEntries = new();
    public int currentIndex = -1;

    public enum SortingType
    {
        NameAscending,
        NameDescending,
        
        DateNewestFirst,
        DateOldestFirst,
        
        DurationLongestFirst,
        DurationShortestFirst,
        
        MapAscending,
        PlayerCountDescending
    }

    public string CurrentReplayPath =>
        currentIndex >= 0 && currentIndex < currentReplayPaths.Count
            ? currentReplayPaths[currentIndex]
            : null;

    public Entry currentlySelectedEntry =>
        currentIndex >= 0 && currentIndex < currentReplayEntries.Count
            ? currentReplayEntries[currentIndex]
            : null;

    public ReplayExplorer(string root)
    {
        RootPath = root;
        CurrentFolderPath = root;
        Refresh();
    }

    public void Refresh()
    {
        currentReplayEntries = GetEntries();
        currentReplayPaths = currentReplayEntries.Select(e => e.FullPath).ToList();
        
        currentIndex = Clamp(currentIndex, -1, currentReplayPaths.Count - 1);
    }

    public List<Entry> GetEntries(SortingType sorting = SortingType.DateNewestFirst)
    {
        var folders = Directory
            .GetDirectories(CurrentFolderPath)
            .Select(dir => new Entry
            {
                Name = Path.GetFileName(dir), 
                FullPath = dir, 
                IsFolder = true
            })
            .OrderBy(e => e.Name)
            .ToList();
        
        var files = Directory
            .GetFiles(CurrentFolderPath, "*.replay")
            .Select(file => new Entry
            {
                Name = Path.GetFileNameWithoutExtension(file),
                FullPath = file,
                header = ReplayArchive.GetManifest(file),
                IsFolder = false,
            })
            .ToList();

        files = SortFiles(files, sorting);

        currentReplayEntries = files;

        folders.AddRange(files);
        return folders;
    }

    private List<Entry> SortFiles(List<Entry> files, SortingType sorting)
    {
        return sorting switch
        {
            SortingType.NameAscending => files.OrderBy(f => f.Name).ToList(),
            SortingType.NameDescending => files.OrderByDescending(f => f.Name).ToList(),

            SortingType.DateNewestFirst => files.OrderBy(f => File.GetLastWriteTimeUtc(f.FullPath)).ToList(),
            SortingType.DateOldestFirst => files.OrderByDescending(f => File.GetLastWriteTimeUtc(f.FullPath)).ToList(),

            SortingType.DurationLongestFirst => files.OrderBy(f => f.header.Duration).ToList(),
            SortingType.DurationShortestFirst => files.OrderByDescending(f => f.header.Duration).ToList(),

            SortingType.MapAscending => files.OrderBy(f => ReplayFormatting.GetMapName(header: f.header), StringComparer.OrdinalIgnoreCase).ToList(),
            SortingType.PlayerCountDescending => files.OrderByDescending(f => f.header.Players?.Length).ToList(),

            _ => files
        };
    }

    public void Enter(string path)
    {
        if (!Directory.Exists(path)) return;
        CurrentFolderPath = path;
        currentIndex = -1;
        Refresh();
    }

    public void GoUp()
    {
        var parent = Directory.GetParent(CurrentFolderPath);
        if (parent != null)
        {
            CurrentFolderPath = parent.FullName;
            currentIndex = -1;
            Refresh();
        }
    }

    public void Next()
    {
        if (currentReplayPaths.Count == 0) return;

        currentIndex++;
        if (currentIndex > currentReplayPaths.Count - 1)
            currentIndex = -1;
    }

    public void Previous()
    {
        if (currentReplayPaths.Count == 0) return;
        
        currentIndex--;
        if (currentIndex < -1)
            currentIndex = currentReplayPaths.Count - 1;
    }

    public void Select(int index)
    {
        if (index < -1 || index >= currentReplayPaths.Count)
            currentIndex = -1;
        else
            currentIndex = index;
    }

    public IEnumerable<string> GetSubFolders()
        => Directory.GetDirectories(CurrentFolderPath);

    public class Entry
    {
        public string Name;
        public string FullPath;
        public ReplaySerializer.ReplayHeader header;
        public bool IsFolder;
    }
}