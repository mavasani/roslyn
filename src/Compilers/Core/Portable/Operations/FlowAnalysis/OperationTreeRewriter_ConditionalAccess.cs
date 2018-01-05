// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Diagnostics;

namespace Microsoft.CodeAnalysis.Operations.FlowAnalysis
{
    internal abstract partial class OperationTreeRewriter : OperationCloner
    {
        public override IOperation VisitConditionalAccess(IConditionalAccessOperation operation, object argument)
        {
            var syntax = operation.Syntax;

            // For conditional access expressions, we generate a temporary that will contain the result of the expression,
            // and hoist the temporary variable declaration and conditional access expression computation before the statement.
            // See "VisitBlock" override, where we do the hoisting.
            //
            // operation?.whennotnull
            //
            // becomes
            //
            // var temp1 = operation;
            // var temp2;
            // GotoIfTrue temp1 == null alt;
            // temp2 = whennotnull;
            // goto afterexpr;
            // alt:
            // temp2 = default;
            // afterexpr:

            // var temp1 = operation;
            // NOTE: We delay rewriting the WhenNotNull child as the rewrittenOperation needs to be hoisted out first before we rewrite WhenNotNull.
            var rewrittenOperation = Visit(operation.Operation);
            var temp1Declaration = CreateTemporaryDeclaration(rewrittenOperation, out ILocalSymbol temp1Local);
            AddTempLocalToBlock(temp1Local);
            AddRewrittenOperationsToHoist(temp1Declaration);

            // Ensure that the implicit ConditionaAccessInstance receiver for whennotnull is replaced with reference to temp1Local
            _conditionalAccessInstanceReplacements.Add(rewrittenOperation.Syntax, temp1Local);

            // var temp2;
            var temp2Declaration = CreateTemporaryDeclaration(operation.Type, syntax, out ILocalSymbol temp2Local);
            AddTempLocalToBlock(temp2Local);
            AddRewrittenOperationsToHoist(temp2Declaration);

            // GotoIfTrue temp1 == null alt;
            ILabelSymbol altLabel = CreateGeneratedLabelSymbol("alt");
            var temp1Reference = new LocalReferenceExpression(temp1Local, isDeclaration: false, SemanticModel,
                syntax, type: temp1Local.Type, constantValue: default, isImplicit: true);
            var nullCheck = MakeNullCheck(temp1Reference, SemanticModel, notNullCheck: false);
            var conditionalBranchToAlt = new BranchStatement(altLabel, BranchKind.ConditionalGoTo, nullCheck,
                jumpIfConditionTrue: true, SemanticModel, syntax, type: null, constantValue: default, isImplicit: true);
            AddRewrittenOperationsToHoist(conditionalBranchToAlt);

            // temp2 = whennotnull;
            var rewrittenWhenNotNull = Visit(operation.WhenNotNull);
            var left = new LocalReferenceExpression(temp2Local, isDeclaration: false, SemanticModel,
                rewrittenWhenNotNull.Syntax, type: temp2Local.Type, constantValue: default, isImplicit: true);
            var assignToTemp2 = new SimpleAssignmentExpression(left, isRef: false, rewrittenWhenNotNull, SemanticModel,
                rewrittenWhenNotNull.Syntax, type: temp2Local.Type, constantValue: default, isImplicit: true);
            AddRewrittenOperationsToHoist(assignToTemp2);

            // goto afterexpr;
            ILabelSymbol afterExprLabel = CreateGeneratedLabelSymbol("afterexpr");
            var afterIfBranch = new BranchStatement(afterExprLabel, BranchKind.GoTo, condition: null,
                jumpIfConditionTrue: false, SemanticModel, syntax, type: null, constantValue: default, isImplicit: true);
            AddRewrittenOperationsToHoist(afterIfBranch);

            // alt:
            var labeledAlt = new LabeledStatement(altLabel, null, SemanticModel,
                rewrittenWhenNotNull.Syntax, type: null, constantValue: default, isImplicit: true);
            AddRewrittenOperationsToHoist(labeledAlt);

            // temp2 = default;
            left = new LocalReferenceExpression(temp2Local, isDeclaration: false, SemanticModel,
                rewrittenWhenNotNull.Syntax, type: temp2Local.Type, constantValue: default, isImplicit: true);
            ConstantValue constantValue = ConstantValue.Default(operation.Type.SpecialType);
            var right = new DefaultValueExpression(SemanticModel, rewrittenWhenNotNull.Syntax, type: operation.Type, constantValue, isImplicit: true);
            var assignDefaultToTemp2 = new SimpleAssignmentExpression(left, isRef: false, right, SemanticModel,
                rewrittenWhenNotNull.Syntax, type: temp2Local.Type, constantValue: default, isImplicit: true);
            var assignDefaultToTemp2Statement = new ExpressionStatement(assignDefaultToTemp2, SemanticModel,
                rewrittenWhenNotNull.Syntax, type: null, constantValue: default, isImplicit: true);
            AddRewrittenOperationsToHoist(assignDefaultToTemp2Statement);

            // afterexpr:
            var labeledAfterExpr = new LabeledStatement(afterExprLabel, null, SemanticModel,
                syntax, type: null, constantValue: default, isImplicit: true);
            AddRewrittenOperationsToHoist(labeledAfterExpr);

            // Replace the conditional access expression with the reference to tempLocal2
            return new LocalReferenceExpression(temp2Local, isDeclaration: false, SemanticModel,
                syntax, type: temp2Local.Type, constantValue: default, isImplicit: true);
        }

        public override IOperation VisitConditionalAccessInstance(IConditionalAccessInstanceOperation operation, object argument)
        {
            if (_conditionalAccessInstanceReplacements.TryGetValue(operation.Syntax, out ILocalSymbol temp))
            {
                _conditionalAccessInstanceReplacements.Remove(operation.Syntax);
                return new LocalReferenceExpression(temp, isDeclaration: false, SemanticModel,
                    operation.Syntax, type: temp.Type, constantValue: default, isImplicit: true);
            }

            Debug.Fail(string.Format("Unexpected syntax for IConditionalAccessInstanceOperation '{0}'", operation.Syntax));
            return operation;
        }
    }
}
