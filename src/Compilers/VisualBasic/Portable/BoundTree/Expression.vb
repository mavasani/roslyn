' Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

Imports System.Collections.Immutable

Namespace Microsoft.CodeAnalysis.VisualBasic
    Partial Friend Class BoundBadExpression
        Protected Overrides ReadOnly Property Children As ImmutableArray(Of BoundNode)
            Get
                Return StaticCast(Of BoundNode).From(Me.ChildBoundNodes)
            End Get
        End Property
    End Class

    Partial Friend Class BoundAssignmentOperator
        Protected Overrides ReadOnly Property Children As ImmutableArray(Of BoundNode)
            Get
                Return ImmutableArray.Create(Of BoundNode)(Me.Left, Me.Right)
            End Get
        End Property
    End Class

    Partial Friend Class BoundDelegateCreationExpression
        Protected Overrides ReadOnly Property Children As ImmutableArray(Of BoundNode)
            Get
                Return ImmutableArray.Create(Of BoundNode)(Me.ReceiverOpt)
            End Get
        End Property
    End Class

    Partial Friend Class BoundAddressOfOperator
        Protected Overrides ReadOnly Property Children As ImmutableArray(Of BoundNode)
            Get
                Return ImmutableArray.Create(Of BoundNode)(Me.MethodGroup)
            End Get
        End Property
    End Class

    Partial Friend Class BoundMethodOrPropertyGroup
        Protected Overrides ReadOnly Property Children As ImmutableArray(Of BoundNode)
            Get
                If Me.ReceiverOpt IsNot Nothing Then
                    Return ImmutableArray.Create(Of BoundNode)(Me.ReceiverOpt)
                Else
                    Return ImmutableArray(Of BoundNode).Empty
                End If
            End Get
        End Property
    End Class

    Partial Friend Class BoundNullableIsTrueOperator
        Protected Overrides ReadOnly Property Children As ImmutableArray(Of BoundNode)
            Get
                Return ImmutableArray.Create(Of BoundNode)(Me.Operand)
            End Get
        End Property
    End Class

    Partial Friend Class BoundAttribute
        Protected Overrides ReadOnly Property Children As ImmutableArray(Of BoundNode)
            Get
                Return StaticCast(Of BoundNode).From(Me.ConstructorArguments.AddRange(Me.NamedArguments))
            End Get
        End Property
    End Class

    Partial Friend Class BoundLateInvocation
        Protected Overrides ReadOnly Property Children As ImmutableArray(Of BoundNode)
            Get
                Return StaticCast(Of BoundNode).From(Me.ArgumentsOpt.Insert(0, Me.Member))
            End Get
        End Property
    End Class

    Partial Friend Class BoundLateAddressOfOperator
        Protected Overrides ReadOnly Property Children As ImmutableArray(Of BoundNode)
            Get
                Return ImmutableArray.Create(Of BoundNode)(Me.MemberAccess)
            End Get
        End Property
    End Class

    Partial Friend Class BoundNoPiaObjectCreationExpression
        Protected Overrides ReadOnly Property Children As ImmutableArray(Of BoundNode)
            Get
                Return If(InitializerOpt IsNot Nothing, StaticCast(Of BoundNode).From(InitializerOpt.Initializers), ImmutableArray(Of BoundNode).Empty)
            End Get
        End Property
    End Class

    Partial Friend Class BoundAnonymousTypeCreationExpression
        Protected Overrides ReadOnly Property Children As ImmutableArray(Of BoundNode)
            Get
                Return StaticCast(Of BoundNode).From(Me.Arguments)
            End Get
        End Property
    End Class

    Partial Friend Class BoundAnonymousTypeFieldInitializer
        Protected Overrides ReadOnly Property Children As ImmutableArray(Of BoundNode)
            Get
                Return ImmutableArray.Create(Of BoundNode)(Me.Value)
            End Get
        End Property
    End Class

    Partial Friend Class BoundArrayLiteral
        Protected Overrides ReadOnly Property Children As ImmutableArray(Of BoundNode)
            Get
                Return StaticCast(Of BoundNode).From(Me.Bounds.Add(Me.Initializer))
            End Get
        End Property
    End Class

    Partial Friend Class BoundQueryExpression
        Protected Overrides ReadOnly Property Children As ImmutableArray(Of BoundNode)
            Get
                Return ImmutableArray.Create(Of BoundNode)(Me.LastOperator)
            End Get
        End Property
    End Class

    Partial Friend Class BoundQueryPart
        Protected MustOverride Overrides ReadOnly Property Children As ImmutableArray(Of BoundNode)
    End Class

    Partial Friend Class BoundQuerySource
        Protected Overrides ReadOnly Property Children As ImmutableArray(Of BoundNode)
            Get
                Return ImmutableArray.Create(Of BoundNode)(Me.Expression)
            End Get
        End Property
    End Class

    Partial Friend Class BoundToQueryableCollectionConversion
        Protected Overrides ReadOnly Property Children As ImmutableArray(Of BoundNode)
            Get
                Return ImmutableArray.Create(Of BoundNode)(Me.ConversionCall)
            End Get
        End Property
    End Class

    Partial Friend Class BoundQueryableSource
        Protected Overrides ReadOnly Property Children As ImmutableArray(Of BoundNode)
            Get
                Return ImmutableArray.Create(Of BoundNode)(Me.Source)
            End Get
        End Property
    End Class

    Partial Friend Class BoundQueryClause
        Protected Overrides ReadOnly Property Children As ImmutableArray(Of BoundNode)
            Get
                Return ImmutableArray.Create(Of BoundNode)(Me.UnderlyingExpression)
            End Get
        End Property
    End Class

    Partial Friend Class BoundOrdering
        Protected Overrides ReadOnly Property Children As ImmutableArray(Of BoundNode)
            Get
                Return ImmutableArray.Create(Of BoundNode)(Me.UnderlyingExpression)
            End Get
        End Property
    End Class

    Partial Friend Class BoundQueryLambda
        Protected Overrides ReadOnly Property Children As ImmutableArray(Of BoundNode)
            Get
                Return ImmutableArray.Create(Of BoundNode)(Me.Expression)
            End Get
        End Property
    End Class

    Partial Friend Class BoundRangeVariableAssignment
        Protected Overrides ReadOnly Property Children As ImmutableArray(Of BoundNode)
            Get
                Return ImmutableArray.Create(Of BoundNode)(Me.Value)
            End Get
        End Property
    End Class

    Partial Friend Class BoundAggregateClause
        Protected Overrides ReadOnly Property Children As ImmutableArray(Of BoundNode)
            Get
                Return ImmutableArray.Create(Of BoundNode)(Me.CapturedGroupOpt, Me.UnderlyingExpression)
            End Get
        End Property
    End Class

    Partial Friend Class BoundGroupAggregation
        Protected Overrides ReadOnly Property Children As ImmutableArray(Of BoundNode)
            Get
                Return ImmutableArray.Create(Of BoundNode)(Me.Group)
            End Get
        End Property
    End Class

    Partial Friend Class BoundMidResult
        Protected Overrides ReadOnly Property Children As ImmutableArray(Of BoundNode)
            Get
                Return ImmutableArray.Create(Of BoundNode)(Me.Original, Me.Start, Me.LengthOpt, Me.Source)
            End Get
        End Property
    End Class

    Partial Friend Class BoundNameOfOperator
        Protected Overrides ReadOnly Property Children As ImmutableArray(Of BoundNode)
            Get
                Return ImmutableArray.Create(Of BoundNode)(Me.Argument)
            End Get
        End Property
    End Class

    Partial Friend Class BoundXmlName
        Protected Overrides ReadOnly Property Children As ImmutableArray(Of BoundNode)
            Get
                Return ImmutableArray.Create(Of BoundNode)(Me.XmlNamespace, Me.LocalName, Me.ObjectCreation)
            End Get
        End Property
    End Class

    Partial Friend Class BoundXmlNamespace
        Protected Overrides ReadOnly Property Children As ImmutableArray(Of BoundNode)
            Get
                Return ImmutableArray.Create(Of BoundNode)(Me.XmlNamespace, Me.ObjectCreation)
            End Get
        End Property
    End Class

    Partial Friend Class BoundXmlDocument
        Protected Overrides ReadOnly Property Children As ImmutableArray(Of BoundNode)
            Get
                Return StaticCast(Of BoundNode).From(Me.ChildNodes).Insert(0, Me.Declaration)
            End Get
        End Property
    End Class

    Partial Friend Class BoundXmlDeclaration
        Protected Overrides ReadOnly Property Children As ImmutableArray(Of BoundNode)
            Get
                Return ImmutableArray.Create(Of BoundNode)(Me.Version, Me.Encoding, Me.Standalone, Me.ObjectCreation)
            End Get
        End Property
    End Class

    Partial Friend Class BoundXmlProcessingInstruction
        Protected Overrides ReadOnly Property Children As ImmutableArray(Of BoundNode)
            Get
                Return ImmutableArray.Create(Of BoundNode)(Me.Target, Me.Data, Me.ObjectCreation)
            End Get
        End Property
    End Class

    Partial Friend Class BoundXmlComment
        Protected Overrides ReadOnly Property Children As ImmutableArray(Of BoundNode)
            Get
                Return ImmutableArray.Create(Of BoundNode)(Me.Value, Me.ObjectCreation)
            End Get
        End Property
    End Class

    Partial Friend Class BoundXmlAttribute
        Protected Overrides ReadOnly Property Children As ImmutableArray(Of BoundNode)
            Get
                Return ImmutableArray.Create(Of BoundNode)(Me.Name, Me.Value, Me.ObjectCreation)
            End Get
        End Property
    End Class

    Partial Friend Class BoundXmlElement
        Protected Overrides ReadOnly Property Children As ImmutableArray(Of BoundNode)
            Get
                Return StaticCast(Of BoundNode).From(Me.ChildNodes).Insert(0, Me.Argument)
            End Get
        End Property
    End Class

    Partial Friend Class BoundXmlMemberAccess
        Protected Overrides ReadOnly Property Children As ImmutableArray(Of BoundNode)
            Get
                Return ImmutableArray.Create(Of BoundNode)(Me.MemberAccess)
            End Get
        End Property
    End Class

    Partial Friend Class BoundXmlEmbeddedExpression
        Protected Overrides ReadOnly Property Children As ImmutableArray(Of BoundNode)
            Get
                Return ImmutableArray.Create(Of BoundNode)(Me.Expression)
            End Get
        End Property
    End Class

    Partial Friend Class BoundXmlCData
        Protected Overrides ReadOnly Property Children As ImmutableArray(Of BoundNode)
            Get
                Return ImmutableArray.Create(Of BoundNode)(Me.Value, Me.ObjectCreation)
            End Get
        End Property
    End Class

End Namespace
