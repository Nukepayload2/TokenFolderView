Imports System.Globalization
Imports System.Text.Json.Nodes
Imports Tokenizers.Internal

Namespace Normalizers

    ''' <summary>
    ''' Port of the Rust <c>BertNormalizer</c> (normalizers/bert.rs).
    ''' Applies in order: clean_text, handle_chinese_chars, strip_accents
    ''' (resolved as strip_accents ?? lowercase), lowercase.
    ''' </summary>
    Public NotInheritable Class BertNormalizer
        Implements INormalizer

        Private ReadOnly _cleanText As Boolean
        Private ReadOnly _handleChineseChars As Boolean
        Private ReadOnly _stripAccents As Boolean?
        Private ReadOnly _lowercase As Boolean

        Public Sub New(Optional cleanText As Boolean = True,
                       Optional handleChineseChars As Boolean = True,
                       Optional stripAccents As Boolean? = Nothing,
                       Optional lowercase As Boolean = True)
            _cleanText = cleanText
            _handleChineseChars = handleChineseChars
            _stripAccents = stripAccents
            _lowercase = lowercase
        End Sub

        Public Sub Normalize(normalized As Internal.NormalizedString) Implements INormalizer.Normalize
            If _cleanText Then DoCleanText(normalized)
            If _handleChineseChars Then DoHandleChineseChars(normalized)

            Dim stripAccents As Boolean = If(_stripAccents.HasValue, _stripAccents.Value, _lowercase)
            If stripAccents Then DoStripAccents(normalized)
            If _lowercase Then DoLowercase(normalized)
        End Sub

        Public Function ToJson() As JsonObject Implements INormalizer.ToJson
            Dim o As New JsonObject()
            o("type") = "BertNormalizer"
            o("clean_text") = _cleanText
            o("handle_chinese_chars") = _handleChineseChars
            o("strip_accents") = If(_stripAccents.HasValue, JsonValue.Create(_stripAccents.Value), Nothing)
            o("lowercase") = _lowercase
            Return o
        End Function

        ' ------------------------------------------------------------------
        ' Steps
        ' ------------------------------------------------------------------

        ''' <summary>
        ''' 1. Remove any control characters (Cc/Cf/Cn/Co except tab/LF/CR), the NUL char and
        '''    U+FFFD, then replace all sorts of whitespace by the classic ' '.
        ''' </summary>
        Private Sub DoCleanText(normalized As Internal.NormalizedString)
            normalized.Filter(
                Function(c)
                    Dim cp As Integer = AscW(c)
                    Return Not (cp = 0 OrElse cp = &HFFFD OrElse IsControl(c))
                End Function).Map(
                Function(c)
                    Return If(IsWhitespace(c), " "c, c)
                End Function)
        End Sub

        ''' <summary>2. Put spaces around Chinese characters so they get split.</summary>
        Private Sub DoHandleChineseChars(normalized As Internal.NormalizedString)
            Dim newChars As New List(Of (String, Integer))()
            Dim s As String = normalized.Get
            For Each sc In Utf8Helpers.EnumerateScalars(s)
                Dim cp As Integer = UnicodePredicates.ScalarCodePoint(s, sc.NetStart)
                If IsChineseChar(cp) Then
                    newChars.Add((" ", 0))
                    newChars.Add((sc.Value, 1))
                    newChars.Add((" ", 1))
                Else
                    newChars.Add((sc.Value, 0))
                End If
            Next
            normalized.Transform(newChars, 0)
        End Sub

        ''' <summary>3. NFD then remove any non-spacing combining marks.</summary>
        Private Sub DoStripAccents(normalized As Internal.NormalizedString)
            normalized.Nfd().Filter(Function(c) Not IsNonspacingMark(c))
        End Sub

        ''' <summary>4. Lowercase the input.</summary>
        Private Sub DoLowercase(normalized As Internal.NormalizedString)
            normalized.Lowercase()
        End Sub

        ' ------------------------------------------------------------------
        ' Predicates
        ' ------------------------------------------------------------------

        ''' <summary>Whether the char is a control char (Cc/Cf/Cn/Co), excluding tab/LF/CR.</summary>
        Private Shared Function IsControl(c As Char) As Boolean
            Select Case AscW(c)
                Case 9, 10, 13 ' tab, LF, CR are counted as whitespace, not control
                    Return False
            End Select
            Dim cat As UnicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c)
            Return cat = UnicodeCategory.Control OrElse
                   cat = UnicodeCategory.Format OrElse
                   cat = UnicodeCategory.OtherNotAssigned OrElse
                   cat = UnicodeCategory.PrivateUse
        End Function

        ''' <summary>Whether the char is whitespace (tab/LF/CR count as whitespace).</summary>
        Private Shared Function IsWhitespace(c As Char) As Boolean
            Select Case AscW(c)
                Case 9, 10, 13 ' tab, LF, CR count as whitespace
                    Return True
                Case Else
                    Return Char.IsWhiteSpace(c)
            End Select
        End Function

        ''' <summary>
        ''' Whether the scalar value is in the CJK Unicode block ranges. Mirrors
        ''' <c>is_chinese_char</c> in bert.rs.
        ''' </summary>
        Private Shared Function IsChineseChar(cp As Integer) As Boolean
            Return (cp >= &H4E00 AndAlso cp <= &H9FFF) OrElse
                   (cp >= &H3400 AndAlso cp <= &H4DBF) OrElse
                   (cp >= &H20000 AndAlso cp <= &H2A6DF) OrElse
                   (cp >= &H2A700 AndAlso cp <= &H2B73F) OrElse
                   (cp >= &H2B740 AndAlso cp <= &H2B81F) OrElse
                   (cp >= &H2B920 AndAlso cp <= &H2CEAF) OrElse
                   (cp >= &HF900 AndAlso cp <= &HFAFF) OrElse
                   (cp >= &H2F800 AndAlso cp <= &H2FA1F)
        End Function

        ''' <summary>Whether the char is a non-spacing combining mark (General_Category = Mn).</summary>
        Private Shared Function IsNonspacingMark(c As Char) As Boolean
            Return CharUnicodeInfo.GetUnicodeCategory(c) = UnicodeCategory.NonSpacingMark
        End Function

    End Class

End Namespace
