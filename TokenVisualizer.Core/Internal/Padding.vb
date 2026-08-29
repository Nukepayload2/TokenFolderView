Imports System.Linq

Namespace Internal

    ''' <summary>The various possible padding directions. Mirrors the Rust <c>PaddingDirection</c>.</summary>
    Public Enum PaddingDirection
        Left
        Right
    End Enum

    ''' <summary>Kind of a <see cref="PaddingStrategy"/>.</summary>
    Public Enum PaddingStrategyKind
        BatchLongest
        Fixed
    End Enum

    ''' <summary>
    ''' The padding strategy: pad to the longest sequence of the batch, or to a fixed length.
    ''' Mirrors the Rust <c>PaddingStrategy</c> enum. VB enums cannot carry data, so this is a
    ''' small structure with a <see cref="PaddingStrategyKind"/> and an optional fixed length.
    ''' </summary>
    Public Structure PaddingStrategy
        Public Kind As PaddingStrategyKind
        Public FixedLength As Integer

        Public Sub New(kind As PaddingStrategyKind, fixedLength As Integer)
            Me.Kind = kind
            Me.FixedLength = fixedLength
        End Sub

        Public Shared ReadOnly Property BatchLongest As PaddingStrategy
            Get
                Return New PaddingStrategy(PaddingStrategyKind.BatchLongest, 0)
            End Get
        End Property

        Public Shared Function Fixed(size As Integer) As PaddingStrategy
            Return New PaddingStrategy(PaddingStrategyKind.Fixed, size)
        End Function
    End Structure

    ''' <summary>Parameters for padding. Mirrors the Rust <c>PaddingParams</c>.</summary>
    Public Class PaddingParams
        Public Strategy As PaddingStrategy
        Public Direction As PaddingDirection
        Public PadToMultipleOf As Integer?
        Public PadId As Integer
        Public PadTypeId As Integer
        Public PadToken As String

        Public Sub New()
            Strategy = PaddingStrategy.BatchLongest
            Direction = PaddingDirection.Right
            PadToMultipleOf = Nothing
            PadId = 0
            PadTypeId = 0
            PadToken = "[PAD]"
        End Sub
    End Class

    ''' <summary>
    ''' Padding helpers. Faithful port of the Rust <c>utils::padding::pad_encodings</c>.
    ''' </summary>
    Public Module Padding

        ''' <summary>
        ''' Pads all the given encodings to the length mandated by the strategy (batch longest or
        ''' fixed), rounded up to the optional multiple. Mirrors the Rust <c>pad_encodings</c>.
        ''' </summary>
        Public Sub PadEncodings(encodings As List(Of Encoding), params As PaddingParams)
            If encodings Is Nothing OrElse encodings.Count = 0 Then Return

            Dim padLength As Integer
            If params.Strategy.Kind = PaddingStrategyKind.Fixed Then
                padLength = params.Strategy.FixedLength
            Else
                padLength = encodings.Max(Function(e) e.Length)
            End If

            If params.PadToMultipleOf.HasValue Then
                Dim multiple As Integer = params.PadToMultipleOf.Value
                If multiple > 0 AndAlso padLength Mod multiple > 0 Then
                    padLength += multiple - padLength Mod multiple
                End If
            End If

            For Each encoding In encodings
                encoding.Pad(padLength, params.PadId, params.PadTypeId, params.PadToken, params.Direction)
            Next
        End Sub

    End Module

End Namespace
