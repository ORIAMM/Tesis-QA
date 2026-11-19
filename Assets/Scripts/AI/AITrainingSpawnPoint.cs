using UnityEngine;

namespace TesisQA.AI
{
    public enum AISpawnPointUsage
    {
        Agent,
        Target,
        Both
    }

    [DisallowMultipleComponent]
    public sealed class AITrainingSpawnPoint : MonoBehaviour
    {
        [SerializeField] private AISpawnPointUsage usage = AISpawnPointUsage.Both;

        public AISpawnPointUsage Usage => usage;

        public bool AllowsAgent => usage == AISpawnPointUsage.Agent
            || usage == AISpawnPointUsage.Both;

        public bool AllowsTarget => usage == AISpawnPointUsage.Target
            || usage == AISpawnPointUsage.Both;

        private void OnDrawGizmos()
        {
            Gizmos.color = usage == AISpawnPointUsage.Agent
                ? Color.cyan
                : usage == AISpawnPointUsage.Target
                    ? Color.yellow
                    : Color.green;
            Gizmos.DrawWireSphere(transform.position, 0.35f);
            Gizmos.DrawLine(transform.position, transform.position + transform.forward);
        }
    }
}
