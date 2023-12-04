using System.Collections;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;

namespace IntraMR
{
    public class Utils
    {
        public static void CreatePoint(uint index, Vector3Int dimension,Transform cube)
        {
            Vector3 position = IndexToPosition(index, dimension);
            CreatePoint(position, cube);
        }

        public static void CreatePoint(Vector3 cubicPosition, Transform cube)
        {
            //var position = cube.TransformPoint(cubicPosition);
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.transform.SetParent(cube);
            sphere.transform.localPosition = cubicPosition;
            sphere.transform.localScale= Vector3.one * 0.01f;
        }

        public static void CreatePoint(Vector3 cubicCoordinate, Vector3 dimension, Transform cube, Color color)
        {
            var cubicPosition = cubicCoordinate.Divide(dimension) - 0.5f * Vector3.one;
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.transform.SetParent(cube);
            sphere.transform.localPosition = cubicPosition;
            sphere.transform.localScale = Vector3.one / (dimension.MaxComponent());
            sphere.GetComponent<MeshRenderer>().material.color = color;
        }

        public static Vector3 IndexToPosition(uint index, Vector3Int dimension)
        {
            int i = (int)(index % dimension.x);
            int j = (int)((index / dimension.x) % dimension.y);
            int k = (int)((index / (dimension.x * dimension.y) % dimension.z));
            Vector3 position = new()
            {
                x = i / (float)dimension.x,
                y = j / (float)dimension.y,
                z = k / (float)dimension.z
            };

            return position;
        }

        public static Vector3 IndexToCoordinate(uint index, Vector3Int dimension)
        {
            int i = (int)(index % dimension.x);
            int j = (int)((index / dimension.x) % dimension.y);
            int k = (int)((index / (dimension.x * dimension.y) % dimension.z));
            Vector3 coordinate = new(i,j,k);
            return coordinate;
        }

        public static int CoordinateToIndex(Vector3 coordinate, Vector3Int dimension)
        {
            int index = ((int)coordinate.x + (int)coordinate.y * dimension.x + (int)coordinate.z * dimension.x * dimension.y);
            return index;
        }
        
          public static Vector3 Rotate2D(Vector3 v, float deg)
  {
    Vector3 results = v;
    float cos = Mathf.Cos(MathUtil.Deg2Rad * deg);
    float sin = Mathf.Sin(MathUtil.Deg2Rad * deg);
    results.x = cos * v.x - sin * v.y;
    results.y = sin * v.x + cos * v.y;
    return results;
  }
  
  public static Vector3 NormalizeSafe(Vector3 v, Vector3 fallback)
  {
    return
      v.sqrMagnitude > MathUtil.Epsilon
      ? v.normalized
      : fallback;
  }

  // Returns a vector orthogonal to given vector.
  // If the given vector is a unit vector, the returned vector will also be a unit vector.
  public static Vector3 FindOrthogonal(Vector3 v)
  {
    if (v.x >= MathUtil.Sqrt3Inv)
      return new Vector3(v.y, -v.x, 0.0f);
    else
      return new Vector3(0.0f, v.z, -v.y);
  }

  // Yields two extra vectors that form an orthogonal basis with the given vector.
  // If the given vector is a unit vector, the returned vectors will also be unit vectors.
  public static void FormOrthogonalBasis(Vector3 v, out Vector3 a, out Vector3 b)
  {
    a = FindOrthogonal(v);
    b = Vector3.Cross(a, v);
  }

  // Both vectors must be unit vectors.
  public static Vector3 Slerp(Vector3 a, Vector3 b, float t)
  {
    float dot = Vector3.Dot(a, b);

    if (dot > 0.99999f)
    {
      // singularity: two vectors point in the same direction
      return Vector3.Lerp(a, b, t);
    }
    else if (dot < -0.99999f)
    {
      // singularity: two vectors point in the opposite direction
      Vector3 axis = FindOrthogonal(a);
      return Quaternion.AngleAxis(180.0f * t, axis) * a;
    }

    float rad = MathUtil.AcosSafe(dot);
    return (Mathf.Sin((1.0f - t) * rad) * a + Mathf.Sin(t * rad) * b) / Mathf.Sin(rad);
  }

  public static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
  {
    float tt = t * t;
    return
      0.5f
      * ((2.0f * p1)
        + (-p0 + p2) * t
        + (2.0f * p0 - 5.0f * p1 + 4.0f * p2 - p3) * tt
        + (-p0 + 3.0f * p1 - 3.0f * p2 + p3) * tt * t
        );
  }

  public static Vector3 Abs(Vector3 v)
  {
    return new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
  }

  public static Vector3 Min(Vector3 a, Vector3 b)
  {
    return new Vector3(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Min(a.z, b.z));
  }

  public static Vector3 Max(Vector3 a, Vector3 b)
  {
    return new Vector3(Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y), Mathf.Max(a.z, b.z));
  }

  public static float GetComopnent(Vector3 v, int i)
  {
    switch (i)
    {
      case 0: return v.x;
      case 1: return v.y;
      case 2: return v.z;
    }

    return float.MinValue;
  }
    }
}
