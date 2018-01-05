// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Diagnostics;

namespace Microsoft.CodeAnalysis.Operations.FlowAnalysis
{
    internal abstract partial class OperationTreeRewriter : OperationCloner
    {
        public override IOperation VisitCoalesce(ICoalesceOperation operation, object argument)
        {
            var syntax = operation.Syntax;

            // For coalesce expressions, we generate a temporary that will contain the result of the expression,
            // and hoist the temporary variable declaration and coalesce expression computation before the statement.
            // See "VisitBlock" override, where we do the hoisting.
            //
            // value ?? whennull
            //
            // becomes
            //
            // var temp1 = value;
            // var temp2;
            // GotoIfTrue temp1 != null alt;
            // temp2 = whennull;
            // goto afterexpr;
            // alt:
            // temp2 = (conversion)temp1;
            // afterexpr:

            // var temp1 = value;
            var rewrittenValue = Visit(operation.Value);
            var temp1Declaration = CreateTemporaryDeclaration(rewrittenValue, out ILocalSymbol temp1Local);
            AddTempLocalToBlock(temp1Local);
            AddRewrittenOperationsToHoist(temp1Declaration);

            // var temp2;
            var temp2Declaration = CreateTemporaryDeclaration(operation.Type, syntax, out ILocalSymbol temp2Local);
            AddTempLocalToBlock(temp2Local);
            AddRewrittenOperationsToHoist(temp2Declaration);

            // GotoIfTrue temp1 != null alt;
            ILabelSymbol altLabel = CreateGeneratedLabelSymbol("alt");
            var temp1Reference = new LocalReferenceExpression(temp1Local, isDeclaration: false, SemanticModel,
                syntax, type: temp1Local.Type, constantValue: default, isImplicit: true);
            var nullCheck = MakeNullCheck(temp1Reference, SemanticModel, notNullCheck: true);
            var conditionalBranchToAlt = new BranchStatement(altLabel, BranchKind.ConditionalGoTo, nullCheck,
                jumpIfConditionTrue: true, SemanticModel, syntax, type: null, constantValue: default, isImplicit: true);
            AddRewrittenOperationsToHoist(conditionalBranchToAlt);

            // temp2 = whennull;
            var rewrittenWhenNull = Visit(operation.WhenNull);
            var left = new LocalReferenceExpression(temp2Local, isDeclaration: false, SemanticModel,
                rewrittenWhenNull.Syntax, type: temp2Local.Type, constantValue: default, isImplicit: true);
            var assignToTemp2 = new SimpleAssignmentExpression(left, isRef: false, rewrittenWhenNull, SemanticModel,
                rewrittenWhenNull.Syntax, type: temp2Local.Type, constantValue: default, isImplicit: true);
            AddRewrittenOperationsToHoist(assignToTemp2);

            // goto afterexpr;
            ILabelSymbol afterExprLabel = CreateGeneratedLabelSymbol("afterexpr");
            var afterIfBranch = new BranchStatement(afterExprLabel, BranchKind.GoTo, condition: null,
                jumpIfConditionTrue: false, SemanticModel, syntax, type: null, constantValue: default, isImplicit: true);
            AddRewrittenOperationsToHoist(afterIfBranch);

            // alt:
            var labeledAlt = new LabeledStatement(altLabel, null, SemanticModel,
                rewrittenWhenNull.Syntax, type: null, constantValue: default, isImplicit: true);
            AddRewrittenOperationsToHoist(labeledAlt);

            // temp2 = (conversion)temp1;
            left = new LocalReferenceExpression(temp2Local, isDeclaration: false, SemanticModel,
                rewrittenWhenNull.Syntax, type: temp2Local.Type, constantValue: default, isImplicit: true);
            var right = new LocalReferenceExpression(temp1Local, isDeclaration: false, SemanticModel,
                rewrittenWhenNull.Syntax, type: temp1Local.Type, constantValue: default, isImplicit: true);
            var rightConversion = MakeConversionIfNeeded(right, temp2Local.Type);
            var assignTemp1ToTemp2 = new SimpleAssignmentExpression(left, isRef: false, rightConversion, SemanticModel,
                rewrittenWhenNull.Syntax, type: temp1Local.Type, constantValue: default, isImplicit: true);
            var assignTemp1ToTemp2Statement = new ExpressionStatement(assignTemp1ToTemp2, SemanticModel,
                rewrittenWhenNull.Syntax, type: temp1Local.Type, constantValue: default, isImplicit: true);
            AddRewrittenOperationsToHoist(assignTemp1ToTemp2Statement);

            // afterexpr:
            var labeledAfterExpr = new LabeledStatement(afterExprLabel, null, SemanticModel,
                syntax, type: null, constantValue: default, isImplicit: true);
            AddRewrittenOperationsToHoist(labeledAfterExpr);

            // Replace the coalesce expression with the reference to tempLocal2
            return new LocalReferenceExpression(temp2Local, isDeclaration: false, SemanticModel,
                syntax, type: temp2Local.Type, constantValue: default, isImplicit: true);
        }
    }
}
