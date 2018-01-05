// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;

namespace Microsoft.CodeAnalysis.Operations.FlowAnalysis
{
    internal abstract partial class OperationTreeRewriter : OperationCloner
    {
        // For expressions with control flow (such as conditional expressions, coalesce expressions, conditional access expression),
        // we generate a temporary that will contain the result of the expression
        // and hoist the temporary variable declaration and expression computation before the statement.
        // We maintain a list of rewritten operations for expressions, that needs to be hoisted, and temporary locals for operations to hoist.
        private readonly Dictionary<IBlockOperation, List<IOperation>> _rewrittenOperationsToHoist;
        private readonly Dictionary<IBlockOperation, List<ILocalSymbol>> _tempLocalsForOperationsToHoist;

        private SyntaxNode _rootSyntax;
        private IBlockOperation _currentBlock;

        // Conditional access instance expression is replaced with temp local reference.
        private readonly Dictionary<SyntaxNode, ILocalSymbol> _conditionalAccessInstanceReplacements;

        protected OperationTreeRewriter(SemanticModel semanticModel, ISymbol containerSymbol)
        {
            SemanticModel = semanticModel;
            ContainerSymbol = containerSymbol;
            
            _rewrittenOperationsToHoist = new Dictionary<IBlockOperation, List<IOperation>>();
            _tempLocalsForOperationsToHoist = new Dictionary<IBlockOperation, List<ILocalSymbol>>();
            _conditionalAccessInstanceReplacements = new Dictionary<SyntaxNode, ILocalSymbol>();
            _currentBlock = null;
        }

        public IOperation Rewrite(IOperation operation)
        {
            _rootSyntax = operation.Syntax;
            var rewrittenOperation = Visit(operation);
            if (_currentBlock != null)
            {
                _rewrittenOperationsToHoist[_currentBlock].Add(rewrittenOperation);
                rewrittenOperation = new BlockStatement(_rewrittenOperationsToHoist[_currentBlock].ToImmutableArray(), _tempLocalsForOperationsToHoist[_currentBlock].ToImmutableArray(), SemanticModel,
                    operation.Syntax, operation.Type, operation.ConstantValue, operation.IsImplicit);
                _rewrittenOperationsToHoist.Remove(_currentBlock);
                _tempLocalsForOperationsToHoist.Remove(_currentBlock);
                _currentBlock = null;
            }

            Debug.Assert(_rewrittenOperationsToHoist.Count == 0);
            Debug.Assert(_tempLocalsForOperationsToHoist.Count == 0);
            _rootSyntax = null;
            return rewrittenOperation;
        }

        private void EnsureCurrentBlock()
        {
            if (_currentBlock == null)
            {
                _currentBlock = new BlockStatement(ImmutableArray<IOperation>.Empty, ImmutableArray<ILocalSymbol>.Empty, SemanticModel,
                    _rootSyntax, type: null, constantValue: default, isImplicit: true);
                _rewrittenOperationsToHoist.Add(_currentBlock, new List<IOperation>());
                _tempLocalsForOperationsToHoist.Add(_currentBlock, new List<ILocalSymbol>());
            }
        }

        private void AddRewrittenOperationsToHoist(IOperation rewrittenOperation)
        {
            EnsureCurrentBlock();
            Debug.Assert(_rewrittenOperationsToHoist.ContainsKey(_currentBlock));
            _rewrittenOperationsToHoist[_currentBlock].Add(rewrittenOperation);
        }

        private void AddTempLocalToBlock(ILocalSymbol tempLocal)
        {
            EnsureCurrentBlock();
            Debug.Assert(_tempLocalsForOperationsToHoist.ContainsKey(_currentBlock));
            _tempLocalsForOperationsToHoist[_currentBlock].Add(tempLocal);
        }

        protected SemanticModel SemanticModel { get; }
        protected ISymbol ContainerSymbol { get; }

