﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Analyzer.Utilities;
using Analyzer.Utilities.PooledObjects;
using Microsoft.CodeAnalysis.CodeQuality;
using Microsoft.CodeAnalysis.CodeStyle;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis.DataFlow;
using Microsoft.CodeAnalysis.FlowAnalysis.DataFlow.DisposeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis.DataFlow.PointsToAnalysis;

namespace Microsoft.CodeAnalysis.DisposeAnalysis
{
    [DiagnosticAnalyzer(LanguageNames.CSharp, LanguageNames.VisualBasic)]
    internal sealed class DisposeObjectsBeforeLosingScopeDiagnosticAnalyzer
        : AbstractCodeQualityDiagnosticAnalyzer
    {
        private static readonly DiagnosticDescriptor s_disposeObjectsBeforeLosingScopeRule = CreateDescriptor(
            IDEDiagnosticIds.DisposeObjectsBeforeLosingScopeDiagnosticId,
            new LocalizableResourceString(nameof(FeaturesResources.Dispose_objects_before_losing_scope), FeaturesResources.ResourceManager, typeof(FeaturesResources)),
            new LocalizableResourceString(nameof(FeaturesResources.Call_Dispose_on_object_created_by_0_before_all_references_to_it_are_out_of_scope), FeaturesResources.ResourceManager, typeof(FeaturesResources)),
            isUnneccessary: false);

        public DisposeObjectsBeforeLosingScopeDiagnosticAnalyzer()
            : base(ImmutableArray.Create(s_disposeObjectsBeforeLosingScopeRule), GeneratedCodeAnalysisFlags.None)
        {
        }

        public override bool OpenFileOnly(Workspace workspace) => false;

        public override DiagnosticAnalyzerCategory GetAnalyzerCategory() => DiagnosticAnalyzerCategory.SemanticDocumentAnalysis;

        protected override void InitializeWorker(AnalysisContext context)
        {
            context.RegisterCompilationStartAction(compilationContext =>
            {
                if (!DisposeAnalysisHelper.TryGetOrCreate(compilationContext.Compilation, out DisposeAnalysisHelper disposeAnalysisHelper))
                {
                    return;
                }

                var reportedLocations = new ConcurrentDictionary<Location, bool>();
                compilationContext.RegisterOperationBlockAction(operationBlockContext =>
                {
                    if (!(operationBlockContext.OwningSymbol is IMethodSymbol containingMethod) ||
                        !disposeAnalysisHelper.HasAnyDisposableCreationDescendant(operationBlockContext.OperationBlocks, containingMethod))
                    {
                        return;
                    }

                    var option = GetDisposeAnalysisOption(containingMethod, operationBlockContext.Options, operationBlockContext.CancellationToken);
                    if (option == null || option.Notification == NotificationOption.None)
                    {
                        return;
                    }

                    // We can skip interprocedural analysis for certain invocations.
                    var interproceduralAnalysisPredicateOpt = new InterproceduralAnalysisPredicate(
                        skipAnalysisForInvokedMethodPredicateOpt: SkipInterproceduralAnalysis,
                        skipAnalysisForInvokedLambdaOrLocalFunctionPredicateOpt: null,
                        skipAnalysisForInvokedContextPredicateOpt: null);

                    if (disposeAnalysisHelper.TryGetOrComputeResult(operationBlockContext.OperationBlocks, containingMethod,
                        operationBlockContext.Options, s_disposeObjectsBeforeLosingScopeRule, trackInstanceFields: false, trackExceptionPaths: false,
                        operationBlockContext.CancellationToken, out var disposeAnalysisResult, out var pointsToAnalysisResult,
                        interproceduralAnalysisPredicateOpt))
                    {
                        var notDisposedDiagnostics = ArrayBuilder<Diagnostic>.GetInstance();
                        var mayBeNotDisposedDiagnostics = ArrayBuilder<Diagnostic>.GetInstance();
                        try
                        {
                            // Compute diagnostics for undisposed objects at exit block for non-exceptional exit paths.
                            var exitBlock = disposeAnalysisResult.ControlFlowGraph.GetExit();
                            var disposeDataAtExit = disposeAnalysisResult.ExitBlockOutput.Data;
                            ComputeDiagnostics(disposeDataAtExit, notDisposedDiagnostics, mayBeNotDisposedDiagnostics,
                                disposeAnalysisResult, pointsToAnalysisResult, option);

                            // Report diagnostics preferring *not* disposed diagnostics over may be not disposed diagnostics
                            // and avoiding duplicates.
                            foreach (var diagnostic in notDisposedDiagnostics.Concat(mayBeNotDisposedDiagnostics))
                            {
                                if (reportedLocations.TryAdd(diagnostic.Location, true))
                                {
                                    operationBlockContext.ReportDiagnostic(diagnostic);
                                }
                            }
                        }
                        finally
                        {
                            notDisposedDiagnostics.Free();
                            mayBeNotDisposedDiagnostics.Free();
                        }
                    }
                });

                return;

                // Local functions.

                bool SkipInterproceduralAnalysis(IMethodSymbol invokedMethod)
                {
                    // Skip interprocedural analysis if we are invoking a method and not passing any disposable object as an argument.
                    // We also check that we are not passing any object type argument which might hold disposable object
                    // and also check that we are not passing delegate type argument which can
                    // be a lambda or local function that has access to disposable object in current method's scope.
                    foreach (var p in invokedMethod.Parameters)
                    {
                        if (p.Type.SpecialType == SpecialType.System_Object ||
                            p.Type.AllInterfaces.Contains(disposeAnalysisHelper.IDisposable) ||
                            p.Type.TypeKind == TypeKind.Delegate)
                        {
                            return false;
                        }
                    }

                    return true;
                }
            });
        }

        private static CodeStyleOption<DisposeAnalysisKind> GetDisposeAnalysisOption(IMethodSymbol method, AnalyzerOptions options, CancellationToken cancellationToken)
        {
            method = method.PartialImplementationPart ?? method;
            var optionSet = options.GetDocumentOptionSetAsync(method.Locations[0].SourceTree, cancellationToken).GetAwaiter().GetResult();
            return optionSet?.GetOption(CodeStyleOptions.DisposeAnalysis, method.Language);
        }

        private static void ComputeDiagnostics(
            ImmutableDictionary<AbstractLocation, DisposeAbstractValue> disposeData,
            ArrayBuilder<Diagnostic> notDisposedDiagnostics,
            ArrayBuilder<Diagnostic> mayBeNotDisposedDiagnostics,
            DisposeAnalysisResult disposeAnalysisResult,
            PointsToAnalysisResult pointsToAnalysisResult,
            CodeStyleOption<DisposeAnalysisKind> option)
        {
            foreach (var kvp in disposeData)
            {
                AbstractLocation location = kvp.Key;
                DisposeAbstractValue disposeValue = kvp.Value;
                if (disposeValue.Kind == DisposeAbstractValueKind.NotDisposable ||
                    location.CreationOpt == null)
                {
                    continue;
                }

                var isNotDisposed = disposeValue.Kind == DisposeAbstractValueKind.NotDisposed ||
                    (disposeValue.DisposingOrEscapingOperations.Count > 0 &&
                     disposeValue.DisposingOrEscapingOperations.All(d => d.IsInsideCatchRegion(disposeAnalysisResult.ControlFlowGraph)));
                var isMayBeNotDisposed = !isNotDisposed && (disposeValue.Kind == DisposeAbstractValueKind.MaybeDisposed || disposeValue.Kind == DisposeAbstractValueKind.NotDisposedOrEscaped);

                if (isNotDisposed ||
                    (isMayBeNotDisposed && option.Value.AreMayBeNotDisposedViolationsEnabled()))
                {
                    var syntax = location.TryGetNodeToReportDiagnostic(pointsToAnalysisResult);
                    if (syntax == null)
                    {
                        continue;
                    }

                    // CA2000: Call System.IDisposable.Dispose on object created by '{0}' before all references to it are out of scope.
                    var diagnostic = DiagnosticHelper.CreateWithMessage(
                        s_disposeObjectsBeforeLosingScopeRule,
                        syntax.GetLocation(),
                        option.Notification.Severity,
                        additionalLocations: null,
                        properties: null,
                        GetMessage(syntax, isNotDisposed));

                    if (isNotDisposed)
                    {
                        notDisposedDiagnostics.Add(diagnostic);
                    }
                    else
                    {
                        mayBeNotDisposedDiagnostics.Add(diagnostic);
                    }
                }
            }
        }

        private static LocalizableString GetMessage(SyntaxNode disposeAllocation, bool isNotDisposed)
        {
            var messageFormat = s_disposeObjectsBeforeLosingScopeRule.MessageFormat;
            if (!isNotDisposed)
            {
                // May be not disposed violation.
                messageFormat = FeaturesResources.Use_recommended_dispose_pattern_to_ensure_that_object_created_by_0_is_disposed_on_all_paths_If_possible_wrap_the_creation_within_a_using_statement_or_a_using_declaration_Otherwise_use_a_try_finally_pattern;
            }

            return new DiagnosticHelper.LocalizableStringWithArguments(messageFormat, disposeAllocation);
        }
    }
}
