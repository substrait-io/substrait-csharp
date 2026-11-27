// SPDX-License-Identifier: Apache-2.0

namespace Substrait.Core.Plan;

/// <summary>
/// Execution behavior for plan evaluation.
/// </summary>
public sealed class ExecutionBehavior : IEquatable<ExecutionBehavior>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ExecutionBehavior"/> class.
    /// </summary>
    /// <param name="variableEvaluationMode">The scope of execution context variable evaluation.</param>
    public ExecutionBehavior(VariableEvaluationMode variableEvaluationMode)
    {
        this.VariableEvaluationMode = variableEvaluationMode;
    }

    /// <summary>
    /// Gets the scope of execution context variable evaluation.
    /// </summary>
    public VariableEvaluationMode VariableEvaluationMode { get; }

    /// <inheritdoc/>
    public bool Equals(ExecutionBehavior? other) => other is not null && this.VariableEvaluationMode == other.VariableEvaluationMode;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => this.Equals(obj as ExecutionBehavior);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(nameof(ExecutionBehavior), this.VariableEvaluationMode);
}