        protected abstract ILabelSymbol CreateGeneratedLabelSymbol(string label);
        protected abstract ILocalSymbol CreateTemporaryLocalSymbol(ITypeSymbol type, SyntaxNode syntax);
        protected IOperation CreateNullLiteral(SyntaxNode syntax, SemanticModel semanticModel)
        {
            return CreateLiteral(ConstantValue.Null, type: null, syntax, semanticModel);
        }
        protected IOperation CreateLiteral(ConstantValue constantValue, ITypeSymbol type, SyntaxNode syntax, SemanticModel semanticModel)
        {
            return new LiteralExpression(semanticModel, syntax, type, constantValue, isImplicit: true);
        }

        private VariableDeclaration CreateTemporaryDeclaration(ITypeSymbol type, SyntaxNode syntax, out ILocalSymbol tempLocal)
        {
            tempLocal = CreateTemporaryLocalSymbol(type, syntax);
            IVariableDeclaratorOperation declarator = new VariableDeclarator(tempLocal, initializer: null, ignoredArguments: ImmutableArray<IOperation>.Empty, SemanticModel,
                syntax, type: null, constantValue: default, isImplicit: true);
            return new VariableDeclaration(ImmutableArray.Create(declarator), initializer: null, SemanticModel,
                syntax, type: null, constantValue: default, isImplicit: true);
        }

        private VariableDeclaration CreateTemporaryDeclaration(IOperation initializedValue, out ILocalSymbol tempLocal)
        {
            tempLocal = CreateTemporaryLocalSymbol(initializedValue.Type, initializedValue.Syntax);
            var initializer = new VariableInitializer(initializedValue, SemanticModel, initializedValue.Syntax, initializedValue.Type, initializedValue.ConstantValue, isImplicit: true);
            IVariableDeclaratorOperation declarator = new VariableDeclarator(tempLocal, initializer: null, ignoredArguments: ImmutableArray<IOperation>.Empty, SemanticModel,
                initializedValue.Syntax, type: null, constantValue: default, isImplicit: true);
            return new VariableDeclaration(ImmutableArray.Create(declarator), initializer, SemanticModel,
                initializedValue.Syntax, type: null, constantValue: default, isImplicit: true);
        }

        private BinaryOperatorExpression MakeNullCheck(IOperation valueToCheck, SemanticModel semanticModel, bool notNullCheck)
        {
            IOperation nullLiteral = CreateNullLiteral(valueToCheck.Syntax, semanticModel);
            var operatorKind = !notNullCheck ? BinaryOperatorKind.Equals : BinaryOperatorKind.NotEquals;
            var booleanType = semanticModel.Compilation.GetSpecialType(SpecialType.System_Boolean);
            Optional<object> constantValue = default;
            if (valueToCheck.ConstantValue.HasValue && valueToCheck.ConstantValue.Value == null)
            {
                constantValue = !notNullCheck ? ConstantValue.True : ConstantValue.False;
            }

            return new BinaryOperatorExpression(operatorKind, valueToCheck, nullLiteral,
                isLifted: false, isChecked: false, isCompareText: false, operatorMethod: null, semanticModel,
                valueToCheck.Syntax, type: booleanType, constantValue: constantValue, isImplicit: true);
        }

        protected abstract IOperation MakeConversionIfNeeded(IOperation source, ITypeSymbol convertedType);
        protected abstract bool IsNullableType(ITypeSymbol type);

        public override IOperation VisitSwitch(ISwitchOperation operation, object argument)
        {
            return new SwitchStatement(Visit(operation.Value), VisitArray(operation.Cases), SemanticModel, operation.Syntax, operation.Type, operation.ConstantValue, operation.IsImplicit);
        }

        public override IOperation VisitForEachLoop(IForEachLoopOperation operation, object argument)
        {
            return new ForEachLoopStatement(operation.Locals, Visit(operation.LoopControlVariable), Visit(operation.Collection), VisitArray(operation.NextVariables), Visit(operation.Body), ((BaseForEachLoopStatement)operation).ContinueLabel, ((BaseForEachLoopStatement)operation).BreakLabel, SemanticModel, operation.Syntax, operation.Type, operation.ConstantValue, operation.IsImplicit);
        }
    }
}
