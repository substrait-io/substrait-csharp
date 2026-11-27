// SPDX-License-Identifier: Apache-2.0

using Substrait.Core.Plan;

namespace Substrait.Tests.Core;

internal static class TestPlans
{
    internal static readonly ExecutionBehavior PerPlan = new(VariableEvaluationMode.PerPlan);

    internal static Protobuf.ExecutionBehavior ProtoPerPlan => new() { VariableEvalMode = Protobuf.ExecutionBehavior.Types.VariableEvaluationMode.PerPlan };
}
