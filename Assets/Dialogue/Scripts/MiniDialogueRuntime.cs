using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Dialogue.Scripts
{
    /// <summary>
    /// Executes parsed dialogue nodes and emits events for lines, options, variables, and end-of-dialogue.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The runtime processes a <see cref="MiniNode"/> step-by-step:
    /// <see cref="LineStep"/> triggers <see cref="OnLine"/>, <see cref="ChoiceStep"/> triggers <see cref="OnOptions"/>,
    /// and <see cref="CommandStep"/> executes an <see cref="ICommand"/> immediately.
    /// </para>
    ///
    /// <para>
    /// This class is UI-agnostic: it only raises events and exposes methods like <see cref="Continue"/> and <see cref="Choose"/>.
    /// A view/controller (e.g., <c>DialogueUIController</c>) is expected to subscribe and drive presentation and input.
    /// </para>
    ///
    /// <para><b>Variables</b></para>
    /// <para>
    /// Variables are stored in a case-insensitive dictionary and can be substituted into lines using <see cref="Interpolate"/>.
    /// Supported pattern: <c>{varName}</c>.
    /// </para>
    /// </remarks>
    public class MiniDialogueRuntime
    {
        /// <summary>
        /// Raised when the runtime reaches a <see cref="LineStep"/>.
        /// The string is the raw line text (not automatically interpolated).
        /// </summary>
        public event Action<string> OnLine;

        /// <summary>
        /// Raised when the runtime reaches a <see cref="ChoiceStep"/>.
        /// The list contains the display labels for available choices (filtered by conditions).
        /// </summary>
        public event Action<IReadOnlyList<string>> OnOptions;

        /// <summary>
        /// Raised when the current node finishes or the runtime has no more steps to process.
        /// </summary>
        public event Action OnEnd;

        /// <summary>
        /// Raised when a variable is set via a <see cref="SetCommand"/>.
        /// </summary>
        public event Action<string, object> OnVariableSet;

        /// <summary>
        /// All known dialogue nodes, keyed by node name (case-insensitive).
        /// </summary>
        private readonly Dictionary<string, MiniNode> _nodes;

        /// <summary>
        /// Currently executing node.
        /// </summary>
        private MiniNode _current;

        /// <summary>
        /// Index of the next step to execute within <see cref="_current"/>.
        /// </summary>
        private int _index;
        
        private readonly ConditionEvaluator _conditionEvaluator;

        /// <summary>
        /// Fired when a timed choice begins.
        /// </summary>
        /// <remarks>
        /// The float parameter represents the initial time limit in seconds.
        /// </remarks>
        public event Action<float> OnChoiceTimerStarted;

        /// <summary>
        /// Fired every frame while a timed choice is active
        /// </summary>
        /// <remarks>
        /// The float parameter represents the remaining time in seconds
        /// </remarks>
        public event Action<float> OnChoiceTimerUpdated;

        /// <summary>
        /// Fired when a timed choice expires and an automatic selection occurs.
        /// </summary>
        public event Action OnChoiceTimerEnded;

        /// <summary>
        /// True immediately after a <see cref="Jump(string)"/> occurs and until a <see cref="LineStep"/> is emitted.
        /// Useful for UI logic that needs to know a jump just happened.
        /// </summary>
        public bool JustJumped { get; private set; }

        /// <summary>
        /// Currently displayed (filtered) choices for the active <see cref="ChoiceStep"/>.
        /// </summary>
        private List<Choice> _displayedChoices;

        /// <summary>
        /// Non-null while waiting for the user to select a choice.
        /// When set, <see cref="Continue"/> will do nothing until <see cref="Choose(int)"/> is called.
        /// </summary>
        private ChoiceStep _waitingChoiceStep;

        /// <summary>
        /// Runtime variable store (case-insensitive keys).
        /// </summary>
        private readonly Dictionary<string, object> _vars = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Indicates whether a timed choice countdown is currently active.
        /// </summary>
        /// <remarks>
        /// This flag is set when a <see cref="ChoiceStep"/> with a time limit
        /// is presented to the player, and cleared when either:
        /// <list type="bullet">
        /// <item><description>The player selects a choice</description></item>
        /// <item><description>The time limit expires and an automatic selection occurs</description></item>
        /// <item><description>The dialogue flow leaves the choice step</description></item>
        /// </list>
        /// </remarks>
        private bool _choiceTimerRunning;
        
        /// <summary>
        /// Remaining time (in seconds) for the currently active timed choice.
        /// </summary>
        /// <remarks>
        /// This value is initialized from <see cref="ChoiceStep.TimeLimitSeconds"/>
        /// when the choices are shown, and is decremented each frame via <see cref="Tick(float)"/>.
        /// When it reaches zero, the runtime automatically selects the default choice.
        /// </remarks>
        private float _choiceTimeRemaining;

        /// <summary>
        /// Regex pattern used for variable interpolation: <c>{varName}</c>.
        /// Variable names must start with a letter and may contain letters, digits, and underscores.
        /// </summary>
        private static readonly Regex VarPattern =
            new Regex(@"\{([A-Za-z][A-Za-z0-9_]*)\}", RegexOptions.Compiled);

        /// <summary>
        /// Replaces any <c>{varName}</c> tokens with values from the runtime variable store.
        /// </summary>
        /// <param name="text">Input text possibly containing interpolation tokens.</param>
        /// <returns>
        /// Interpolated text. If a variable is missing or null, the original token is left unchanged.
        /// </returns>
        /// <remarks>
        /// Formatting rules:
        /// <list type="bullet">
        /// <item><description><see cref="bool"/> becomes "true"/"false".</description></item>
        /// <item><description><see cref="double"/> and <see cref="float"/> use invariant culture.</description></item>
        /// <item><description>Other types use <see cref="object.ToString"/>.</description></item>
        /// </list>
        /// </remarks>
        public string Interpolate(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            return VarPattern.Replace(text, match =>
            {
                var varName = match.Groups[1].Value;
                if (!_vars.TryGetValue(varName, out var value) || value is null)
                    return match.Value;

                // Customize formatting per type here
                return value switch
                {
                    bool b => b ? "true" : "false",
                    double d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    float f => f.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    _ => value.ToString()
                };
            });
        }

        /// <summary>
        /// Advances internal timers and time-based state.
        /// </summary>
        /// <param name="deltaTime">
        /// Time elapsed since the previous tick, typically <see cref="UnityEngine.Time.deltaTime"/>.
        /// </param>
        /// <remarks>
        /// This method must be called regularly (e.g. once per frame) by the host application.
        /// It is responsible for expiring timed choices and triggering automatic selection.
        /// </remarks>
        public void Tick(float deltaTime)
        {
            if (!_choiceTimerRunning) return;
            if (_waitingChoiceStep is null)
            {
                _choiceTimerRunning = false;
                return;
            }
            
            _choiceTimeRemaining -= deltaTime;
            if (_choiceTimeRemaining < 0f)
                _choiceTimeRemaining = 0f;

            OnChoiceTimerUpdated?.Invoke(_choiceTimeRemaining);

            if (_choiceTimeRemaining <= 0f)
            {
                _choiceTimerRunning = false;
                OnChoiceTimerEnded?.Invoke();
                
                // Auto-pick default option (displayed index)
                Choose(_waitingChoiceStep.DefaultOptionIndex);
            }
        }

        /// <summary>
        /// Gets a variable value typed as <typeparamref name="T"/>, or returns <paramref name="defaultValue"/>
        /// if the variable is not present or has a different type.
        /// </summary>
        /// <typeparam name="T">Expected variable type.</typeparam>
        /// <param name="name">Variable name.</param>
        /// <param name="defaultValue">Value to return when missing or incompatible.</param>
        public T Get<T>(string name, T defaultValue = default)
        {
            if (_vars.TryGetValue(name, out var v) && v is T t) return t;
            return defaultValue;
        }

        /// <summary>
        /// Sets a runtime variable to the specified value.
        /// </summary>
        /// <param name="name">Variable name.</param>
        /// <param name="value">Value to store.</param>
        /// <remarks>
        /// This does not raise <see cref="OnVariableSet"/>; that event is raised only for <see cref="SetCommand"/>.
        /// </remarks>
        public void Set(string name, object value) => _vars[name] = value;

        /// <summary>
        /// Returns true if the variable exists in the store (regardless of its value).
        /// </summary>
        public bool Has(string name) => _vars.ContainsKey(name);

        /// <summary>
        /// Creates a new runtime for the given parsed node dictionary.
        /// </summary>
        /// <param name="nodes">Parsed dialogue nodes keyed by node name.</param>
        public MiniDialogueRuntime(Dictionary<string, MiniNode> nodes, ConditionEvaluator conditionEvaluator = null)
        {
            _nodes = nodes;
            _conditionEvaluator = conditionEvaluator ?? new ConditionEvaluator();
        }

        /// <summary>
        /// Starts execution at the given node name and immediately advances until the first line/options/end is reached.
        /// </summary>
        /// <param name="nodeName">Name of the node to start at.</param>
        public void StartNode(string nodeName)
        {
            Jump(nodeName);
            Continue();
        }

        /// <summary>
        /// Continues execution until the next visible output boundary:
        /// a line (<see cref="OnLine"/>), options (<see cref="OnOptions"/>), or end (<see cref="OnEnd"/>).
        /// </summary>
        /// <remarks>
        /// If the runtime is currently waiting for a choice selection (<see cref="_waitingChoiceStep"/>),
        /// this method returns immediately without doing anything.
        /// </remarks>
        public void Continue()
        {
            if (_waitingChoiceStep is not null)
                return;

            while (true)
            {
                if (_current is null || _index >= _current.Steps.Count)
                {
                    OnEnd?.Invoke();
                    return;
                }

                var step = _current.Steps[_index++];

                if (step is LineStep ls)
                {
                    JustJumped = false;
                    OnLine?.Invoke(ls.Text);
                    return;
                }

                if (step is CommandStep cmdStep)
                {
                    Execute(cmdStep.Command);
                    continue; // keep going until we hit a line or options
                }

                if (step is ChoiceStep cs)
                {
                    _waitingChoiceStep = cs;
                    
                    _displayedChoices = new List<Choice>();
                    var labels = new List<string>();

                    foreach (var c in cs.Choices.Where(IsChoiceAvailable))
                    {
                        _displayedChoices.Add(c);
                        labels.Add(c.Text);
                    }
                    
                    UnityEngine.Debug.Log(
                        $"[CHOICE] node='{_current?.Name}' " +
                        $"choicesShown={labels.Count} timeLimit={(cs.TimeLimitSeconds.HasValue ? cs.TimeLimitSeconds.Value.ToString() : "none")}"
                    );

                    if (labels.Count is 0)
                        throw new Exception($"No available choices at node '{_current.Name}'.");

                    OnOptions?.Invoke(labels);
                    if (_waitingChoiceStep.TimeLimitSeconds.HasValue)
                    {
                        _choiceTimeRemaining = _waitingChoiceStep.TimeLimitSeconds.Value;
                        _choiceTimerRunning = true;
                        OnChoiceTimerStarted?.Invoke(_choiceTimeRemaining);
                    }
                    else
                    {
                        _choiceTimerRunning = false;
                    }
                    return;
                }

                if (step is IfStep ifs)
                {
                    ExpandIfStep(ifs);
                    continue;
                }
            }
        }

        /// <summary>
        /// Selects an option from the currently displayed choices and executes its commands.
        /// </summary>
        /// <param name="optionIndex">Index into the displayed (filtered) choice list, 0-based.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="optionIndex"/> is invalid.</exception>
        /// <remarks>
        /// After executing the selected choice's commands, the runtime automatically calls <see cref="Continue"/>
        /// to advance to the next line/options/end.
        /// </remarks>
        public void Choose(int optionIndex)
        {
            _choiceTimerRunning = false;
            
            if (_waitingChoiceStep is null) return;

            if (optionIndex < 0 || optionIndex >= _displayedChoices.Count)
                throw new ArgumentOutOfRangeException(nameof(optionIndex));

            var choice = _displayedChoices[optionIndex];

            _waitingChoiceStep = null;

            UnityEngine.Debug.Log($"[CHOOSE] '{choice.Text}' at node '{_current?.Name}'");

            foreach (var cmd in choice.Commands)
            {
                Execute(cmd);
            }

            Continue();
        }

        /// <summary>
        /// Executes a single command, potentially mutating variables or control flow.
        /// </summary>
        /// <param name="cmd">Command to execute.</param>
        /// <exception cref="Exception">Thrown for unknown command types.</exception>
        private void Execute(ICommand cmd)
        {
            switch (cmd)
            {
                case JumpCommand jump:
                    Jump(jump.NodeName);
                    break;

                case SetCommand set:
                    _vars[set.VarName] = set.Value;
                    OnVariableSet?.Invoke(set.VarName, set.Value);
                    break;

                case IfCommand ifc:
                {
                    var branch = EvalCondition(ifc.Condition) ? ifc.ThenCommands : ifc.ElseCommands;
                    foreach (var inner in branch)
                        Execute(inner);
                    break;
                }

                default:
                    throw new Exception($"Unknown Command: " + cmd.GetType().Name);
            }
        }

        /// <summary>
        /// Jumps execution to another node and resets the step index to the start of that node.
        /// </summary>
        /// <param name="nodeName">Target node name.</param>
        /// <exception cref="Exception">Thrown if the node name does not exist in <see cref="_nodes"/>.</exception>
        private void Jump(string nodeName)
        {
            if (!_nodes.TryGetValue(nodeName, out _current))
                throw new Exception($"Unknown node: {nodeName}");

            _index = 0;
            JustJumped = true;
        }

        /// <summary>
        /// True if the next step in the current node is a <see cref="ChoiceStep"/>.
        /// </summary>
        public bool IsNextStepChoice
        {
            get
            {
                if (_current is null) return false;
                if (_index >= _current.Steps.Count) return false;
                return _current.Steps[_index] is ChoiceStep;
            }
        }

        /// <summary>
        /// Evaluates a boolean condition expression of the form <c>varName</c> or <c>!varName</c>.
        /// </summary>
        /// <param name="condition">Condition string.</param>
        /// <returns>
        /// True if the variable exists and is <c>true</c> (or false if negated).
        /// Empty or whitespace conditions evaluate to true.
        /// </returns>
        /// <remarks>
        /// Current implementation supports only boolean variables.
        /// </remarks>
        private bool EvalCondition(string condition)
        {
            // try
            // {
            //     return _conditionEvaluator.EvalBool(condition, _vars);
            // }
            // catch (Exception e)
            // {
            //     throw new Exception($"Condition error in node '{_current?.Name}': '{condition}'. {e.Message}", e);
            // }
            
            if (string.IsNullOrWhiteSpace(condition)) return true;

            var result = _conditionEvaluator.EvalBool(condition, _vars);
            UnityEngine.Debug.Log($"[EVAL] node='{_current?.Name}' expr='{condition}' => {result}");
            return result;
            // if (string.IsNullOrWhiteSpace(condition)) return true;
            //
            // var cond = condition.Trim();
            //
            // bool negate = false;
            // if (cond.StartsWith("!"))
            // {
            //     negate = true;
            //     cond = cond.Substring(1).Trim();
            // }
            //
            // var value =
            //     _vars.TryGetValue(cond, out var varValue) &&
            //     varValue is bool and true;
            //
            // return negate ? !value : value;
        }

        private void ExpandIfStep(IfStep ifs)
        {
            var branch = EvalCondition(ifs.Condition) ? ifs.ThenSteps : ifs.ElseSteps;

            _current.Steps.InsertRange(_index, branch);
        }

        /// <summary>
        /// Returns whether the given choice is available according to its <see cref="Choice.ConditionVar"/>.
        /// </summary>
        /// <param name="inChoice">Choice to evaluate.</param>
        /// <returns>
        /// True if the choice is non-null and either has no condition, or its condition evaluates to true.
        /// </returns>
        /// <remarks>
        /// Conditions support optional negation using <c>!</c> (e.g., <c>!hasKey</c>).
        /// </remarks>
        private bool IsChoiceAvailable(Choice inChoice)
        {
            if (inChoice is null) return false;
            if(string.IsNullOrWhiteSpace(inChoice.ConditionVar)) return true;
            
            return EvalCondition(inChoice.ConditionVar);
            // if (inChoice is null) return false;
            // if (string.IsNullOrWhiteSpace(inChoice.ConditionVar)) return true;
            //
            // var cond = inChoice.ConditionVar.Trim();
            //
            // bool negate = false;
            // if (cond.StartsWith("!"))
            // {
            //     negate = true;
            //     cond = cond.Substring(1).Trim();
            // }
            //
            // var value =
            //     _vars.TryGetValue(cond, out var varValue) &&
            //     varValue is bool and true;
            //
            // return negate ? !value : value;
        }
    }
}
