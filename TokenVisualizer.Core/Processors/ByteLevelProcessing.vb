Imports System.Linq
Imports System.Text.Json.Nodes
Imports Tokenizers.Internal

Namespace Processors

    ''' <summary>
    ''' Post-processor that trims byte-level offsets. Mirrors the Rust <c>ByteLevel</c> post-processor
    ''' implementation (pre_tokenizers/byte_level.rs).
    ''' </summary>
    Public NotInheritable Class ByteLevelProcessing
        Implements IPostProcessor

        Public ReadOnly AddPrefixSpace As Boolean
        Public ReadOnly TrimOffsets As Boolean
        Public ReadOnly UseRegex As Boolean

        Public Sub New(addPrefixSpace As Boolean, trimOffsets As Boolean, useRegex As Boolean)
            Me.AddPrefixSpace = addPrefixSpace
            Me.TrimOffsets = trimOffsets
            Me.UseRegex = useRegex
        End Sub

        Public Sub New()
            Me.New(True, True, True)
        End Sub

        Public Function GetAddedTokens(isPair As Boolean) As Integer Implements IPostProcessor.GetAddedTokens
            Return 0
        End Function

        Public Function Process(enc As Encoding, pairEnc As Encoding, addSpecialTokens As Boolean) As Encoding
            Return PostProcessorHelpers.DefaultProcess(Me, enc, pairEnc, addSpecialTokens)
        End Function

        Public Function ProcessEncodings(encodings As List(Of Encoding), addSpecialTokens As Boolean) As List(Of Encoding) Implements IPostProcessor.ProcessEncodings
            If TrimOffsets Then
                For Each e As Encoding In encodings
                    ProcessOffsets(e, AddPrefixSpace)
                    For Each o As Encoding In e.Overflowing
                        ProcessOffsets(o, AddPrefixSpace)
                    Next
                Next
            End If
            For i As Integer = 0 To encodings.Count - 1
                encodings(i).SetSequenceId(i)
            Next
            Return encodings
        End Function

        ''' <summary>
        ''' Trims leading/trailing byte-level space glyphs out of each token's offsets. Mirrors the Rust
        ''' <c>process_offsets</c> free function (pre_tokenizers/byte_level.rs).
        ''' </summary>
        Public Shared Sub ProcessOffsets(encoding As Encoding, addPrefixSpace As Boolean)
            For i As Integer = 0 To encoding.Tokens.Count - 1
                Dim token As String = encoding.Tokens(i)
                Dim offsets As (Integer, Integer) = encoding.Offsets(i)

                Dim leadingSpaces As Integer = 0
                While leadingSpaces < token.Length AndAlso IsSpaceGlyph(token(leadingSpaces))
                    leadingSpaces += 1
                End While
                Dim trailingSpaces As Integer = 0
                While trailingSpaces < token.Length AndAlso IsSpaceGlyph(token(token.Length - 1 - trailingSpaces))
                    trailingSpaces += 1
                End While

                If leadingSpaces > 0 OrElse trailingSpaces > 0 Then
                    If leadingSpaces > 0 Then
                        ' If user uses `is_pretokenized=True` we might have offsets that begin at
                        ' the start of the string but are NOT the first token.
                        Dim isFirst As Boolean = (i = 0) OrElse (offsets.Item1 = 0)
                        If isFirst AndAlso addPrefixSpace AndAlso leadingSpaces = 1 Then
                            ' If we are processing the first pair of offsets, with add_prefix_space,
                            ' then we shouldn't remove anything we added. If there are more than one
                            ' leading spaces though, it means we didn't add them, and they should be
                            ' removed.
                            leadingSpaces = 0
                        End If
                        offsets.Item1 = Math.Min(offsets.Item1 + leadingSpaces, offsets.Item2)
                    End If
                    If trailingSpaces > 0 AndAlso offsets.Item2 >= trailingSpaces Then
                        offsets.Item2 = Math.Max(offsets.Item2 - trailingSpaces, offsets.Item1)
                    End If
                    encoding.Offsets(i) = offsets
                End If
            Next
        End Sub

        Private Shared Function IsSpaceGlyph(c As Char) As Boolean
            ' BYTES_CHAR[&b' '] is the GPT-2 space glyph (U+0120).
            Return c = "Ġ"c OrElse Char.IsWhiteSpace(c)
        End Function

        Public Function ToJson() As JsonObject Implements IPostProcessor.ToJson
            Dim o As New JsonObject()
            o("type") = "ByteLevel"
            o("add_prefix_space") = AddPrefixSpace
            o("trim_offsets") = TrimOffsets
            o("use_regex") = UseRegex
            Return o
        End Function
    End Class

End Namespace
