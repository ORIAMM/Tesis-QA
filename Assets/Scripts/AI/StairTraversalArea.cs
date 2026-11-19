using UnityEngine;

namespace TesisQA.AI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class StairTraversalArea : MonoBehaviour
    {
        private Collider triggerCollider;

        private void Awake()
        {
            triggerCollider = GetComponent<Collider>();
            triggerCollider.isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            ExplorationAgent agent = other.GetComponentInParent<ExplorationAgent>();
            if (agent != null)
            {
                agent.VisitStair(this);
            }
        }

        private void OnTriggerStay(Collider other)
        {
            ExplorationAgent agent = other.GetComponentInParent<ExplorationAgent>();
            if (agent == null || triggerCollider == null)
            {
                return;
            }

            float normalizedHeight = Mathf.InverseLerp(
                triggerCollider.bounds.min.y,
                triggerCollider.bounds.max.y,
                agent.transform.position.y);
            if (normalizedHeight >= 0.35f && normalizedHeight <= 0.65f)
            {
                agent.VisitStairMidpoint(this);
            }
        }
    }
}
