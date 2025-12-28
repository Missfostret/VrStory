using System;
using System.Collections.Generic;

/// <summary>
/// Base class for all executable dialogue steps.
/// </summary>
/// <remarks>
/// A <see cref="MiniNode"/> is composed of an ordered list of <see cref="Step"/> instances
/// that are executed sequentially by the dialogue runtime.
/// </remarks>
public abstract class Step { }

/// <summary>
/// Dialogue step that displays a single line of text to the player.
/// </summary>
public sealed class LineStep : Step
{
    /// <summary>
    /// Raw dialogue text for this line.
    /// May contain variables or markup to be interpolated at runtime.
    /// </summary>
    public string Text;

    public Dialogue.Scripts.SourceInfo Source;

    /// <summary>
    /// Creates a new line step with the given text.
    /// </summary>
    /// <param name="text">Dialogue line text.</param>
    public LineStep(string text) => Text = text;
}

/// <summary>
/// Represents a step in a dialogue node where the player is presented with one or more choices.
/// </summary>
/// <remarks>
/// A <see cref="ChoiceStep"/> may optionally have a time limit, after which a default choice
/// will be automatically selected if the player has not made a selection.
/// </remarks>
public sealed class ChoiceStep : Step
{
    /// <summary>
    /// List of available choices for this step.
    /// Choices may be conditionally visible based on runtime variables.
    /// </summary>
    public List<Choice> Choices = new();

    /// <summary>
    /// Optional time limit (in seconds) for this choice step.
    /// If null, the choices do not expire.
    /// </summary>
    /// <remarks>
    /// When set, the runtime will begin a countdown as soon as the choices are shown.
    /// When the timer reaches zero, the default choice will be selected automatically.
    /// </remarks>
    public float? TimeLimitSeconds;

    /// <summary>
    /// The index of the choice (in the displayed, filtered list) that will be
    /// automatically selected if the time limit expires.
    /// </summary>
    /// <remarks>
    /// This index refers to the list of choices actually shown to the player,
    /// not the raw unfiltered choice list.
    /// </remarks>
    public int DefaultOptionIndex = 0;
}

/// <summary>
/// Represents a single selectable dialogue choice.
/// </summary>
public sealed class Choice
{
    /// <summary>
    /// Text displayed to the player for this choice.
    /// </summary>
    public string Text;

    /// <summary>
    /// Optional condition variable name that controls whether this choice is available.
    /// If null or empty, the choice is always available.
    /// </summary>
    /// <remarks>
    /// The runtime is responsible for interpreting and evaluating this condition.
    /// Supported forms include:
    /// <list type="bullet">
    /// <item><description><c>hasKey</c> — choice is available if the variable is true</description></item>
    /// <item><description><c>!hasKey</c> — choice is available if the variable is false or unset</description></item>
    /// </list>
    /// </remarks>
    public string ConditionVar;

    /// <summary>
    /// Commands executed when this choice is selected.
    /// </summary>
    public List<ICommand> Commands = new();

    /// <summary>
    /// Creates a new dialogue choice.
    /// </summary>
    /// <param name="text">Display text for the choice.</param>
    /// <param name="conditionVar">
    /// Optional condition variable that must be satisfied for the choice to appear.
    /// </param>
    public Choice(string text, string conditionVar)
    {
        Text = text;
        ConditionVar = conditionVar;
    }
}

/// <summary>
/// Dialogue step that executes a single command immediately.
/// </summary>
public sealed class CommandStep : Step
{
    /// <summary>
    /// Command to execute.
    /// </summary>
    public ICommand Command;

    /// <summary>
    /// Creates a command step that wraps a single command.
    /// </summary>
    /// <param name="command">Command to execute.</param>
    public CommandStep(ICommand command) => Command = command;
}

/// <summary>
/// Marker interface for executable dialogue commands.
/// </summary>
/// <remarks>
/// Commands are evaluated by the dialogue runtime to modify state,
/// control flow, or variables.
/// </remarks>
public interface ICommand { }

/// <summary>
/// Command that jumps execution to another dialogue node.
/// </summary>
public sealed class JumpCommand : ICommand
{
    /// <summary>
    /// Name of the target node to jump to.
    /// </summary>
    public string NodeName;

    /// <summary>
    /// Creates a jump command targeting the specified node.
    /// </summary>
    /// <param name="nodeName">Target node name.</param>
    public JumpCommand(string nodeName) => NodeName = nodeName;
}

/// <summary>
/// Command that sets or updates a runtime variable.
/// </summary>
public sealed class SetCommand : ICommand
{
    /// <summary>
    /// Name of the variable to set.
    /// </summary>
    public string VarName;

    /// <summary>
    /// Value assigned to the variable.
    /// </summary>
    public object Value;

    /// <summary>
    /// Creates a variable assignment command.
    /// </summary>
    /// <param name="varName">Variable name.</param>
    /// <param name="value">Value to assign.</param>
    public SetCommand(string varName, object value)
    {
        VarName = varName;
        Value = value;
    }
}

/// <summary>
/// Conditional command that executes one of two command lists based on a condition.
/// </summary>
public sealed class IfCommand : ICommand
{
    /// <summary>
    /// Condition expression string (e.g. "hasKey" or "!hasKey").
    /// </summary>
    /// <remarks>
    /// Condition parsing and evaluation is handled by the dialogue runtime.
    /// </remarks>
    public string Condition;

    /// <summary>
    /// Commands executed if the condition evaluates to true.
    /// </summary>
    public List<ICommand> ThenCommands = new();

    /// <summary>
    /// Commands executed if the condition evaluates to false.
    /// </summary>
    public List<ICommand> ElseCommands = new();

    /// <summary>
    /// Creates a conditional command with the given condition expression.
    /// </summary>
    /// <param name="condition">Condition expression string.</param>
    public IfCommand(string condition)
    {
        Condition = condition;
    }
}

public sealed class IfStep : Step
{
    public string Condition;
    public List<Step> ThenSteps = new();
    public List<Step> ElseSteps = new();
    
    public IfStep(string condition)
    {
        Condition = condition;
    }
}

/// <summary>
/// Represents a dialogue node containing a sequence of executable steps.
/// </summary>
/// <remarks>
/// Nodes are the primary structural unit of a dialogue graph.
/// Execution typically begins at a named node (e.g. "Intro").
/// </remarks>
public sealed class MiniNode
{
    /// <summary>
    /// Unique name identifying this node.
    /// </summary>
    public string Name;

    /// <summary>
    /// Ordered list of steps executed when this node is entered.
    /// </summary>
    public List<Step> Steps = new();
}
