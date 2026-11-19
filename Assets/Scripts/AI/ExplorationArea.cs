using UnityEngine;

namespace TesisQA.AI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class ExplorationArea : MonoBehaviour
    {
        [SerializeField] private string areaName;
        [SerializeField, Min(0)] private int floorIndex;

        public string AreaName => string.IsNullOrWhiteSpace(areaName)
            ? name
            : areaName;
        public int FloorIndex => floorIndex;

        public void ConfigureFloor(int value)
        {
            floorIndex = Mathf.Max(0, value);
        }

        private void Awake()
        {
            Collider areaCollider = GetComponent<Collider>();
            areaCollider.isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            ExplorationAgent agent = other.GetComponentInParent<ExplorationAgent>();
            if (agent != null)
            {
                agent.VisitArea(this);
            }
        }
    }
}
