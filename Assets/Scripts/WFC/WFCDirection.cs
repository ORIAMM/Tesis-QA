using UnityEngine;

namespace TesisQA.WFC
{
    public enum WFCDirection
    {
        North,
        East,
        South,
        West,
        Up,
        Down
    }

    public static class WFCDirectionUtility
    {
        public const int DirectionCount = 6;

        public static Vector3Int ToOffset(this WFCDirection direction)
        {
            switch (direction)
            {
                case WFCDirection.North: return Vector3Int.forward;
                case WFCDirection.East: return Vector3Int.right;
                case WFCDirection.South: return Vector3Int.back;
                case WFCDirection.West: return Vector3Int.left;
                case WFCDirection.Up: return Vector3Int.up;
                case WFCDirection.Down: return Vector3Int.down;
                default: return Vector3Int.zero;
            }
        }

        public static WFCDirection Opposite(this WFCDirection direction)
        {
            switch (direction)
            {
                case WFCDirection.North: return WFCDirection.South;
                case WFCDirection.East: return WFCDirection.West;
                case WFCDirection.South: return WFCDirection.North;
                case WFCDirection.West: return WFCDirection.East;
                case WFCDirection.Up: return WFCDirection.Down;
                case WFCDirection.Down: return WFCDirection.Up;
                default: return direction;
            }
        }

        public static WFCDirection RotateY(this WFCDirection direction, int quarterTurns)
        {
            if (direction == WFCDirection.Up || direction == WFCDirection.Down)
            {
                return direction;
            }

            int turns = ((quarterTurns % 4) + 4) % 4;
            return (WFCDirection)(((int)direction + turns) % 4);
        }

        public static Vector3Int RotateCellY(Vector3Int cell, Vector3Int originalSize, int quarterTurns)
        {
            switch (((quarterTurns % 4) + 4) % 4)
            {
                case 1:
                    return new Vector3Int(cell.z, cell.y, originalSize.x - 1 - cell.x);
                case 2:
                    return new Vector3Int(
                        originalSize.x - 1 - cell.x,
                        cell.y,
                        originalSize.z - 1 - cell.z);
                case 3:
                    return new Vector3Int(originalSize.z - 1 - cell.z, cell.y, cell.x);
                default:
                    return cell;
            }
        }

        public static Vector3Int RotatedSize(Vector3Int originalSize, int quarterTurns)
        {
            int turns = ((quarterTurns % 4) + 4) % 4;
            return turns % 2 == 0
                ? originalSize
                : new Vector3Int(originalSize.z, originalSize.y, originalSize.x);
        }
    }
}
