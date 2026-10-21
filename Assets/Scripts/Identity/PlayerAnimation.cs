using UnityEngine;

namespace MovementSandbox.Identity
{
    /// <summary>
    /// The player's Animator parameters and the poses the identity modes share. The base controller
    /// drives the Animator while walking; a mode that takes the body over (climbing, riding, charging a
    /// slingshot, ragdoll recovery, respawn) goes through here instead, so a renamed parameter or clip is
    /// one edit. Every method accepts a null Animator and does nothing.
    /// </summary>
    public static class PlayerAnimation
    {
        static readonly int Speed = Animator.StringToHash("Speed");
        static readonly int MotionSpeed = Animator.StringToHash("MotionSpeed");
        static readonly int Jump = Animator.StringToHash("Jump");
        static readonly int FreeFall = Animator.StringToHash("FreeFall");
        static readonly int Grounded = Animator.StringToHash("Grounded");
        static readonly int Dead = Animator.StringToHash("Dead");
        static readonly int IdleState = Animator.StringToHash("AnimalArmature|Idle");

        /// <summary>
        /// A still Idle with the locomotion parameters cleared, for the moment a mode takes the body.
        /// Without it the Animator keeps whatever locomotion state was active when base locomotion switched
        /// off: a walk frozen mid-stride, or FreeFall still set from the jump just before a grab.
        /// </summary>
        public static void EnterStillPose(Animator animator)
        {
            if (animator == null)
                return;

            animator.SetFloat(Speed, 0f);
            animator.SetFloat(MotionSpeed, 0f);
            animator.SetBool(Jump, false);
            animator.SetBool(FreeFall, false);
            animator.SetBool(Grounded, true);
            animator.Play(IdleState);
        }

        /// <summary>
        /// Plays the walk cycle while travelling and holds Idle otherwise. The Fox has no climb or ride
        /// clip, so the walk stands in for both; <paramref name="speed"/> only has to land in the walk band
        /// of the blend tree, not match how fast the body really moves.
        /// </summary>
        public static void SetTravelling(Animator animator, bool moving, float speed)
        {
            if (animator == null)
                return;

            animator.SetFloat(Speed, moving ? speed : 0f);
            animator.SetFloat(MotionSpeed, moving ? 1f : 0f);
        }

        /// <summary>
        /// Cuts straight to Idle, clearing Jump so a body that was mid-air does not go on into the jump
        /// state. Used after a respawn or a ragdoll recovery, where the pose must change on this exact frame.
        /// </summary>
        public static void PlayIdle(Animator animator)
        {
            if (animator == null)
                return;

            animator.SetBool(Jump, false);
            animator.Play(IdleState);
        }

        public static void PlayDeath(Animator animator)
        {
            if (animator != null)
                animator.SetTrigger(Dead);
        }
    }
}
