// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis.PooledObjects;

namespace Microsoft.CodeAnalysis.Operations.FlowAnalysis
{
    internal abstract partial class OperationTreeRewriter : OperationCloner
    {
        public override IOperation VisitConditional(IConditionalOperation operation, object argument)
        {
            if (operation.Type != null)
            {
                // We delay rewriting the child operations as the rewrittenCondition needs to be hoisted out first before we rewrite WhenTrue/WhenFalse.
                return RewriteConditionalExpression(operation);
            }
            else
            {
                if (operation.WhenFalse != null)
                {
                    return RewriteConditionalStatementWithElse(operation);
                }
                else
                {
                    return RewriteConditionalStatementWithoutElse(operation);
                }
            }
        }

        private IOperation RewriteConditionalExpression(IConditionalOperation operation)
        {
            var syntax = operation.Syntax;

            // For conditional expressions, we generate a temporary that will contain the result of the expression,
            // and hoist the temporary variable declaration and conditional expression computation before the statement.
            // See "VisitBlock" override, where we do the hoisting.
            //
            // condition ? consequence : alternative
            //
            // becomes
            //
            // var temp;
            // GotoIfFalse condition alt;
            // temp = consequence;
            // goto afterif;
            // alt:
            // temp = alternative;
            // afterif:

            // var temp;
            var tempDeclaration = CreateTemporaryDeclaration(operation.Type, syntax, out ILocalSymbol tempLocal);
            AddTempLocalToBlock(tempLocal);
            AddRewrittenOperationsToHoist(tempDeclaration);

            // GotoIfFalse condition alt;
            ILabelSymbol altLabel = CreateGeneratedLabelSymbol("alt");
            // NOTE: We delay rewriting the child operations as the rewrittenCondition needs to be hoisted out first before we rewrite WhenTrue/WhenFalse.
            var rewrittenCondition = Visit(operation.Condition);
            var conditionalBranchToAlt = new BranchStatement(altLabel, BranchKind.ConditionalGoTo, rewrittenCondition,
                jumpIfConditionTrue: false, SemanticModel, syntax, type: null, constantValue: default, isImplicit: true);
            AddRewrittenOperationsToHoist(conditionalBranchToAlt);

            // temp = consequence;
            var rewrittenWhenTrue = Visit(operation.WhenTrue);
            var left = new LocalReferenceExpression(tempLocal, isDeclaration: false, SemanticModel,
                rewrittenWhenTrue.Syntax, type: tempLocal.Type, constantValue: default, isImplicit: true);
            var assignConsequenceToTemp = new SimpleAssignmentExpression(left, isRef: false, rewrittenWhenTrue, SemanticModel,
                rewrittenWhenTrue.Syntax, type: tempLocal.Type, constantValue: default, isImplicit: true);
            AddRewrittenOperationsToHoist(assignConsequenceToTemp);

            // goto afterif;
            ILabelSymbol afterIfLabel = CreateGeneratedLabelSymbol("afterif");
            var afterIfBranch = new BranchStatement(afterIfLabel, BranchKind.GoTo, condition: null,
                jumpIfConditionTrue: false, SemanticModel, syntax, type: null, constantValue: default, isImplicit: true);
            AddRewrittenOperationsToHoist(afterIfBranch);

            // alt:
            var labeledAlt = new LabeledStatement(altLabel, null, SemanticModel,
                syntax, type: null, constantValue: default, isImplicit: true);
            AddRewrittenOperationsToHoist(labeledAlt);

            // temp = alternative;
            var rewrittenWhenFalse = Visit(operation.WhenFalse);
            left = new LocalReferenceExpression(tempLocal, isDeclaration: false, SemanticModel,
                rewrittenWhenFalse.Syntax, type: tempLocal.Type, constantValue: default, isImplicit: true);
            var assignAlternativeToTemp = new SimpleAssignmentExpression(left, isRef: false, rewrittenWhenFalse, SemanticModel,
                rewrittenWhenFalse.Syntax, type: tempLocal.Type, constantValue: default, isImplicit: true);
            var assignAlternativeToTempStatement = new ExpressionStatement(assignAlternativeToTemp, SemanticModel,
                rewrittenWhenFalse.Syntax, type: null, constantValue: default, isImplicit: true);
            AddRewrittenOperationsToHoist(assignAlternativeToTempStatement);

            // afterif:
            var labeledAfterIf = new LabeledStatement(afterIfLabel, null, SemanticModel,
                syntax, type: null, constantValue: default, isImplicit: true);
            AddRewrittenOperationsToHoist(labeledAfterIf);

            // Replace the conditional expression with the reference to tempLocal
            return new LocalReferenceExpression(tempLocal, isDeclaration: false, SemanticModel,
                syntax, type: tempLocal.Type, constantValue: default, isImplicit: true);
        }

