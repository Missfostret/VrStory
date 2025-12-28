using UnityEngine;
using UnityEngine.InputSystem;

namespace Dialogue.Scripts
{
    /// <summary>
    /// Unity bootstrapper that loads a dialogue script from a <see cref="TextAsset"/>,
    /// creates a <c>MiniDialogueRuntime</c>, and wires player input actions to drive the dialogue.
    /// </summary>
    /// <remarks>
    /// Responsibilities:
    /// <list type="bullet">
    /// <item><description>Parse dialogue nodes/lines from <see cref="dialogueFile"/>.</description></item>
    /// <item><description>Instantiate and configure the dialogue runtime.</description></item>
    /// <item><description>Subscribe to runtime events to display output (currently via <see cref="Debug.Log"/>).</description></item>
    /// <item><description>Enable/disable input actions and map them to Continue/Choose calls.</description></item>
    /// </list>
    /// Notes:
    /// <para>Input actions are expected to come from the "Dialogue" action map.</para>
    /// <para>This component starts the dialogue at node "Intro".</para>
    /// </remarks>
    public class DialogueBootstrap : MonoBehaviour
    {
        /// <summary>
        /// Dialogue text file to parse (e.g., a .txt asset in the project).
        /// </summary>
        [Tooltip("TextAsset containing the dialogue source to parse.")]
        public TextAsset dialogueFile;

        /// <summary>
        /// Runtime instance that manages dialogue state, line progression, and options.
        /// </summary>
        private MiniDialogueRuntime _runtime;

        [Header("Input Actions (Dialogue map)")]
        /// <summary>
        /// Input action used to advance the dialogue (continue to next line or progress state).
        /// </summary>
        [SerializeField] private InputActionReference inputAdvance;

        /// <summary>
        /// Input action used to select dialogue option #1 (index 0).
        /// </summary>
        [SerializeField] private InputActionReference inputChoose1;

        /// <summary>
        /// Input action used to select dialogue option #2 (index 1).
        /// </summary>
        [SerializeField] private InputActionReference inputChoose2;

        /// <summary>
        /// Input action used to select dialogue option #3 (index 2).
        /// </summary>
        [SerializeField] private InputActionReference inputChoose3;

        /// <summary>
        /// Enables configured input actions and subscribes to their <c>performed</c> callbacks.
        /// </summary>
        private void OnEnable()
        {
            if (inputAdvance is not null)
            {
                inputAdvance.action.Enable();
                inputAdvance.action.performed += OnAdvance;
            }

            if (inputChoose1 is not null)
            {
                inputChoose1.action.Enable();
                inputChoose1.action.performed += OnChoose1;
            }

            if (inputChoose2 is not null)
            {
                inputChoose2.action.Enable();
                inputChoose2.action.performed += OnChoose2;
            }

            if (inputChoose3 is not null)
            {
                inputChoose3.action.Enable();
                inputChoose3.action.performed += OnChoose3;
            }
        }

        /// <summary>
        /// Disables configured input actions and unsubscribes from their <c>performed</c> callbacks.
        /// </summary>
        private void OnDisable()
        {
            if (inputAdvance is not null)
            {
                inputAdvance.action.Disable();
                inputAdvance.action.performed -= OnAdvance;
            }

            if (inputChoose1 is not null)
            {
                inputChoose1.action.Disable();
                inputChoose1.action.performed -= OnChoose1;
            }

            if (inputChoose2 is not null)
            {
                inputChoose2.action.Disable();
                inputChoose2.action.performed -= OnChoose2;
            }

            if (inputChoose3 is not null)
            {
                inputChoose3.action.Disable();
                inputChoose3.action.performed -= OnChoose3;
            }
        }

        /// <summary>
        /// Parses the dialogue file, constructs the runtime, subscribes to runtime events,
        /// and starts the dialogue at the "Intro" node.
        /// </summary>
        /// <remarks>
        /// Runtime events currently write to the Unity Console via <see cref="Debug.Log"/>.
        /// Replace these handlers to route output to UI (TextMeshPro, etc.).
        /// </remarks>
        private void Start()
        {
            // Parse the dialogue source into runtime nodes/lines.
            var nodes = MiniDlgParser.ParseNodesAndLines(dialogueFile.text);

            // Create the runtime that manages dialogue flow.
            _runtime = new MiniDialogueRuntime(nodes);

            // Subscribe to runtime events for output.
            _runtime.OnLine += line =>
            {
                Debug.Log("LINE: " + line);
                Debug.Log("Press Space to continue");
            };

            _runtime.OnOptions += options =>
            {
                Debug.Log("Options:");
                for (int i = 0; i < options.Count; i++)
                    Debug.Log($" {i + 1}) {options[i]}");

                Debug.Log("Press 1-9 to choose. ");
            };

            _runtime.OnEnd += () => Debug.Log("END.");
        }

        /// <summary>
        /// Input callback that advances the dialogue by calling <see cref="MiniDialogueRuntime.Continue"/>.
        /// </summary>
        /// <param name="context">Input context provided by the Input System.</param>
        private void OnAdvance(InputAction.CallbackContext context)
        {
            Debug.Log($"Advance performed (frame {Time.frameCount})");
            _runtime?.Continue();
        }

        /// <summary>
        /// Input callback that selects option 1 (index 0) via <see cref="MiniDialogueRuntime.Choose(int)"/>.
        /// </summary>
        /// <param name="context">Input context provided by the Input System.</param>
        private void OnChoose1(InputAction.CallbackContext context)
        {
            Debug.Log($"Choose1 performed (frame {Time.frameCount})");
            _runtime?.Choose(0);
        }

        /// <summary>
        /// Input callback that selects option 2 (index 1) via <see cref="MiniDialogueRuntime.Choose(int)"/>.
        /// </summary>
        /// <param name="context">Input context provided by the Input System.</param>
        private void OnChoose2(InputAction.CallbackContext context)
        {
            Debug.Log($"Choose2 performed (frame {Time.frameCount})");
            _runtime?.Choose(1);
        }

        /// <summary>
        /// Input callback that selects option 3 (index 2) via <see cref="MiniDialogueRuntime.Choose(int)"/>.
        /// </summary>
        /// <param name="context">Input context provided by the Input System.</param>
        private void OnChoose3(InputAction.CallbackContext context)
        {
            Debug.Log($"Choose3 performed (frame {Time.frameCount})");
            _runtime?.Choose(2);
        }
    }
}