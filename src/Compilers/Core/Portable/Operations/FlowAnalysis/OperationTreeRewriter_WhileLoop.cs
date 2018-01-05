// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using Microsoft.CodeAnalysis.PooledObjects;

namespace Microsoft.CodeAnalysis.Operations.FlowAnalysis
{
    internal abstract partial class OperationTreeRewriter : OperationCloner
    {
        public override IOperation VisitWhileLoop(IWhileLoopOperation operation, object argument)
        {
            // while (condition) 
            //   body;
            //
            // becomes
            //
            // {
            //   goto continue;   (this branch is added only if "conditionIsTop = true")
            //   start:
            //    body
            //   continue:
            //    GotoIfTrue condition start;      (GotoIfFalse if "conditionIsUntil = true")
            //   break:
            // }

            var statementBuilder = ArrayBuilder<IOperation>.GetInstance();
            var syntax = operation.Syntax;
            var conditionIsTop = operation.ConditionIsTop;
            var conditionIsUntil = operation.ConditionIsUntil;
            var continueLabel = ((BaseWhileLoopStatement)operation).ContinueLabel;
            var breakLabel = ((BaseWhileLoopStatement)operation).BreakLabel;

            // goto continue;   (this branch is added only if "conditionIsTop = true")
            if (conditionIsTop)
            {
                var branchToContinue = new BranchStatement(continueLabel, BranchKind.GoTo, null,
                    jumpIfConditionTrue: false, SemanticModel, syntax, type: null, constantValue: default, isImplicit: true);
                statementBuilder.Add(branchToContinue);
            }

            // start:
            var startLabel = CreateGeneratedLabelSymbol("start");
            var labeledStart = new LabeledStatement(startLabel, null, SemanticModel,
                operation.Body.Syntax, type: null, constantValue: default, isImplicit: true);
            statementBuilder.Add(labeledStart);

            //  body
            var rewrittenBody = Visit(operation.Body);
            statementBuilder.Add(rewrittenBody);

            // continue:
            var labeledContinue = new LabeledStatement(continueLabel, null, SemanticModel,
                syntax, type: null, constantValue: default, isImplicit: true);
            statementBuilder.Add(labeledContinue);

            //  GotoIfTrue condition start;      (GotoIfFalse if "conditionIsUntil = true")
            var rewrittenCondition = Visit(operation.Condition);
            var conditionalBranchToStart = new BranchStatement(startLabel, BranchKind.ConditionalGoTo, rewrittenCondition,
                    jumpIfConditionTrue: !conditionIsUntil, SemanticModel, syntax, type: null, constantValue: default, isImplicit: true);
            statementBuilder.Add(conditionalBranchToStart);

            if (operation.IgnoredCondition != null)
            {
                // TODO
                var rewrittenIgnoredCondition = Visit(operation.IgnoredCondition);
            }

            // }
            return new BlockStatement(statementBuilder.ToImmutableAndFree(), operation.Locals,
                SemanticModel, syntax, type: null, constantValue: default, isImplicit: true);
        }
    }
}

