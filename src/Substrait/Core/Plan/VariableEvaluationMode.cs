// SPDX-License-Identifier: Apache-2.0

using ProtoVariableEvaluationMode = Substrait.Protobuf.ExecutionBehavior.Types.VariableEvaluationMode;

namespace Substrait.Core.Plan;

/// <summary>
/// Frequency and scope of execution context variable evaluation.
/// </summary>
public enum VariableEvaluationMode
{
    /// <summary>
    /// Variable evaluation mode unspecified.
    /// </summary>
    Unspecified = ProtoVariableEvaluationMode.Unspecified,

    /// <summary>
    /// Variables are evaluated once per plan execution.
    /// </summary>
    PerPlan = ProtoVariableEvaluationMode.PerPlan,

    /// <summary>
    /// Variables are evaluated once per record.
    /// </summary>
    PerRecord = ProtoVariableEvaluationMode.PerRecord,
}
