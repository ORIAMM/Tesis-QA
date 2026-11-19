using System;
using UnityEngine;

namespace TesisQA.AI.Editor
{
    public static class ParticipantJumpChecks
    {
        public static void Run()
        {
            if (!Application.isBatchMode || !Application.dataPath.Contains(".ai-eval"))
                throw new InvalidOperationException("Use the isolated batch project.");
            ParticipantMenuPreview.Prepare();
            ParticipantGameChecks.Run();
        }

        public static string Validate()
        {
            var fixture = new GameObject("Temporary participant jump fixture");
            try
            {
                Vector3 origin = new Vector3(10000, 0, 10000);
                Platform(fixture.transform, origin + Vector3.down * .5f, new Vector3(8, 1, 8));
                Platform(fixture.transform, origin + new Vector3(0, -.5f, 16), new Vector3(8, 1, 8));
                var player = new GameObject("Jumping participant", typeof(CharacterController), typeof(AICharacterMotor));
                player.transform.SetParent(fixture.transform);
                var controller = player.GetComponent<CharacterController>();
                controller.height = 1.8f; controller.radius = .35f; controller.center = Vector3.up * .9f;
                controller.skinWidth = .08f; controller.stepOffset = .3f;
                var motor = player.GetComponent<AICharacterMotor>();
                motor.ResetMotor(origin + new Vector3(0, .08f, 3.5f), Quaternion.identity);
                Physics.SyncTransforms();
                for (int i = 0; i < 12; i++) motor.MoveParticipant(Vector2.zero, false, .02f);
                Check(motor.IsGrounded, "Jump fixture did not settle on healthy floor.");
                float peak = player.transform.position.y;
                motor.MoveParticipant(Vector2.up, true, .02f);
                Check(motor.VerticalVelocity > 0, "Space did not launch from the floor.");
                bool landed = false;
                for (int i = 0; i < 80; i++)
                {
                    // An extra press while airborne must not reset the ascent.
                    motor.MoveParticipant(Vector2.up, i == 8, .02f);
                    peak = Mathf.Max(peak, player.transform.position.y);
                    if (motor.IsGrounded && player.transform.position.z > origin.z + 12)
                    { landed = true; break; }
                }
                Check(landed, "Participant could not cross an 8 m missing-collider tile and land.");
                Check(peak > .9f && peak < 1.65f, "Jump height/double-jump would exceed the two-floor clearance.");

                Platform(fixture.transform, origin + new Vector3(0, 2.7f, 0), new Vector3(8, 1, 8));
                motor.ResetMotor(origin + Vector3.up * .08f, Quaternion.identity);
                Physics.SyncTransforms();
                for (int i = 0; i < 12; i++) motor.MoveParticipant(Vector2.zero, false, .02f);
                motor.MoveParticipant(Vector2.zero, true, .02f);
                bool hitCeiling = false;
                for (int i = 0; i < 60; i++)
                {
                    motor.MoveParticipant(Vector2.zero, false, .02f);
                    if ((motor.LastCollisionFlags & CollisionFlags.Above) != 0)
                    { hitCeiling = true; Check(motor.VerticalVelocity <= 0, "Ceiling failed to stop upward velocity."); }
                }
                Check(hitCeiling && motor.IsGrounded, "Ceiling collision failed to return player to the floor.");
                motor.ResetMotor(origin + new Vector3(0, 2, 8), Quaternion.identity);
                motor.MoveParticipant(Vector2.up, true, .02f);
                Check(motor.VerticalVelocity <= 0, "Reset/respawn retained a jump or allowed a midair launch.");
                return "PASS: Space jump crosses an 8 m collider gap and lands; peak below 1.65 m; extra midair press cannot double-jump; ceiling cancels ascent and player lands; motor reset clears jump state.\n";
            }
            finally { UnityEngine.Object.DestroyImmediate(fixture); }
        }

        private static void Platform(Transform parent, Vector3 position, Vector3 size)
        {
            var floor = new GameObject("Jump test collider", typeof(BoxCollider));
            floor.transform.SetParent(parent); floor.transform.position = position;
            floor.GetComponent<BoxCollider>().size = size;
        }

        private static void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
