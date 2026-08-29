Imports System.Globalization
Imports System.Text
Imports System.Text.Encodings.Web
Imports System.Text.Json
Imports System.Text.Json.Nodes
Imports Tokenizers.Internal

Namespace TokenVisualizer.Core.Tests

    Friend Module TestHelpers

        ''' <summary>
        ''' Serializes a JsonNode without escaping non-ASCII (mirrors serde_json's behavior), so
        ''' byte-exact comparisons in the serialization tests use raw UTF-8.
        ''' </summary>
        Public Function SerializeJson(node As JsonNode) As String
            Return node.ToJsonString(New JsonSerializerOptions With {
                .Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            })
        End Function

        ''' <summary>
        ''' Builds the byte-level transform stream for the given string: each scalar is mapped to
        ''' its UTF-8 bytes and each byte is mapped through the GPT-2 byte-to-char table, with
        ''' change 0 for the first byte and 1 for the following bytes.
        ''' </summary>
        Public Function ByteLevelTransform(s As String) As List(Of (String, Integer))
            Dim result As New List(Of (String, Integer))()
            For Each sc In Utf8Helpers.EnumerateScalars(s)
                Dim bytes As Byte() = Global.System.Text.Encoding.UTF8.GetBytes(sc.Value)
                For i As Integer = 0 To bytes.Length - 1
                    Dim ch As Char = BytesToUnicodeTable.GetBytesToChar()(bytes(i))
                    result.Add((ch.ToString(), If(i = 0, 0, 1)))
                Next
            Next
            Return result
        End Function

        ''' <summary>Whether a character is a combining mark (General_Category = Mark).</summary>
        Public Function IsCombiningMark(c As Char) As Boolean
            Dim cat As UnicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c)
            Return cat = UnicodeCategory.NonSpacingMark OrElse
                   cat = UnicodeCategory.SpacingCombiningMark OrElse
                   cat = UnicodeCategory.EnclosingMark
        End Function

        ''' <summary>Asserts the state of a NormalizedString against the expected values.</summary>
        Public Sub AssertNormalized(n As NormalizedString,
                                    expectedOriginal As String,
                                    expectedNormalized As String,
                                    expectedAlignments As List(Of (Integer, Integer)))
            Microsoft.VisualStudio.TestTools.UnitTesting.Assert.AreEqual(expectedOriginal, n.Original)
            Microsoft.VisualStudio.TestTools.UnitTesting.Assert.AreEqual(expectedNormalized, n.Get)
            CollectionAssert.AreEqual(expectedAlignments, n.Alignments)
        End Sub

    End Module

End Namespace
