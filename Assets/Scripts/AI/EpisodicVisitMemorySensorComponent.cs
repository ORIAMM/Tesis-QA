using System.Collections.Generic;
using Unity.MLAgents.Sensors;
using UnityEngine;

namespace TesisQA.AI
{
    // A local, agent-centric record of places actually traversed this episode.
    // It never reads the WFC topology or marks unseen cells as walkable.
    [DisallowMultipleComponent]
    public sealed class EpisodicVisitMemorySensorComponent : SensorComponent
    {
        public const int GridWidth = 7;
        public const int ObservationSize = GridWidth * GridWidth;

        private const float HorizontalCellSize = 8f;
        private const float FloorHeight = 4f;
        private const float MinimumCellTransitionDistance = 0.75f;
        private const int VisitCountCap = 4;
        private const string SensorName = "Explorer Visit Memory";

        private readonly Dictionary<Vector3Int, int> visits =
            new Dictionary<Vector3Int, int>();
        private Vector3Int lastCell;
        private Vector3 lastRecordedPosition;
        private bool hasLastCell;

        public override ISensor[] CreateSensors()
        {
            return new ISensor[] { new VisitMemorySensor(this) };
        }

        public void ResetEpisode(Vector3 spawnPosition)
        {
            visits.Clear();
            hasLastCell = false;
            RecordPosition(spawnPosition);
        }

        public void RecordPosition(Vector3 position)
        {
            Vector3Int cell = ToCell(position);
            if (hasLastCell && cell == lastCell)
            {
                return;
            }
            if (hasLastCell
                && Vector3.Distance(position, lastRecordedPosition)
                    < MinimumCellTransitionDistance)
            {
                return;
            }

            visits.TryGetValue(cell, out int previousVisits);
            visits[cell] = Mathf.Min(previousVisits + 1, VisitCountCap);
            lastCell = cell;
            lastRecordedPosition = position;
            hasLastCell = true;
        }

        private static Vector3Int ToCell(Vector3 position)
        {
            return new Vector3Int(
                Mathf.RoundToInt(position.x / HorizontalCellSize),
                Mathf.RoundToInt(position.y / FloorHeight),
                Mathf.RoundToInt(position.z / HorizontalCellSize));
        }

        private sealed class VisitMemorySensor : ISensor
        {
            private readonly EpisodicVisitMemorySensorComponent owner;

            public VisitMemorySensor(EpisodicVisitMemorySensorComponent owner)
            {
                this.owner = owner;
            }

            public ObservationSpec GetObservationSpec()
            {
                return ObservationSpec.Vector(ObservationSize);
            }

            public int Write(ObservationWriter writer)
            {
                Vector3Int center = ToCell(owner.transform.position);
                int facing = Mathf.RoundToInt(
                    owner.transform.eulerAngles.y / 90f) & 3;
                int index = 0;
                int radius = GridWidth / 2;

                // First row is in front of the agent; columns run left to right.
                for (int forward = radius; forward >= -radius; forward--)
                {
                    for (int right = -radius; right <= radius; right++)
                    {
                        int worldX;
                        int worldZ;
                        switch (facing)
                        {
                            case 1:
                                worldX = forward;
                                worldZ = -right;
                                break;
                            case 2:
                                worldX = -right;
                                worldZ = -forward;
                                break;
                            case 3:
                                worldX = -forward;
                                worldZ = right;
                                break;
                            default:
                                worldX = right;
                                worldZ = forward;
                                break;
                        }

                        Vector3Int cell = new Vector3Int(
                            center.x + worldX, center.y, center.z + worldZ);
                        owner.visits.TryGetValue(cell, out int count);
                        writer[index++] = count / (float)VisitCountCap;
                    }
                }

                return ObservationSize;
            }

            public byte[] GetCompressedObservation() => null;
            public CompressionSpec GetCompressionSpec() => CompressionSpec.Default();
            public string GetName() => SensorName;
            public void Update() { }
            public void Reset() { }
        }
    }
}