        private IOperation RewriteConditionalStatementWithElse(IConditionalOperation operation)
        {
            // if (condition)
            //     consequence;
            // else 
            //     alternative
            //
            // becomes
            //
            // GotoIfFalse condition alt;
            // consequence
            // goto afterif;
            // alt:
            // alternative;
            // afterif:

            var statementBuilder = ArrayBuilder<IOperation>.GetInstance();
            var syntax = operation.Syntax;

            // GotoIfFalse condition alt;
            ILabelSymbol altLabel = CreateGeneratedLabelSymbol("alt");
            var rewrittenCondition = Visit(operation.Condition);
            var conditionalBranchToAlt = new BranchStatement(altLabel, BranchKind.ConditionalGoTo, rewrittenCondition,
                jumpIfConditionTrue: false, SemanticModel, syntax, type: null, constantValue: default, isImplicit: true);
            statementBuilder.Add(conditionalBranchToAlt);

            // consequence
            var rewrittenWhenTrue = Visit(operation.WhenTrue);
            statementBuilder.Add(rewrittenWhenTrue);

            // goto afterif;
            ILabelSymbol afterIfLabel = CreateGeneratedLabelSymbol("afterif");
            var afterIfBranch = new BranchStatement(afterIfLabel, BranchKind.GoTo, condition: null,
                jumpIfConditionTrue: false, SemanticModel, syntax, type: null, constantValue: default, isImplicit: true);
            statementBuilder.Add(afterIfBranch);

            // alt:
            var labeledAlternative = new LabeledStatement(altLabel, null, SemanticModel,
                operation.WhenFalse.Syntax, type: null, constantValue: default, isImplicit: true);
            statementBuilder.Add(labeledAlternative);

            // alternative;
            var rewrittenWhenFalse = Visit(operation.WhenFalse);
            statementBuilder.Add(rewrittenWhenFalse);

            // afterif:
            var labeledAfterIf = new LabeledStatement(afterIfLabel, null, SemanticModel,
                syntax, type: null, constantValue: default, isImplicit: true);
            statementBuilder.Add(labeledAfterIf);

            return new BlockStatement(statementBuilder.ToImmutableAndFree(), locals: ImmutableArray<ILocalSymbol>.Empty,
                SemanticModel, syntax, type: null, constantValue: default, isImplicit: true);
        }

        private IOperation RewriteConditionalStatementWithoutElse(IConditionalOperation operation)
        {
            // if (condition) 
            //   consequence;  
            //
            // becomes
            //
            // GotoIfFalse condition afterif;
            // consequence;
            // afterif:

            var statementBuilder = ArrayBuilder<IOperation>.GetInstance();
            var syntax = operation.Syntax;

            // GotoIfFalse condition alt;
            ILabelSymbol afterIfLabel = CreateGeneratedLabelSymbol("afterIfLabel");
            var rewrittenCondition = Visit(operation.Condition);
            var conditionalBranchToAfterIf = new BranchStatement(afterIfLabel, BranchKind.ConditionalGoTo, rewrittenCondition,
                jumpIfConditionTrue: false, SemanticModel, syntax, type: null, constantValue: default, isImplicit: true);
            statementBuilder.Add(conditionalBranchToAfterIf);

            // consequence
            var rewrittenWhenTrue = Visit(operation.WhenTrue);
            statementBuilder.Add(rewrittenWhenTrue);

            // afterif:
            var labeledAfterIf = new LabeledStatement(afterIfLabel, null, SemanticModel,
                syntax, type: null, constantValue: default, isImplicit: true);
            statementBuilder.Add(labeledAfterIf);

            return new BlockStatement(statementBuilder.ToImmutableAndFree(), locals: ImmutableArray<ILocalSymbol>.Empty,
                SemanticModel, syntax, type: null, constantValue: default, isImplicit: true);
        }
    }
}
