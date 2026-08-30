using UnityEngine;

public static class AttackRangeVisualizer
{
    public static void DrawBoxRange(Vector3 center, Vector3 size, Quaternion rotation, Color color, float duration = 0.05f)
    {
        Vector3 half = size * 0.5f;
        Vector3[] corners = new Vector3[8]
        {
            new Vector3(-half.x, -half.y, -half.z),
            new Vector3(half.x, -half.y, -half.z),
            new Vector3(half.x, -half.y, half.z),
            new Vector3(-half.x, -half.y, half.z),
            new Vector3(-half.x, half.y, -half.z),
            new Vector3(half.x, half.y, -half.z),
            new Vector3(half.x, half.y, half.z),
            new Vector3(-half.x, half.y, half.z)
        };

        for (int i = 0; i < 8; i++)
            corners[i] = rotation * corners[i] + center;

        int[,] edges =
        {
            {0,1}, {1,2}, {2,3}, {3,0},
            {4,5}, {5,6}, {6,7}, {7,4},
            {0,4}, {1,5}, {2,6}, {3,7}
        };

        for (int i = 0; i < edges.GetLength(0); i++)
        {
            int a = edges[i, 0];
            int b = edges[i, 1];
            Debug.DrawLine(corners[a], corners[b], color, duration);
        }
    }
}
