using System;
using UnityEngine;

namespace TesisQA.AI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class AICharacterMotor : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField, Min(0.1f)] private float moveSpeed = 4f;
        [SerializeField, Min(1f)] private float rotationSpeed = 180f;
        [SerializeField] private float gravity = -20f;
        [SerializeField] private float groundedVerticalSpeed = -2f;
        [SerializeField] private float terminalVelocity = -25f;

        [Header("Participant jump (human controls only)")]
        [SerializeField, Min(.1f)] private float participantJumpHeight = 1.5f;
        [SerializeField, Min(1f)] private float participantJumpDistance = 10f;

        [Header("Ground Probe")]
        [SerializeField] private LayerMask groundMask = ~0;
        [SerializeField, Min(0.01f)] private float groundProbeDistance = 0.3f;

        private CharacterController controller;
        private float verticalVelocity;
        private Vector3 lastVelocity;
        private CollisionFlags lastCollisionFlags;
        private float jumpBufferSeconds, unsupportedSeconds = float.PositiveInfinity;
        private bool participantJumpInProgress, participantHasMovedSinceReset;

        public event Action<ControllerColliderHit> ControllerHit;

        public float MoveSpeed => moveSpeed;
        public float ParticipantJumpHeight => participantJumpHeight;
        public float ParticipantJumpDistance => participantJumpDistance;
        public float VerticalVelocity => verticalVelocity;
        public Vector3 Velocity => lastVelocity;
        public CollisionFlags LastCollisionFlags => lastCollisionFlags;
        public bool IsGrounded => controller != null
            && (controller.isGrounded || (lastCollisionFlags & CollisionFlags.Below) != 0);

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
        }

        public void Move(float forwardInput, float turnInput, float deltaTime)
        {
            if (controller == null || !controller.enabled || deltaTime <= 0f)
            {
                return;
            }

            forwardInput = Mathf.Clamp(forwardInput, -1f, 1f);
            turnInput = Mathf.Clamp(turnInput, -1f, 1f);

            transform.Rotate(0f, turnInput * rotationSpeed * deltaTime, 0f, Space.Self);

            ApplyMotion(transform.forward * (forwardInput * moveSpeed), deltaTime);
        }

        // Human first-person controls use local strafing; the learned policy keeps
        // its original forward/turn action space in Move().
        public void MovePlanar(Vector2 localInput, float deltaTime)
        {
            if (controller == null || !controller.enabled || deltaTime <= 0f) return;
            localInput = Vector2.ClampMagnitude(localInput, 1f);
            ApplyMotion((transform.right * localInput.x + transform.forward * localInput.y) * moveSpeed, deltaTime);
        }

        // Space gives the participant a low, long jump across one 8 m WFC tile.
        // Move() and MovePlanar() retain their original policy/legacy physics.
        public void MoveParticipant(Vector2 localInput, bool jumpPressed, float deltaTime)
        {
            if (controller == null || !controller.enabled || deltaTime <= 0f) return;
            localInput = Vector2.ClampMagnitude(localInput, 1f);
            // CharacterController.isGrounded can retain its pre-teleport value
            // until the first Move. Only trust contact after a post-reset move.
            if (participantHasMovedSinceReset && IsGrounded && verticalVelocity <= 0)
            {
                unsupportedSeconds = 0;
                participantJumpInProgress = false;
            }
            else unsupportedSeconds += deltaTime;
            jumpBufferSeconds = jumpPressed ? .15f : Mathf.Max(0, jumpBufferSeconds - deltaTime);
            float launchSpeed = Mathf.Sqrt(-2 * gravity * participantJumpHeight);
            if (jumpBufferSeconds > 0 && unsupportedSeconds <= .1f && !participantJumpInProgress)
            {
                verticalVelocity = launchSpeed;
                participantJumpInProgress = true;
                jumpBufferSeconds = 0;
                unsupportedSeconds = float.PositiveInfinity;
            }
            float speed = participantJumpInProgress ? participantJumpDistance / (2 * launchSpeed / -gravity) : moveSpeed;
            ApplyMotion((transform.right * localInput.x + transform.forward * localInput.y) * speed, deltaTime);
            participantHasMovedSinceReset = true;
            if ((lastCollisionFlags & CollisionFlags.Above) != 0 && verticalVelocity > 0) verticalVelocity = 0;
        }

        public void ClearParticipantJumpBuffer() => jumpBufferSeconds = 0;

        private void ApplyMotion(Vector3 motion, float deltaTime)
        {
            if (IsGrounded && verticalVelocity < 0f)
            {
                verticalVelocity = groundedVerticalSpeed;
            }
            else
            {
                verticalVelocity = Mathf.Max(
                    terminalVelocity,
                    verticalVelocity + gravity * deltaTime);
            }

            motion.y = verticalVelocity;
            lastCollisionFlags = controller.Move(motion * deltaTime);
            lastVelocity = controller.velocity;

            if ((lastCollisionFlags & CollisionFlags.Below) != 0 && verticalVelocity < 0f)
            {
                verticalVelocity = groundedVerticalSpeed;
            }
        }

        public bool TryGetGroundSlope(out float angleDegrees)
        {
            angleDegrees = 0f;
            if (controller == null)
            {
                return false;
            }

            Vector3 origin = transform.position
                + Vector3.up * (controller.radius + controller.skinWidth + 0.05f);
            float radius = Mathf.Max(0.05f, controller.radius * 0.8f);
            if (!Physics.SphereCast(
                    origin,
                    radius,
                    Vector3.down,
                    out RaycastHit hit,
                    groundProbeDistance + controller.radius,
                    groundMask,
                    QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            angleDegrees = Vector3.Angle(hit.normal, Vector3.up);
            return true;
        }

        public void ResetMotor(Vector3 feetPosition, Quaternion rotation)
        {
            if (controller == null)
            {
                controller = GetComponent<CharacterController>();
            }

            bool wasEnabled = controller.enabled;
            controller.enabled = false;
            transform.SetPositionAndRotation(feetPosition, rotation);
            controller.enabled = wasEnabled;

            verticalVelocity = groundedVerticalSpeed;
            lastVelocity = Vector3.zero;
            lastCollisionFlags = CollisionFlags.None;
            jumpBufferSeconds = 0;
            unsupportedSeconds = float.PositiveInfinity;
            participantJumpInProgress = false;
            participantHasMovedSinceReset = false;
        }

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            ControllerHit?.Invoke(hit);
        }

        private void OnValidate()
        {
            moveSpeed = Mathf.Max(0.1f, moveSpeed);
            rotationSpeed = Mathf.Max(1f, rotationSpeed);
            gravity = Mathf.Min(-0.01f, gravity);
            groundedVerticalSpeed = Mathf.Min(-0.01f, groundedVerticalSpeed);
            terminalVelocity = Mathf.Min(groundedVerticalSpeed, terminalVelocity);
            groundProbeDistance = Mathf.Max(0.01f, groundProbeDistance);
        }
    }
}
