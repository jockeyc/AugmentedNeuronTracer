using System.Collections;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;

namespace ANT
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
    }
}
