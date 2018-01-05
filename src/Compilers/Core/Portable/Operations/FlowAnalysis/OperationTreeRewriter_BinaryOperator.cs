// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using Microsoft.CodeAnalysis.PooledObjects;

namespace Microsoft.CodeAnalysis.Operations.FlowAnalysis
{
    internal abstract partial class OperationTreeRewriter : OperationCloner
    {
        public override IOperation VisitBinaryOperator(IBinaryOperation operation, object argument)
        {
            switch (operation.OperatorKind)
            {
                case BinaryOperatorKind.ConditionalAnd:
                case BinaryOperatorKind.ConditionalOr:
                    return RewriteConditionalBinaryOperator(operation);
                default:
                    return RewriteNonConditionalBinaryOperator(operation);
            }
        }

        private IOperation RewriteNonConditionalBinaryOperator(IBinaryOperation operation)
        {
            var syntax = operation.Syntax;

            // For binary expressions, we generate a temporary that will contain the result of the expression,
            // and hoist the temporary variable declaration and expression computation before the statement.
            // See "VisitBlock" override, where we do the hoisting.

            // var temp;
            var rewrittenLeft = Visit(operation.LeftOperand);
            var rewrittenRight = Visit(operation.RightOperand);
            var rewrittenBinary = new BinaryOperatorExpression(operation.OperatorKind, rewrittenLeft, rewrittenRight, operation.IsLifted,
                operation.IsChecked, operation.IsCompareText, operation.OperatorMethod, SemanticModel, syntax,
                operation.Type, operation.ConstantValue, operation.IsImplicit);

            var tempDeclaration = CreateTemporaryDeclaration(rewrittenBinary,  out ILocalSymbol tempLocal);
            AddTempLocalToBlock(tempLocal);
            AddRewrittenOperationsToHoist(tempDeclaration);

            // Replace the binary expression with the reference to tempLocal
            return new LocalReferenceExpression(tempLocal, isDeclaration: false, SemanticModel,
                syntax, type: tempLocal.Type, constantValue: default, isImplicit: true);
        }

        private IOperation RewriteConditionalBinaryOperator(IBinaryOperation operation)
        {
            var syntax = operation.Syntax;

            // left && right
            //
            // becomes
            //
            // bool temp;
            // GoToIfFalse left shortCircuit
            // GoToIfFalse right shortCircuit
            // temp = true;
            // goto end
            // shortCircuit:
            // temp = false;
            // end:
            //
            //
            // left || right
            //
            // becomes
            //
            // bool temp;
            // GoToIfTrue left shortCircuit
            // GoToIfTrue right shortCircuit
            // temp = false;
            // goto end
            // shortCircuit:
            // temp = true;
            // end:

            var booleanType = SemanticModel.Compilation.GetSpecialType(SpecialType.System_Boolean);
            var falseLiteral = CreateLiteral(ConstantValue.False, type: booleanType, syntax, SemanticModel);
            var trueLiteral = CreateLiteral(ConstantValue.True, type: booleanType, syntax, SemanticModel);
            bool jumpIfConditionTrue;
            IOperation shortCircuitValue;
            IOperation nonShortCircuitValue;
            if (operation.OperatorKind == BinaryOperatorKind.ConditionalAnd)
            {
                jumpIfConditionTrue = false;
                shortCircuitValue = falseLiteral;
                nonShortCircuitValue = trueLiteral;
            }
            else
            {
                jumpIfConditionTrue = true;
                shortCircuitValue = trueLiteral;
                nonShortCircuitValue = falseLiteral;
            }

            // bool temp;
            var tempDeclaration = CreateTemporaryDeclaration(operation.Type, syntax, out ILocalSymbol tempLocal);
            AddTempLocalToBlock(tempLocal);
            AddRewrittenOperationsToHoist(tempDeclaration);

            // GoToIfFalse left shortCircuit (or GoToIfTrue)
            ILabelSymbol shortCircuitLabel = CreateGeneratedLabelSymbol("shortCircuit");
            var rewrittenLeft = Visit(operation.LeftOperand);
            var conditionalBranchToShortCircuitLabel = new BranchStatement(shortCircuitLabel, BranchKind.ConditionalGoTo, rewrittenLeft,
                jumpIfConditionTrue, SemanticModel, operation.LeftOperand.Syntax, type: null, constantValue: default, isImplicit: true);
            AddRewrittenOperationsToHoist(conditionalBranchToShortCircuitLabel);

            // GoToIfFalse right shortCircuit (or GoToIfTrue)
            var rewrittenRight = Visit(operation.RightOperand);
            conditionalBranchToShortCircuitLabel = new BranchStatement(shortCircuitLabel, BranchKind.ConditionalGoTo, rewrittenRight,
                jumpIfConditionTrue, SemanticModel, operation.RightOperand.Syntax, type: null, constantValue: default, isImplicit: true);
            AddRewrittenOperationsToHoist(conditionalBranchToShortCircuitLabel);

            // temp = true; (or false)
            var left = new LocalReferenceExpression(tempLocal, isDeclaration: false, SemanticModel,
                syntax, type: tempLocal.Type, constantValue: default, isImplicit: true);
            var assignNonShortCircuitValueToTemp = new SimpleAssignmentExpression(left, isRef: false, nonShortCircuitValue, SemanticModel,
                syntax, type: tempLocal.Type, constantValue: default, isImplicit: true);
            var assignNonShortCircuitValueToTempStatement = new ExpressionStatement(assignNonShortCircuitValueToTemp, SemanticModel,
                syntax, type: null, constantValue: default, isImplicit: true);
            AddRewrittenOperationsToHoist(assignNonShortCircuitValueToTempStatement);

            // goto end;
            ILabelSymbol endLabel = CreateGeneratedLabelSymbol("end");
            var branchToEnd = new BranchStatement(endLabel, BranchKind.GoTo, condition: null,
                jumpIfConditionTrue: false, SemanticModel, syntax, type: null, constantValue: default, isImplicit: true);
            AddRewrittenOperationsToHoist(branchToEnd);

            // shortCircuit:
            var labeledShortCircuit = new LabeledStatement(shortCircuitLabel, null, SemanticModel,
                syntax, type: null, constantValue: default, isImplicit: true);
            AddRewrittenOperationsToHoist(labeledShortCircuit);

            // temp = false; (or true)
            left = new LocalReferenceExpression(tempLocal, isDeclaration: false, SemanticModel,
                syntax, type: tempLocal.Type, constantValue: default, isImplicit: true);
            var assignShortCircuitValueToTemp = new SimpleAssignmentExpression(left, isRef: false, shortCircuitValue, SemanticModel,
                syntax, type: tempLocal.Type, constantValue: default, isImplicit: true);
            var assignShortCircuitValueToTempStatement = new ExpressionStatement(assignShortCircuitValueToTemp, SemanticModel,
                syntax, type: null, constantValue: default, isImplicit: true);
            AddRewrittenOperationsToHoist(assignShortCircuitValueToTempStatement);

            // end:
            var labeledEnd = new LabeledStatement(endLabel, null, SemanticModel, syntax, type: null, constantValue: default, isImplicit: true);
            AddRewrittenOperationsToHoist(labeledEnd);

            // Replace the binary expression with the reference to tempLocal
            return new LocalReferenceExpression(tempLocal, isDeclaration: false, SemanticModel,
                syntax, type: tempLocal.Type, constantValue: default, isImplicit: true);
        }
    }
}
