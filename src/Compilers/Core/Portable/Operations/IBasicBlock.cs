// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Immutable;

namespace Microsoft.CodeAnalysis.FlowAnalysis
{
    internal interface IBasicBlock
    {
        BasicBlockKind Kind { get; }
        ImmutableArray<IOperation> Operations { get; }
        IOperation BranchValue { get; }
        ControlFlowConditionKind ConditionKind { get; }
        IControlFlowBranch FallThroughSuccessor { get; }
        IControlFlowBranch ConditionalSuccessor { get; }
        int Ordinal { get; }
        bool IsReachable { get; }
        ControlFlowRegion EnclosingRegion { get; }
    }
}
