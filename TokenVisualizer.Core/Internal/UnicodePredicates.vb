Imports System.Globalization

Namespace Internal

    ''' <summary>
    ''' Scalar-aware Unicode category predicates. These follow the Unicode General Category
    ''' model (like the Rust <c>regex</c> crate): a supplementary scalar (surrogate pair) is
    ''' treated as a single character, so e.g. U+20000 is a letter and U+1F44B is a symbol.
    '''
    ''' NOTE: .NET's own <see cref="System.Text.RegularExpressions.Regex"/> does NOT treat
    ''' surrogate pairs this way (it classifies each UTF-16 code unit, giving category
    ''' "Surrogate"), so the manual scanners in <c>ManualPattern.vb</c> use a separate set of
    ''' "regex-emulation" predicates to stay byte-for-byte identical to the .NET regex engine.
    ''' </summary>
    Public Module UnicodePredicates

        Private Function IsLetterCategory(cat As UnicodeCategory) As Boolean
            Return cat = UnicodeCategory.UppercaseLetter OrElse
                   cat = UnicodeCategory.LowercaseLetter OrElse
                   cat = UnicodeCategory.TitlecaseLetter OrElse
                   cat = UnicodeCategory.ModifierLetter OrElse
                   cat = UnicodeCategory.OtherLetter
        End Function

        Private Function IsNumberCategory(cat As UnicodeCategory) As Boolean
            Return cat = UnicodeCategory.DecimalDigitNumber OrElse
                   cat = UnicodeCategory.LetterNumber OrElse
                   cat = UnicodeCategory.OtherNumber
        End Function

        Private Function IsPunctuationCategory(cat As UnicodeCategory) As Boolean
            Return cat = UnicodeCategory.ConnectorPunctuation OrElse
                   cat = UnicodeCategory.DashPunctuation OrElse
                   cat = UnicodeCategory.OpenPunctuation OrElse
                   cat = UnicodeCategory.ClosePunctuation OrElse
                   cat = UnicodeCategory.InitialQuotePunctuation OrElse
                   cat = UnicodeCategory.FinalQuotePunctuation OrElse
                   cat = UnicodeCategory.OtherPunctuation
        End Function

        Private Function IsSymbolCategory(cat As UnicodeCategory) As Boolean
            Return cat = UnicodeCategory.MathSymbol OrElse
                   cat = UnicodeCategory.CurrencySymbol OrElse
                   cat = UnicodeCategory.ModifierSymbol OrElse
                   cat = UnicodeCategory.OtherSymbol
        End Function

        Private Function IsMarkCategory(cat As UnicodeCategory) As Boolean
            Return cat = UnicodeCategory.NonSpacingMark OrElse
                   cat = UnicodeCategory.SpacingCombiningMark OrElse
                   cat = UnicodeCategory.EnclosingMark
        End Function

        ''' <summary><c>\p{L}</c>: Lu/Ll/Lt/Lm/Lo.</summary>
        Public Function IsLetter(text As String, netIndex As Integer) As Boolean
            Return IsLetterCategory(CharUnicodeInfo.GetUnicodeCategory(text, netIndex))
        End Function

        ''' <summary>Single-char overload (BMP only; a lone surrogate is never a letter).</summary>
        Public Function IsLetter(c As Char) As Boolean
            Return IsLetterCategory(CharUnicodeInfo.GetUnicodeCategory(c))
        End Function

        ''' <summary><c>\p{N}</c>: Nd/Nl/No.</summary>
        Public Function IsNumber(text As String, netIndex As Integer) As Boolean
            Return IsNumberCategory(CharUnicodeInfo.GetUnicodeCategory(text, netIndex))
        End Function

        ''' <summary>Single-char overload (BMP only).</summary>
        Public Function IsNumber(c As Char) As Boolean
            Return IsNumberCategory(CharUnicodeInfo.GetUnicodeCategory(c))
        End Function

        ''' <summary><c>\p{P}</c>: Pc/Pd/Pe/Pf/Pi/Po.</summary>
        Public Function IsPunctuation(text As String, netIndex As Integer) As Boolean
            Return IsPunctuationCategory(CharUnicodeInfo.GetUnicodeCategory(text, netIndex))
        End Function

        ''' <summary>Single-char overload (BMP only).</summary>
        Public Function IsPunctuation(c As Char) As Boolean
            Return IsPunctuationCategory(CharUnicodeInfo.GetUnicodeCategory(c))
        End Function

        ''' <summary><c>\p{S}</c>: Sc/Sk/Sm/So.</summary>
        Public Function IsSymbol(text As String, netIndex As Integer) As Boolean
            Return IsSymbolCategory(CharUnicodeInfo.GetUnicodeCategory(text, netIndex))
        End Function

        ''' <summary>Single-char overload (BMP only).</summary>
        Public Function IsSymbol(c As Char) As Boolean
            Return IsSymbolCategory(CharUnicodeInfo.GetUnicodeCategory(c))
        End Function

        ''' <summary><c>\p{M}</c>: Mc/Me/Mn.</summary>
        Public Function IsMark(text As String, netIndex As Integer) As Boolean
            Return IsMarkCategory(CharUnicodeInfo.GetUnicodeCategory(text, netIndex))
        End Function

        ''' <summary>Single-char overload (BMP only).</summary>
        Public Function IsMark(c As Char) As Boolean
            Return IsMarkCategory(CharUnicodeInfo.GetUnicodeCategory(c))
        End Function

        ''' <summary>
        ''' <c>\s</c> via <see cref="Char.IsWhiteSpace(String, Integer)"/>. On net10 this excludes
        ''' U+FEFF, matching the Rust <c>regex</c> crate's <c>\s</c> (Unicode White_Space).
        ''' </summary>
        Public Function IsWhiteSpace(text As String, netIndex As Integer) As Boolean
            Return Char.IsWhiteSpace(text, netIndex)
        End Function

        ''' <summary>Single-char overload.</summary>
        Public Function IsWhiteSpace(c As Char) As Boolean
            Return Char.IsWhiteSpace(c)
        End Function

        ''' <summary>
        ''' Rust <c>\w</c> = <c>[\p{Alphabetic}\p{M}\p{Nd}\p{Pc}\p{Join_Control}]</c>:
        ''' letter (Lu/Ll/Lt/Lm/Lo) OR Letter_Number (Nl, e.g. Roman numerals — part of
        ''' \p{Alphabetic}) OR mark (all of Mc/Me/Mn) OR decimal digit (Nd) OR connector
        ''' punctuation (Pc) OR a Join_Control (U+200C / U+200D).
        ''' </summary>
        Public Function IsWord(text As String, netIndex As Integer) As Boolean
            Dim cat As UnicodeCategory = CharUnicodeInfo.GetUnicodeCategory(text, netIndex)
            If IsLetterCategory(cat) OrElse IsMarkCategory(cat) OrElse
               cat = UnicodeCategory.DecimalDigitNumber OrElse
               cat = UnicodeCategory.LetterNumber OrElse
               cat = UnicodeCategory.ConnectorPunctuation Then
                Return True
            End If
            Dim cp As Integer = ScalarCodePoint(text, netIndex)
            Return cp = &H200C OrElse cp = &H200D
        End Function

        ''' <summary>Single-char overload (BMP only).</summary>
        Public Function IsWord(c As Char) As Boolean
            Return IsWord(c.ToString(), 0)
        End Function

        ''' <summary><c>[A-Za-z]</c>: ASCII letters only.</summary>
        Public Function IsAsciiLetter(c As Char) As Boolean
            Return (c >= "A"c AndAlso c <= "Z"c) OrElse (c >= "a"c AndAlso c <= "z"c)
        End Function

        ''' <summary>Gets the Unicode scalar value (code point) of the scalar at the given .NET index.</summary>
        Public Function ScalarCodePoint(text As String, netIndex As Integer) As Integer
            Dim c As Char = text(netIndex)
            Dim hi As Integer = AscW(c)
            If hi >= &HD800 AndAlso hi <= &HDBFF AndAlso netIndex + 1 < text.Length Then
                Dim lo As Integer = AscW(text(netIndex + 1))
                If lo >= &HDC00 AndAlso lo <= &HDFFF Then
                    Return ((hi - &HD800) << 10) + (lo - &HDC00) + &H10000
                End If
            End If
            Return hi
        End Function

    End Module

End Namespace
