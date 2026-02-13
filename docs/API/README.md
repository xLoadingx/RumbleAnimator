# Replay Extensions

Replay Extensions allow external mods to inject custom data into replay files.

An extension can:
- Store custom archive-level data inside the replay file
- Write custom per-frame data
- Read that data back during playback
- Add timeline markers
- React to replay lifecycle events

Each extension is isolated under its own namespace inside the replay archive.

---

## Registering an Extension

Register your extension once during initialization:

```csharp
private ReplayAPI.ReplayExtension extension;

public override void OnlateInitializeMelon() 
{
    extension = ReplayAPI.RegisterExtension(
        id: "MyModId",
        onBuild: OnArchiveBuild,
        onRead: OnArchiveRead,
        onWriteFrame: OnWriteFrame,
        onReadFrame: OnReadFrame
    );
}
```

The `id` must remain stable forever once released.  
Changing it will prevent old replays from associating with your extension.  
ID's must be unique across all mods.

---

## Archive-Level Data
Archive data is written once per replay file.  

Use this for:
 - Static metadata
 - Config snapshots
 - Asset references
 - Large serialized blobs

### Writing Archive Data
```csharp
private void OnArchiveBuild(ReplayAPI.ArchiveBuilder builder) 
{
    var bytes = Encoding.UTF8.GetBytes("example");
    builder.AddFile("data.txt", bytes)
}
```
This writes to
```
extensions/MyModId/data.txt
```

### Reading Archive Data
```csharp
private void OnArchiveRead(ReplayAPI.ArchiveReader reader) 
{
    if (reader.TryGetFile("data.txt", out var bytes)) 
    {
        var text = Encoding.UTF8.GetString(bytes);
    }
}
```

---

## Frame-Level Data
Frame data is written per replay frame and is intended for time-dependent state.

Use this for:
- Transform state
- Animation state
- Runtime object values
- Anything that changes over time and frequently

### Writing Frame Data
```csharp
private enum MyField : byte 
{
    Position,
    Health
}

private void OnWriteFrame(BinaryWriter bw, Frame frame) 
{
    bw.Write(MyField.Position, position);
    bw.Write(MyField.Health, health);
}
```

Each `Write(field, value)` call automatically writes:
- Field ID (1 byte)
- Field payload length (1 byte)
- Field payload (N bytes)

Only write fields that changed from the previous frame (delta encoding highly recommended)  
Always use the provided `BinaryWriter.Write(field, value)` overloads.

The payload is limited to 255 bytes. Do not use classes/structs as a payload.  

Do not reorder enum field values after release. Only append new enum values to the end.

---

### Reading Frame Data
Use `ReplaySerializer.ReadChunk` to reconstruct state:
```csharp
private struct MyState 
{
    public Vector3 Position;
    public int Health;
    
    public MyState Clone() 
    {
        return new MyState 
        {
            Position = Position,
            Health = Health
        }
    }
}

private MyState lastState;

private Dictionary<Frame, MyState> reconstructedFrames = new();

private void OnReadFrame(BinaryReader br, Frame frame) 
{
    var state = ReplaySerializer.ReadChunk<MyState, MyField>(
        br,
        () => lastState?.Clone() ?? new MyState(),
        (state, field, reader) => 
        {
            switch (field) 
            {
                case MyField.Position:
                    state.Position = reader.ReadVector3();
                    break;
                
                case MyField.Health:
                    state.Health = reader.ReadInt32();
                    break;
            }
        });
    
    reconstructedFrames[frame] = state;
}
```

Unknown fields are automatically skipped.

---

## Markers
Extensions can add markers during recording:
```csharp
var marker = extension.AddMarker(
    name: "SpecialMoment",
    time: Time.time,
    color: Color.magenta
)
```
Time.time is the current frame in recording.

Markers are automatically namespaced as:
```
MyModId.SpecialMoment
```

Markers are only added while recording or buffering is active.

---

## Replay Lifecycle Events
`ReplayAPI` exposes the following events:
- `ReplaySelected`
- `ReplayStarted`
- `ReplayEnded`
- `ReplayTimeChanged`
- `ReplayPauseChanged`
- `OnRecordFrame`
- `OnPlaybackFrame`
- `ReplaySaved`
- `ReplayDeleted`
- `ReplayRenamed`

Example:
```csharp
ReplayAPI.ReplayStarted += info => 
{
    LoggerInstance.Msg("Playback started")
}
```
---
## Replay File Structure
A replay archive contains:
```
manifest.json
replay (binary stream)
extensions/
    MyModId/
        custom files...
```

Each extension operates only within its own folder.

---

### Best Practices
- Separate archive data (static) from frame data (dynamic).
- Use delta encoding to reduce file size.
- Keep field payloads small.
- Clear cached state when `ReplayEnded` fires.
- Treat serialized formats as permanent once released.

---

See [ExampleMod.cs](ExampleMod.cs) in this folder for a complete example of recording and replaying a scene object using an extension.