using UnityEngine;

namespace TesisQA.AI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class ExplorationTarget : MonoBehaviour
    {
        private void Awake()
        {
            Collider targetCollider = GetComponent<Collider>();
            targetCollider.isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            ExplorationAgent agent = other.GetComponentInParent<ExplorationAgent>();
            if (agent != null)
            {
                agent.ReachTarget(this);
            }
        }
    }
}
