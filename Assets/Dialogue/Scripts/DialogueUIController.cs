using UnityEngine;
using UnityEngine.InputSystem;

namespace Dialogue.Scripts
{
    /// <summary>
    /// High-level controller that connects a parsed dialogue file (<see cref="TextAsset"/>) to a UI view
    /// (<see cref="DialogueView"/>) and optional input actions.
    /// </summary>
    /// <remarks>
    /// Responsibilities:
    /// <list type="bullet">
    /// <item><description>Parse a dialogue file into nodes/steps and create a <c>MiniDialogueRuntime</c>.</description></item>
    /// <item><description>Subscribe to runtime events (<c>OnLine</c>, <c>OnOptions</c>, <c>OnEnd</c>, <c>OnVariableSet</c>).</description></item>
    /// <item><description>Drive the UI: type lines, show option buttons, and handle end-of-dialogue state.</description></item>
    /// <item><description>Handle player input: advance (continue) and option selection.</description></item>
    /// </list>
    /// </remarks>
    public class DialogueUIController : MonoBehaviour
    {
        /// <summary>
        /// Dialogue source file (text) to parse at startup.
        /// </summary>
        [Tooltip("TextAsset containing the dialogue source to parse.")]
        public TextAsset dialogueFile;

        [Header("Scene References")]
        /// <summary>
        /// View component responsible for rendering dialogue text and option buttons.
        /// </summary>
        [Tooltip("DialogueView that displays lines and spawns choice buttons.")]
        public DialogueView view;

        [Header("Input Actions (optional)")]
        /// <summary>
        /// Optional input action used to advance the dialogue.
        /// If not assigned, advancing can still occur via UI (e.g., clicking choices) or other scripts.
        /// </summary>
        [Tooltip("Optional InputActionReference used to advance/continue dialogue.")]
        public InputActionReference advance;

        /// <summary>
        /// Name of the node to start from when the runtime begins (e.g., \"Intro\").
        /// </summary>
        [Tooltip("Dialogue node name to start at (e.g., 'Intro').")]
        public string StartNodeName;

        /// <summary>
        /// Dialogue runtime instance that manages state, variable changes, and step progression.
        /// </summary>
        private MiniDialogueRuntime _runtime;

        // These fields are currently unused by this implementation but may be intended for future behavior.
        private bool _suppressAutoAdvanceOnce;
        private bool _choiceLocked;

        /// <summary>
        /// Enables and subscribes to the optional advance input action,
        /// and subscribes to the <see cref="DialogueView.OnOptionClicked"/> event.
        /// </summary>
        private void OnEnable()
        {
            if (advance is not null)
            {
                advance.action.Enable();
                advance.action.performed += OnAdvance;
            }

            if (view is not null)
            {
                view.OnOptionClicked += OnOptionClicked;
            }
        }

        /// <summary>
        /// Unsubscribes from input and view events and disables the optional advance action.
        /// </summary>
        private void OnDisable()
        {
            if (advance is not null)
            {
                advance.action.performed -= OnAdvance;
                advance.action.Disable();
            }

            if (view is not null)
            {
                view.OnOptionClicked -= OnOptionClicked;
            }
        }

        /// <summary>
        /// Parses the dialogue file, constructs the runtime, subscribes to runtime events,
        /// and starts the dialogue at <see cref="StartNodeName"/>.
        /// </summary>
        /// <remarks>
        /// The parser is expected to produce steps that support lines and choices.
        /// </remarks>
        private void Start()
        {
            var nodes = MiniDlgParser.ParseNodesAndLines(dialogueFile.text);
            // IMPORTANT: Use the parser that produces ChoiceStep + LineStep.
            // If your method is still called ParseNodesAndLines but supports choices, that's fine.

            _runtime = new MiniDialogueRuntime(nodes);
            _runtime.OnLine += HandleLine;
            _runtime.OnOptions += HandleOptions;
            _runtime.OnEnd += HandleEnd;
            _runtime.OnVariableSet += (name, value) =>
            {
                Debug.Log($"[VAR] {name} = {value}");
            };

            _runtime.StartNode(StartNodeName);
        }

        /// <summary>
        /// Handles a line emitted by the runtime by interpolating variables and typing it in the view.
        /// </summary>
        /// <param name="line">Raw line text emitted by the runtime.</param>
        /// <remarks>
        /// When typing finishes, this will auto-continue if the next step is a choice.
        /// This ensures options appear immediately after the final character is typed.
        /// </remarks>
        private void HandleLine(string line)
        {
            // Type the line; when typing finishes, automatically continue
            // so that if the next step is options, they appear immediately.
            var rendered = _runtime.Interpolate(line);

            view.TypeLine(rendered, onFinished: () =>
            {
                if (!_runtime.JustJumped && _runtime.IsNextStepChoice)
                {
                    _runtime.Continue();
                }
            });
        }

        /// <summary>
        /// Handles a choice list emitted by the runtime by showing option buttons in the view.
        /// </summary>
        /// <param name="options">Option label strings to display.</param>
        private void HandleOptions(System.Collections.Generic.IReadOnlyList<string> options)
        {
            _choiceLocked = false;
            view.ShowOptions(options);
        }

        /// <summary>
        /// Handles dialogue completion by showing an end marker and clearing any remaining options.
        /// </summary>
        private void HandleEnd()
        {
            view.SetLineInstant("(End)");
            view.ClearOptions();
        }

        /// <summary>
        /// Called when the player clicks an option button in the <see cref="DialogueView"/>.
        /// </summary>
        /// <param name="index">Selected option index (0-based).</param>
        /// <remarks>
        /// If a line is still typing, it is completed immediately before choosing.
        /// The actual choose call is deferred to the next frame to avoid UI event timing issues.
        /// </remarks>
        private void OnOptionClicked(int index)
        {
            Debug.Log($"OnOptionClicked index={index} frame={Time.frameCount}");

            if (view.IsTyping)
                view.HurryUp();

            StartCoroutine(ChooseNextFrame(index));
        }

        /// <summary>
        /// Defers calling <see cref="MiniDialogueRuntime.Choose(int)"/> by one frame.
        /// </summary>
        /// <param name="index">Choice index to select (0-based).</param>
        private System.Collections.IEnumerator ChooseNextFrame(int index)
        {
            yield return null;
            _runtime.Choose(index);
        }

        private void Update()
        {
            _runtime?.Tick(Time.deltaTime);
        }

        /// <summary>
        /// Input System callback for the advance action.
        /// </summary>
        /// <param name="context">Input context provided by the Input System.</param>
        private void OnAdvance(InputAction.CallbackContext context)
        {
            AdvancePressed();
        }

        /// <summary>
        /// Handles an advance request:
        /// if the view is typing, it completes the current line; otherwise it continues the runtime.
        /// </summary>
        private void AdvancePressed()
        {
            if (_runtime is null) return;

            if (view.IsTyping)
            {
                view.HurryUp();
            }
            else
            {
                _runtime.Continue();
            }
        }
    }
}
