Imports System.Text.Json.Nodes
Imports Tokenizers.Internal

Namespace Normalizers

    ''' <summary>
    ''' Port of the Rust <c>Precompiled</c> normalizer (normalizers/precompiled.rs, backed by the
    ''' SentencePiece precompiled charsmap).
    '''
    ''' The real charsmap blob format is NOT decoded in this task (out of scope); the grapheme
    ''' lookup is driven by a test-visible mapping populated via <see cref="SetMapping"/>. When
    ''' the blob is empty the normalizer is a no-op, mirroring precompiled.rs.
    ''' </summary>
    Public NotInheritable Class PrecompiledNormalizer
        Implements INormalizer

        Private ReadOnly _precompiledCharsmap As Byte()
        Private ReadOnly _mapping As Dictionary(Of String, String)

        Public Sub New(precompiledCharsmap As Byte())
            If precompiledCharsmap Is Nothing Then
                _precompiledCharsmap = Array.Empty(Of Byte)()
            Else
                _precompiledCharsmap = precompiledCharsmap
            End If
            _mapping = New Dictionary(Of String, String)(StringComparer.Ordinal)
        End Sub

        ''' <summary>Adds (or replaces) a grapheme→replacement entry used by <see cref="Normalize"/>.</summary>
        Public Sub SetMapping(grapheme As String, replacement As String)
            _mapping(grapheme) = replacement
        End Sub

        Public Sub Normalize(normalized As Internal.NormalizedString) Implements INormalizer.Normalize
            If _precompiledCharsmap.Length = 0 Then Return

            Dim transformations As New List(Of (String, Integer))()
            Dim modified As Boolean = False

            ' Iterate the normalized text's graphemes. This implementation iterates Unicode
            ' scalars (surrogate-pair aware) rather than full extended grapheme clusters; a
            ' scalar is at most 4 UTF-8 bytes, so the < 6 byte fast path always applies. The
            ' per-char fallback below is retained to mirror precompiled.rs exactly.
            For Each sc In Utf8Helpers.EnumerateScalars(normalized.Get)
                Dim grapheme As String = Utf8Helpers.ScalarToString(sc.CodePoint)
                Dim norm As String = Nothing
                If Utf8Helpers.Utf8Length(grapheme) < 6 AndAlso _mapping.TryGetValue(grapheme, norm) Then
                    modified = True
                    ReplaceTransform(transformations, grapheme, norm)
                Else
                    ' Per-char loop over the grapheme.
                    Dim part As String = grapheme
                    If _mapping.TryGetValue(part, norm) Then
                        modified = True
                        ReplaceTransform(transformations, part, norm)
                    Else
                        transformations.Add((grapheme, 0))
                    End If
                End If
            Next

            If modified Then
                normalized.Transform(transformations, 0)
            End If
        End Sub

        ''' <summary>
        ''' Serializes this normalizer as <c>{"type":"Precompiled","precompiled_charsmap":&lt;base64&gt;}</c>.
        ''' </summary>
        Public Function ToJson() As JsonObject Implements INormalizer.ToJson
            Dim o As New JsonObject()
            o("type") = "Precompiled"
            o("precompiled_charsmap") = Convert.ToBase64String(_precompiledCharsmap)
            Return o
        End Function

        ''' <summary>
        ''' Appends the replacement of <paramref name="oldPart"/> by <paramref name="newPart"/> to
        ''' <paramref name="transformations"/>, adjusting change values per precompiled.rs
        ''' <c>replace</c>.
        ''' </summary>
        Private Shared Sub ReplaceTransform(transformations As List(Of (String, Integer)),
                                            oldPart As String,
                                            newPart As String)
            Dim oldCount As Integer = Utf8Helpers.ScalarCount(oldPart)
            Dim newCount As Integer = Utf8Helpers.ScalarCount(newPart)
            Dim diff As Integer = newCount - oldCount

            ' If we are just replacing characters, all changes should be == 0.
            For Each sc In Utf8Helpers.EnumerateScalars(newPart)
                transformations.Add((Utf8Helpers.ScalarToString(sc.CodePoint), 0))
            Next

            If diff > 0 Then
                ' Adding some characters: the last DIFF characters should be == 1.
                Dim lastIndex As Integer = transformations.Count - 1
                For k As Integer = 0 To diff - 1
                    Dim idx As Integer = lastIndex - k
                    Dim item As (String, Integer) = transformations(idx)
                    transformations(idx) = (item.Item1, 1)
                Next
            ElseIf diff < 0 Then
                ' Removing some characters: the last one should include the diff.
                Dim lastIndex As Integer = transformations.Count - 1
                If lastIndex >= 0 Then
                    Dim item As (String, Integer) = transformations(lastIndex)
                    transformations(lastIndex) = (item.Item1, item.Item2 + diff)
                End If
            End If
        End Sub

    End Class

End Namespace
