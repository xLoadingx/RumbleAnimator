using UnityEngine;

namespace RumbleAnimator.Recording;
    
[Serializable]
public class SQuaternion
{
    public float x, y, z, w;

    public SQuaternion() { }

    public SQuaternion(Quaternion q)
    {
        x = q.x; y = q.y; z = q.z;  w = q.w;
    }

    public Quaternion ToQuaternion()
    {
        return new Quaternion(
            x, 
            y, 
            z, 
            w
        );
    }
}

[Serializable]
public class SVector3
{
    public float x, y, z;

    public SVector3() { }

    public SVector3(Vector3 v)
    {
        x = v.x; 
        y = v.y; 
        z = v.z;
    }

    public Vector3 ToVector3()
    {
        return new Vector3(
            x, 
            y, 
            z
        );
    }
}

public static class TransformExtensions
{
    public static SVector3 ToSerializable(this Vector3 v) => new(v);
    public static SQuaternion ToSerializable(this Quaternion q) => new(q);
}
