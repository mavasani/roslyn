// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis.PooledObjects;

namespace Microsoft.CodeAnalysis.Operations.FlowAnalysis
{
    internal abstract partial class OperationTreeRewriter : OperationCloner
    {
        public override IOperation VisitForToLoop(IForToLoopOperation operation, object argument)
        {
            // For i as Integer = 3 To 6 step 2
            //    body
            // Next
            //
            // becomes
            //
            // {
            //     // NOTE: control variable life time is as in Dev10, Dev11 may change this!!!!
            //     dim i as Integer = 3  '<-- all iterations share same variable (important for closures)
            //     dim temp1, temp2 ...  // temps if needed for hoisting of Limit/Step/Direction
            //
            //     goto postIncrement;
            //   start:
            //     body      
            //   continue:
            //     i = i + 2
            //   postIncrement:
            //     if i <= 6 goto start
            //
            //   exit:
            // }

            var statementBuilder = ArrayBuilder<IOperation>.GetInstance();
            var syntax = operation.Syntax;
            var continueLabel = ((BaseForToLoopStatement)operation).ContinueLabel;
            var breakLabel = ((BaseForToLoopStatement)operation).BreakLabel;

            //     dim i as Integer = 3  '<-- all iterations share same variable (important for closures)
            IOperation loopControlInitialization;
            var rewrittenLoopControlVariable = Visit(operation.LoopControlVariable);
            var rewrittenInitialValue = Visit(operation.InitialValue);
            if (rewrittenLoopControlVariable != null)
            {
                if (rewrittenInitialValue != null)
                {
                    if (rewrittenLoopControlVariable.Kind == OperationKind.VariableDeclarator)
                    {
                        var variableDeclarator = (IVariableDeclaratorOperation)rewrittenLoopControlVariable;
                        var variableInitializer = new VariableInitializer(rewrittenInitialValue, SemanticModel,
                            rewrittenInitialValue.Syntax, type: null, constantValue: default, isImplicit: true);
                        loopControlInitialization = new VariableDeclarator(variableDeclarator.Symbol, variableInitializer,
                            ignoredArguments: variableDeclarator.IgnoredArguments, SemanticModel, syntax,
                            type: null, constantValue: default, isImplicit: true);
                    }
                    else
                    {
                        loopControlInitialization = new SimpleAssignmentExpression(rewrittenLoopControlVariable, isRef: false,
                            rewrittenInitialValue, SemanticModel, syntax, type: rewrittenLoopControlVariable.Type, constantValue: default, isImplicit: true);
                    }
                }
                else
                {
                    loopControlInitialization = rewrittenLoopControlVariable;
                }
            }
            else
            {
                loopControlInitialization = rewrittenInitialValue;
            }

            if (loopControlInitialization != null)
            {
                statementBuilder.AddRange(loopControlInitialization);
            }

            //     dim temp1, temp2 ...  // temps if needed for hoisting of Limit/Step/Direction
            var rewrittenLimitValue = Visit(operation.LimitValue);
            IVariableDeclarationOperation limitValueTempInitializer = CreateTemporaryDeclaration(rewrittenLimitValue, out ILocalSymbol limitValueTemp);
            statementBuilder.Add(limitValueTempInitializer);
            var rewrittenStepValue = Visit(operation.StepValue);
            IVariableDeclarationOperation stepValueTempInitializer = CreateTemporaryDeclaration(rewrittenStepValue, out ILocalSymbol stepValueTemp);
            statementBuilder.Add(stepValueTempInitializer);

            //     goto postIncrement;
            var postIncrementLabel = CreateGeneratedLabelSymbol("postIncrement");
            var gotoPostIncrement = new BranchStatement(postIncrementLabel, BranchKind.GoTo, condition: null,
                jumpIfConditionTrue: false, SemanticModel, syntax, type: null, constantValue: default, isImplicit: true);
            statementBuilder.Add(gotoPostIncrement);

            //   start:
            var startLabel = CreateGeneratedLabelSymbol("start");
            var labeledStart = new LabeledStatement(startLabel, null, SemanticModel,
                operation.Body.Syntax, type: null, constantValue: default, isImplicit: true);
            statementBuilder.Add(labeledStart);

            //     body      
            var rewrittenBody = Visit(operation.Body);
            statementBuilder.Add(rewrittenBody);

            //   continue:
            //     i = i + 2
            var leftControlVariable = SemanticModel.CloneOperation(rewrittenLoopControlVariable);
            var rightControlVariable = SemanticModel.CloneOperation(rewrittenLoopControlVariable);
            var stepValueReference = new LocalReferenceExpression(stepValueTemp, isDeclaration: false, SemanticModel,
                rewrittenStepValue.Syntax, type: stepValueTemp.Type, constantValue: stepValueTemp.ConstantValue, isImplicit: true);
            var isLifted = IsNullableType(rightControlVariable.Type) || IsNullableType(stepValueReference.Type);
            // TODO: OperatorMethod may be non-null
            var addOperation = new BinaryOperatorExpression(BinaryOperatorKind.Add, rightControlVariable,
                stepValueReference, isLifted, isChecked: false, isCompareText: false, operatorMethod: null,
                SemanticModel, syntax: rewrittenStepValue.Syntax, type: rightControlVariable.Type, constantValue: default, isImplicit: true);
            var assignment = new SimpleAssignmentExpression(leftControlVariable, isRef: false, addOperation,
                SemanticModel, syntax: rewrittenStepValue.Syntax, type: leftControlVariable.Type, constantValue: default, isImplicit: true);
            statementBuilder.Add(assignment);

            //   postIncrement:
            //     if i <= 6 goto start
            leftControlVariable = SemanticModel.CloneOperation(rewrittenLoopControlVariable);
            var limitValueReference = new LocalReferenceExpression(limitValueTemp, isDeclaration: false, SemanticModel,
                rewrittenLimitValue.Syntax, type: limitValueTemp.Type, constantValue: limitValueTemp.ConstantValue, isImplicit: true);
            isLifted = IsNullableType(leftControlVariable.Type) || IsNullableType(limitValueReference.Type);
            // TODO: OperatorMethod may be non-null
            var condition = new BinaryOperatorExpression(BinaryOperatorKind.LessThanOrEqual, leftControlVariable,
                limitValueReference, isLifted, isChecked: false, isCompareText: false, operatorMethod: null,
                SemanticModel, syntax: rewrittenLimitValue.Syntax, type: leftControlVariable.Type, constantValue: default, isImplicit: true);
            var branchOperation = new BranchStatement(startLabel, BranchKind.ConditionalGoTo, condition,
                jumpIfConditionTrue: true, SemanticModel, rewrittenLimitValue.Syntax, type: null, constantValue: default, isImplicit: true);
            var labeledBranch = new LabeledStatement(postIncrementLabel, branchOperation, SemanticModel, syntax, type: null, constantValue: default, isImplicit: true);
            statementBuilder.Add(labeledBranch);

            //   exit:
            var labeledBreak = new LabeledStatement(breakLabel, null, SemanticModel,
                syntax, type: null, constantValue: default, isImplicit: true);
            statementBuilder.Add(labeledBreak);

            // }
            return new BlockStatement(statementBuilder.ToImmutableAndFree(), operation.Locals,
                SemanticModel, syntax, type: null, constantValue: default, isImplicit: true);
        }
    }
}
