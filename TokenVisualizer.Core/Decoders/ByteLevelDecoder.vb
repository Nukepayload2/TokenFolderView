Imports System.Collections.Generic
Imports System.Linq
Imports System.Text
Imports System.Text.Json.Nodes
Imports Tokenizers.Internal

Namespace Decoders

    ''' <summary>
    ''' Port of the Rust <c>ByteLevel</c> decoder (pre_tokenizers/byte_level.rs). Converts the
    ''' GPT-2 byte-level characters back to bytes, then decodes the merged byte stream as UTF-8
    ''' (lossy). A token containing any character not in the byte-to-char map falls back to that
    ''' token's raw UTF-8 bytes.
    ''' </summary>
    Public NotInheritable Class ByteLevelDecoder
        Implements IDecoder

        Public ReadOnly AddPrefixSpace As Boolean
        Public ReadOnly TrimOffsets As Boolean
        Public ReadOnly UseRegex As Boolean

        Public Sub New()
            Me.New(True, True, True)
        End Sub

        Public Sub New(addPrefixSpace As Boolean, trimOffsets As Boolean, useRegex As Boolean)
            Me.AddPrefixSpace = addPrefixSpace
            Me.TrimOffsets = trimOffsets
            Me.UseRegex = useRegex
        End Sub

        Public Function DecodeChain(tokens As IEnumerable(Of String)) As List(Of String) Implements IDecoder.DecodeChain
            Dim charToBytes As IReadOnlyDictionary(Of Char, Byte) = BytesToUnicodeTable.GetCharToBytes()
            Dim allBytes As New List(Of Byte)()

            For Each t In If(tokens, Enumerable.Empty(Of String)())
                If t Is Nothing Then Continue For
                Dim mapped As Boolean = True
                Dim tokenBytes As New List(Of Byte)()
                For Each c As Char In t
                    If charToBytes.ContainsKey(c) Then
                        tokenBytes.Add(charToBytes(c))
                    Else
                        mapped = False
                        Exit For
                    End If
                Next
                If mapped Then
                    allBytes.AddRange(tokenBytes)
                Else
                    allBytes.AddRange(Global.System.Text.Encoding.UTF8.GetBytes(t))
                End If
            Next

            Dim result As String = Global.System.Text.Encoding.UTF8.GetString(allBytes.ToArray())
            Return New List(Of String) From {result}
        End Function

        Public Function ToJson() As JsonObject Implements IDecoder.ToJson
            Dim o As New JsonObject()
            o("type") = "ByteLevel"
            o("add_prefix_space") = AddPrefixSpace
            o("trim_offsets") = TrimOffsets
            o("use_regex") = UseRegex
            Return o
        End Function
    End Class

End Namespace
