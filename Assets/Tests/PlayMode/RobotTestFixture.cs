using System;
using System.Collections;
using NUnit.Framework;
using MovementSandbox.Identity;
using MovementSandbox.Mechanics;
using StarterAssets;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MovementSandbox.Tests
{
    /// <summary>
    /// Base class for robot tests: plays the real <c>Sandbox</c> scene with a virtual keyboard and mouse,
    /// so every key press travels the same path a player's does (PlayerInput, StarterAssetsInputs, then the
    /// identity scripts). A test that only poked the scripts directly would miss input no longer reaching the
    /// player.
    ///
    /// <see cref="InputTestFixture"/> swaps in an isolated input system for each test and restores the real one
    /// afterwards, so the physical keyboard cannot interfere with a run, and a run cannot leave keys held.
    /// </summary>
    public abstract class RobotTestFixture : InputTestFixture
    {
        protected const string TestScene = "Sandbox";

        /// <summary>How long a freshly loaded player gets to land and settle before a test starts.</summary>
        const float SettleTimeout = 5f;

        protected Keyboard Keyboard { get; private set; }
        protected Mouse Mouse { get; private set; }
        protected PlayerModes Player { get; private set; }

        protected Vector3 PlayerPosition => Player.transform.position;

        /// <summary>What the player's scripts read as input: where a key press is supposed to arrive.</summary>
        StarterAssetsInputs inputs;

        /// <summary>
        /// Loads the test scene with the virtual devices already plugged in, then waits for the player to be
        /// standing. The devices come first: PlayerInput pairs with whatever devices exist when it is enabled.
        /// </summary>
        protected IEnumerator LoadTestScene()
        {
            // A headless run has no Game view to hold focus. By default the Editor only routes keyboard input
            // to the game while the Game view is focused, and resets devices when focus is lost, so without
            // this the virtual keys stop arriving after the first test.
            InputSystem.settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;

            Keyboard = InputSystem.AddDevice<Keyboard>();
            Mouse = InputSystem.AddDevice<Mouse>();

            yield return SceneManager.LoadSceneAsync(TestScene, LoadSceneMode.Single);

            Player = Object.FindAnyObjectByType<PlayerModes>();
            Assert.That(Player, Is.Not.Null, $"{TestScene} has no player (PlayerModes) in it.");
            inputs = Player.GetComponent<StarterAssetsInputs>();

            var body = Player.GetComponent<CharacterController>();
            yield return WaitUntil(() => Player.Current == PlayerMode.Walking && body.isGrounded,
                SettleTimeout, "the player to land at the spawn point");
        }

        /// <summary>
        /// Switches the test scene off before the real input system comes back. The scene's PlayerInput was set
        /// up against this test's isolated input system, and shutting it down against the next one throws.
        /// </summary>
        public override void TearDown()
        {
            Scene scene = SceneManager.GetSceneByName(TestScene);
            if (scene.isLoaded)
            {
                foreach (GameObject root in scene.GetRootGameObjects())
                    root.SetActive(false);
            }

            base.TearDown();
        }

        /// <summary>
        /// Holds <paramref name="key"/> until <paramref name="condition"/> is true, then lets go and waits a
        /// frame, so a press straight afterwards reaches the scripts as a new press rather than landing in the
        /// same frame as the release.
        /// </summary>
        protected IEnumerator HoldUntil(ButtonControl key, Func<bool> condition, float timeout, string waitingFor)
        {
            Press(key);
            yield return WaitUntil(condition, timeout, waitingFor);
            Release(key);
            yield return null;
        }

        /// <summary>
        /// Waits until a jump would actually fire. The base controller ignores Space until the player has
        /// been on the ground for its whole jump cooldown, and any moment in the air (including the small drop
        /// after a teleport) restarts that cooldown.
        /// </summary>
        protected IEnumerator WaitUntilReadyToJump()
        {
            var body = Player.GetComponent<CharacterController>();
            float cooldown = Player.GetComponent<ThirdPersonController>().JumpTimeout;
            float groundedFor = 0f;
            yield return WaitUntil(() =>
            {
                groundedFor = body.isGrounded ? groundedFor + Time.deltaTime : 0f;
                return groundedFor > cooldown + 0.05f;
            }, cooldown + 3f, "the player to stand on the ground long enough to jump");
        }

        /// <summary>Holds <paramref name="key"/> for a set time, then lets go and waits a frame.</summary>
        protected IEnumerator HoldFor(ButtonControl key, float seconds)
        {
            Press(key);
            yield return new WaitForSeconds(seconds);
            Release(key);
            yield return null;
        }

        /// <summary>
        /// The player's average ground speed, ignoring height, over <paramref name="seconds"/> of whatever
        /// keys are currently held.
        /// </summary>
        protected IEnumerator MeasureFlatSpeed(float seconds, Action<float> result)
        {
            Vector3 start = Vector3.ProjectOnPlane(PlayerPosition, Vector3.up);
            float startTime = Time.time;
            yield return new WaitForSeconds(seconds);
            Vector3 end = Vector3.ProjectOnPlane(PlayerPosition, Vector3.up);
            result(Vector3.Distance(start, end) / Mathf.Max(Time.time - startTime, 1e-3f));
        }

        /// <summary>
        /// The route from the spawn point onto a wire: hold W to climb Pole_01 to the perch, then a fresh W
        /// to mount the wire the camera faces and ride 3 m out. Held, not tapped, because a player that
        /// stops at the very end of a wire steps back onto the perch.
        /// </summary>
        protected IEnumerator RideOutOntoAWire()
        {
            yield return HoldUntil(Keyboard.wKey, () => Player.Current == PlayerMode.Perched, 15f,
                "the player to climb Pole_01 to the perch");

            Vector3 perch = PlayerPosition;
            yield return HoldUntil(Keyboard.wKey,
                () => Player.Current == PlayerMode.WireRiding && Vector3.Distance(PlayerPosition, perch) > 3f,
                3f, "the player to mount a wire from the perch and ride 3 m along it");
        }

        /// <summary>
        /// A short press, held for a few frames so scripts that look for a press edge in Update see it.
        /// </summary>
        protected IEnumerator Tap(ButtonControl key)
        {
            Press(key);
            yield return null;
            yield return null;
            Release(key);
            yield return null;
        }

        /// <summary>
        /// Waits frame by frame for <paramref name="condition"/>, failing with the player's mode and
        /// position if it never comes, so a failure says where the run went wrong, not only that it did.
        /// </summary>
        protected IEnumerator WaitUntil(Func<bool> condition, float timeout, string waitingFor)
        {
            float deadline = Time.time + timeout;
            while (!condition())
            {
                if (Time.time > deadline)
                    Assert.Fail($"Gave up after {timeout} s waiting for {waitingFor}. " +
                                $"The player is {Player.Current} at {PlayerPosition}, " +
                                $"reading move {inputs.move} and jump {inputs.jump}; " +
                                $"the robot is holding W: {Keyboard.wKey.isPressed}.");
                yield return null;
            }
        }
    }
}
