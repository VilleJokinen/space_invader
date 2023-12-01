// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Math;

public static class VectorExtensions
{
    public static UnityEngine.Vector2 ToVector2(this IntVector2 v) => new UnityEngine.Vector2(v.X, v.Y);

    public static UnityEngine.Vector3 ToVector3(this IntVector3 v) => new UnityEngine.Vector3(v.X, v.Y, v.Z);
}
