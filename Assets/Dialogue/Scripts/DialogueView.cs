using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Dialogue.Scripts
{
    /// <summary>
    /// UI controller for displaying dialogue text and presenting selectable dialogue options.
    /// </summary>
    /// <remarks>
    /// Features:
    /// <list type="bullet">
    /// <item><description>Shows dialogue lines instantly or using a typewriter effect.</description></item>
    /// <item><description>Spawns option buttons from a prefab and raises an event when clicked.</description></item>
    /// <item><description>Temporarily disables interaction for one frame to avoid accidental selection/clicks on spawn.</description></item>
    /// </list>
    /// Typical usage:
    /// <para>
    /// Call <see cref="TypeLine"/> or <see cref="SetLineInstant"/> to show text, and
    /// <see cref="ShowOptions"/> to display choices. Subscribe to <see cref="OnOptionClicked"/>
    /// to be notified when the player selects an option.
    /// </para>
    /// </remarks>
    public class DialogueView : MonoBehaviour
    {
        // --------------------
        // UI references
        // --------------------

        [Header("UI")]
        /// <summary>
        /// Text component used to render the current dialogue line.
        /// </summary>
        public TMP_Text dialogueText;

        /// <summary>
        /// Parent transform under which option buttons are instantiated.
        /// </summary>
        public Transform optionsParent;

        /// <summary>
        /// Prefab used to create an individual option button.
        /// The prefab should include a <see cref="Button"/> and optionally a <see cref="TMP_Text"/> or <see cref="Text"/> label.
        /// </summary>
        public GameObject optionButtonPrefab;

        /// <summary>
        /// Optional group used to enable/disable option interaction and raycasts (useful for fading or blocking input).
        /// </summary>
        public CanvasGroup optionsCanvasGroup;

        // --------------------
        // Typewriter settings
        // --------------------

        [Header("Typewriter")]
        /// <summary>
        /// Typewriter speed in characters per second. If set to 0 or less, lines are shown instantly.
        /// </summary>
        [Min(0f)]
        public float charactersPerSecond = 30f;

        /// <summary>
        /// True while a typewriter coroutine is running.
        /// </summary>
        public bool IsTyping => _typingCoroutine != null;

        // --------------------
        // Events
        // --------------------

        /// <summary>
        /// Raised when a spawned option button is clicked.
        /// The provided integer is the option index (0-based).
        /// </summary>
        public event Action<int> OnOptionClicked;

        /// <summary>
        /// Callback invoked once typing completes or is hurried to completion.
        /// </summary>
        private Action _onFinishedTyping;

        // --------------------
        // Internal state
        // --------------------

        /// <summary>
        /// Active typewriter coroutine (null if not typing).
        /// </summary>
        private Coroutine _typingCoroutine;

        /// <summary>
        /// Cached full line currently being typed/displayed. Used by <see cref="HurryUp"/>.
        /// </summary>
        private string _fullLine = "";

        /// <summary>
        /// Buttons spawned by <see cref="ShowOptions"/> that should be destroyed by <see cref="ClearOptions"/>.
        /// </summary>
        private readonly List<GameObject> _spawnedButtons = new();

        /// <summary>
        /// Destroys any currently spawned option buttons and clears the internal list.
        /// </summary>
        public void ClearOptions()
        {
            foreach (var t in _spawnedButtons.Where(t => t is not null))
            {
                Destroy(t);
            }

            _spawnedButtons.Clear();
        }

        /// <summary>
        /// Stops any ongoing typewriter effect and immediately sets the dialogue text to <paramref name="text"/>.
        /// </summary>
        /// <param name="text">The full line to display. Null is treated as an empty string.</param>
        public void SetLineInstant(string text)
        {
            StopTyping();
            _fullLine = text ?? "";
            dialogueText.text = _fullLine;
        }

        /// <summary>
        /// Displays a line using a typewriter effect. When finished, invokes <paramref name="onFinished"/>.
        /// </summary>
        /// <param name="text">The line to type. Null is treated as an empty string.</param>
        /// <param name="onFinished">Callback invoked after typing completes (or is hurried to completion).</param>
        /// <remarks>
        /// This clears any previously spawned options before starting typing.
        /// </remarks>
        public void TypeLine(string text, Action onFinished)
        {
            StopTyping();
            ClearOptions();

            _fullLine = text ?? "";
            _onFinishedTyping = onFinished;

            _typingCoroutine = StartCoroutine(TypeRoutine(_fullLine));
        }

        /// <summary>
        /// If typing is currently in progress, completes it immediately, updates the text to the full line,
        /// and invokes the completion callback.
        /// </summary>
        public void HurryUp()
        {
            if (!IsTyping) return;

            StopTyping();
            dialogueText.text = _fullLine;

            var onFinished = _onFinishedTyping;
            _onFinishedTyping = null;
            onFinished?.Invoke();
        }

        /// <summary>
        /// Creates one button per option string, sets each label, and wires clicks to <see cref="OnOptionClicked"/>.
        /// </summary>
        /// <param name="options">List of option labels to present.</param>
        /// <remarks>
        /// Interaction is disabled for one frame (both at the <see cref="CanvasGroup"/> level and per-button)
        /// to prevent accidental immediate clicks/selection when the UI appears.
        /// </remarks>
        public void ShowOptions(IReadOnlyList<string> options)
        {
            ClearOptions();

            // Temporarily disable interaction for a frame (avoids instant clicks/selection on spawn)
            if (optionsCanvasGroup is not null)
            {
                optionsCanvasGroup.interactable = false;
                optionsCanvasGroup.blocksRaycasts = false;
                StartCoroutine(ReenableOptionsNextFrame());
            }

            // Clear currently selected UI element (prevents accidental submit activation)
            if (EventSystem.current is not null)
                EventSystem.current.SetSelectedGameObject(null);

            for (var i = 0; i < options.Count; i++)
            {
                var idx = i;

                var btn = Instantiate(optionButtonPrefab, optionsParent);
                _spawnedButtons.Add(btn);
                Debug.Log($"Spawned option button '{options[i]}' frame {Time.frameCount}");

                // Set label (supports either TMP child or legacy Unity UI Text)
                var tmp = btn.GetComponentInChildren<TMP_Text>();
                if (tmp != null) tmp.text = options[i];
                else
                {
                    var legacyText = btn.GetComponentInChildren<Text>();
                    if (legacyText != null) legacyText.text = options[i];
                }

                var button = btn.GetComponent<Button>();
                if (button != null)
                {
                    button.interactable = false;
                    StartCoroutine(EnableButtonNextFrame(button));
                    button.onClick.AddListener(() => OnOptionClicked?.Invoke(idx));
                }
            }
        }

        /// <summary>
        /// Enables a newly spawned <see cref="Button"/> on the next frame.
        /// </summary>
        /// <param name="button">The button to enable.</param>
        private IEnumerator EnableButtonNextFrame(Button button)
        {
            yield return null;
            if (button is not null) button.interactable = true;
        }

        /// <summary>
        /// Re-enables <see cref="optionsCanvasGroup"/> interaction and raycasts on the next frame.
        /// </summary>
        private IEnumerator ReenableOptionsNextFrame()
        {
            yield return null;
            if (optionsCanvasGroup is null) yield break;

            optionsCanvasGroup.interactable = true;
            optionsCanvasGroup.blocksRaycasts = true;
        }

        /// <summary>
        /// Stops the typewriter coroutine if it is currently running.
        /// </summary>
        public void StopTyping()
        {
            if (_typingCoroutine is null) return;

            StopCoroutine(_typingCoroutine);
            _typingCoroutine = null;
        }

        /// <summary>
        /// Coroutine that performs the typewriter effect by revealing characters over time.
        /// </summary>
        /// <param name="text">The text to type.</param>
        /// <returns>Coroutine enumerator.</returns>
        /// <remarks>
        /// If <see cref="charactersPerSecond"/> is 0 or less, the full text is displayed immediately.
        /// </remarks>
        private IEnumerator TypeRoutine(string text)
        {
            dialogueText.text = "";

            if (charactersPerSecond <= 0)
            {
                dialogueText.text = text;
                _typingCoroutine = null;

                var cb = _onFinishedTyping;
                _onFinishedTyping = null;
                cb?.Invoke();
                yield break;
            }

            var delay = 1f / charactersPerSecond;
            for (var i = 0; i < text.Length; i++)
            {
                dialogueText.text = text[..(i + 1)];
                yield return new WaitForSeconds(delay);
            }

            _typingCoroutine = null;
            var onFinished = _onFinishedTyping;
            _onFinishedTyping = null;
            onFinished?.Invoke();
        }
    }
}
