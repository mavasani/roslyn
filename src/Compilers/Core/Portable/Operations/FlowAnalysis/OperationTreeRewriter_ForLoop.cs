// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis.PooledObjects;

namespace Microsoft.CodeAnalysis.Operations.FlowAnalysis
{
    internal abstract partial class OperationTreeRewriter : OperationCloner
    {
        public override IOperation VisitForLoop(IForLoopOperation operation, object argument)
        {
            // for (initializer; condition; increment)
            //   body;
            //
            // becomes the following (with block added for locals)
            //
            // {
            //   initializer;
            //   goto end;
            // start:
            //   body;
            // continue:
            //   increment;
            // end:
            //   GotoIfTrue condition start;
            // break:
            // }

            var statementBuilder = ArrayBuilder<IOperation>.GetInstance();
            var syntax = operation.Syntax;
            var continueLabel = ((BaseForLoopStatement)operation).ContinueLabel;
            var breakLabel = ((BaseForLoopStatement)operation).BreakLabel;

            //   initializer;
            var rewrittenBefore = VisitArray(operation.Before);
            statementBuilder.AddRange(rewrittenBefore);

            //   goto end;
            var endLabel = CreateGeneratedLabelSymbol("end");
            var gotoEnd = new BranchStatement(endLabel, BranchKind.GoTo, condition: null,
                jumpIfConditionTrue: false, SemanticModel, syntax, type: null, constantValue: default, isImplicit: true);
            statementBuilder.Add(gotoEnd);

            // start:
            var startLabel = CreateGeneratedLabelSymbol("start");
            var labeledStart = new LabeledStatement(startLabel, null, SemanticModel,
                operation.Body.Syntax, type: null, constantValue: default, isImplicit: true);
            statementBuilder.Add(labeledStart);

            //   body;
            var rewrittenBody = Visit(operation.Body);
            statementBuilder.Add(rewrittenBody);

            // continue:
            var labeledContinue = new LabeledStatement(continueLabel, null, SemanticModel,
                rewrittenBody.Syntax, type: null, constantValue: default, isImplicit: true);
            statementBuilder.Add(labeledContinue);

            //   increment;
            var rewrittenAtLoopBottom = VisitArray(operation.AtLoopBottom);
            statementBuilder.AddRange(rewrittenAtLoopBottom);

            // end:
            var labeledBranch = new LabeledStatement(endLabel, null, SemanticModel, syntax, type: null, constantValue: default, isImplicit: true);
            statementBuilder.Add(labeledBranch);

            //   GotoIfTrue condition start;
            SyntaxNode branchSyntax;
            BranchKind branchKind;
            bool jumpIfConditionTrue;
            var rewrittenCondition = Visit(operation.Condition);
            if (rewrittenCondition != null)
            {
                branchSyntax = rewrittenCondition.Syntax;
                branchKind = BranchKind.ConditionalGoTo;
                jumpIfConditionTrue = true;
            }
            else
            {
                branchSyntax = syntax;
                branchKind = BranchKind.GoTo;
                jumpIfConditionTrue = false;
            }

            var branchOperation = new BranchStatement(startLabel, branchKind, rewrittenCondition,
                jumpIfConditionTrue, SemanticModel, branchSyntax, type: null, constantValue: default, isImplicit: true);
            statementBuilder.Add(branchOperation);

            // break:
            var labeledBreak = new LabeledStatement(breakLabel, null, SemanticModel,
                syntax, type: null, constantValue: default, isImplicit: true);
            statementBuilder.Add(labeledBreak);

            // }
            return new BlockStatement(statementBuilder.ToImmutableAndFree(), operation.Locals,
                SemanticModel, syntax, type: null, constantValue: default, isImplicit: true);
        }
    }
}
