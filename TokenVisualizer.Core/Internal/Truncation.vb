Namespace Internal

    ''' <summary>Direction in which truncation should be applied. Mirrors the Rust <c>TruncationDirection</c>.</summary>
    Public Enum TruncationDirection
        Left
        Right
    End Enum

    ''' <summary>Strategy used to truncate a (possibly paired) sequence. Mirrors the Rust <c>TruncationStrategy</c>.</summary>
    Public Enum TruncationStrategy
        LongestFirst
        OnlyFirst
        OnlySecond
    End Enum

    ''' <summary>Parameters for truncation. Mirrors the Rust <c>TruncationParams</c>.</summary>
    Public Class TruncationParams
        Public Direction As TruncationDirection
        Public MaxLength As Integer
        Public Strategy As TruncationStrategy
        Public Stride As Integer

        Public Sub New()
            Direction = TruncationDirection.Right
            MaxLength = 512
            Strategy = TruncationStrategy.LongestFirst
            Stride = 0
        End Sub
    End Class

    ''' <summary>
    ''' Truncation helpers. Faithful port of the Rust <c>utils::truncation::truncate_encodings</c>.
    ''' </summary>
    Public Module Truncation

        ''' <summary>
        ''' Truncates the given encoding (and optionally its pair), returning both. Mirrors the Rust
        ''' <c>truncate_encodings</c> exactly, including the error behavior for <c>OnlySecond</c>
        ''' without a pair and the <c>SequenceTooShort</c> error.
        ''' </summary>
        Public Function TruncateEncodings(encoding As Encoding, pairEncoding As Encoding, params As TruncationParams) As (Encoding, Encoding)
            If params.MaxLength = 0 Then
                encoding.Truncate(0, params.Stride, params.Direction)
                If pairEncoding IsNot Nothing Then
                    pairEncoding.Truncate(0, params.Stride, params.Direction)
                End If
                Return (encoding, pairEncoding)
            End If

            Dim totalLength As Integer = encoding.Length + If(pairEncoding Is Nothing, 0, pairEncoding.Length)
            If totalLength <= params.MaxLength Then
                Return (encoding, pairEncoding)
            End If
            Dim toRemove As Integer = totalLength - params.MaxLength

            Select Case params.Strategy
                Case TruncationStrategy.LongestFirst
                    If pairEncoding IsNot Nothing Then
                        Dim n1 As Integer = encoding.Length
                        Dim n2 As Integer = pairEncoding.Length
                        Dim swap As Boolean = False

                        ' Ensure n1 is the length of the shortest input.
                        If n1 > n2 Then
                            swap = True
                            Dim tmp As Integer = n1
                            n1 = n2
                            n2 = tmp
                        End If

                        If n1 > params.MaxLength Then
                            ' Special case to avoid max_length - n1 < 0.
                            n2 = n1
                        Else
                            n2 = Math.Max(n1, params.MaxLength - n1)
                        End If

                        If n1 + n2 > params.MaxLength Then
                            n1 = params.MaxLength \ 2
                            n2 = n1 + params.MaxLength Mod 2
                        End If

                        If swap Then
                            Dim tmp As Integer = n1
                            n1 = n2
                            n2 = tmp
                        End If
                        encoding.Truncate(n1, params.Stride, params.Direction)
                        pairEncoding.Truncate(n2, params.Stride, params.Direction)
                    Else
                        encoding.Truncate(totalLength - toRemove, params.Stride, params.Direction)
                    End If

                Case TruncationStrategy.OnlyFirst, TruncationStrategy.OnlySecond
                    Dim target As Encoding
                    If params.Strategy = TruncationStrategy.OnlyFirst Then
                        target = encoding
                    ElseIf pairEncoding IsNot Nothing Then
                        target = pairEncoding
                    Else
                        Throw New InvalidOperationException("Truncation error: Second sequence not provided")
                    End If

                    Dim targetLen As Integer = target.Length
                    If targetLen > toRemove Then
                        target.Truncate(targetLen - toRemove, params.Stride, params.Direction)
                    Else
                        Throw New InvalidOperationException("Truncation error: Sequence to truncate too short to respect the provided max_length")
                    End If
            End Select

            Return (encoding, pairEncoding)
        End Function

    End Module

End Namespace
