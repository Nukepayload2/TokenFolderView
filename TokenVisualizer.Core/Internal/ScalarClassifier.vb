Imports System.Globalization

Namespace Internal

    ''' <summary>
    ''' A single Unicode scalar value classified once per scalar: its code point, UTF-16 (.NET)
    ''' length, UTF-8 length, and a bit field of derived regex predicates. This is the heart of
    ''' the manual-scanner optimization: instead of calling
    ''' <see cref="CharUnicodeInfo.GetUnicodeCategory(String, Int32)"/> once per predicate per
    ''' scalar (the R3 hot path did 8-10 category queries per "hard" scalar), a scanner calls
    ''' <see cref="ScalarClassifier.Classify"/> ONCE per scalar and then reads cheap bit flags.
    '''
    ''' A value type, so returning it from <see cref="ScalarClassifier.Classify"/> allocates
    ''' nothing. Callers read <see cref="Flags"/> with bit tests and <see cref="CodePoint"/> /
    ''' <see cref="Utf8Len"/> / <see cref="NetLen"/> for advancing.
    ''' </summary>
    Public Structure ScalarClass
        ''' <summary>The Unicode scalar value (code point) of the scalar.</summary>
        Public ReadOnly CodePoint As Integer
        ''' <summary>Number of UTF-8 bytes used by this scalar (1..4). A lone surrogate is the 3-byte U+FFFD.</summary>
        Public ReadOnly Utf8Len As Integer
        ''' <summary>Number of UTF-16 code units used by this scalar (1 or 2).</summary>
        Public ReadOnly NetLen As Integer
        ''' <summary>Predicate bit flags (see <see cref="ScalarClassifier"/> constants).</summary>
        Public ReadOnly Flags As Integer

        Public Sub New(codePoint As Integer, utf8Len As Integer, netLen As Integer, flags As Integer)
            Me.CodePoint = codePoint
            Me.Utf8Len = utf8Len
            Me.NetLen = netLen
            Me.Flags = flags
        End Sub
    End Structure

    ''' <summary>
    ''' Shared per-scalar classifier. The ASCII range uses a precomputed table (no
    ''' <see cref="CharUnicodeInfo"/> call at all); everything else does exactly one
    ''' <see cref="CharUnicodeInfo.GetUnicodeCategory"/> per scalar (surrogate-pair aware, so a
    ''' supplementary scalar is classified by its whole-pair category, matching the Rust regex
    ''' semantics the scanners emulate).
    '''
    ''' Semantics are locked to the previous per-predicate implementation:
    ''' <list type="bullet">
    ''' <item><see cref="FlagWhiteSpace"/> equals <c>Char.IsWhiteSpace</c> (net10 excludes U+FEFF,
    ''' matching Rust <c>\s</c>); a supplementary scalar is never whitespace.</item>
    ''' <item><see cref="FlagWord"/> = \p{Alphabetic} ∪ \p{M} ∪ \p{Nd} ∪ \p{Pc} ∪
    ''' Join_Control(U+200C/U+200D).</item>
    ''' <item><see cref="FlagCjk"/> = the DeepSeek three ranges (U+4E00..U+9FA5,
    ''' U+3040..U+309F, U+30A0..U+30FF).</item>
    ''' <item><see cref="FlagDg2Punct"/> = the DeepSeek four ASCII punctuation ranges.</item>
    ''' </list>
    ''' </summary>
    Public Module ScalarClassifier

        ' ---- Predicate bit flags ----
        ''' <summary>\p{L}: Lu/Ll/Lt/Lm/Lo.</summary>
        Public Const FlagLetter As Integer = 1
        ''' <summary>\p{N}: Nd/Nl/No.</summary>
        Public Const FlagNumber As Integer = 2
        ''' <summary>\p{M}: Mc/Me/Mn.</summary>
        Public Const FlagMark As Integer = 4
        ''' <summary>\p{P}: Pc/Pd/Pe/Pf/Pi/Po.</summary>
        Public Const FlagPunctuation As Integer = 8
        ''' <summary>\p{S}: Sc/Sk/Sm/So.</summary>
        Public Const FlagSymbol As Integer = 16
        ''' <summary>DecimalDigitNumber (Nd).</summary>
        Public Const FlagDecimalDigit As Integer = 32
        ''' <summary>LetterNumber (Nl).</summary>
        Public Const FlagLetterNumber As Integer = 64
        ''' <summary>ConnectorPunctuation (Pc).</summary>
        Public Const FlagConnectorPunct As Integer = 128
        ''' <summary>\s (matches <c>Char.IsWhiteSpace</c>; supplementary scalars are never whitespace).</summary>
        Public Const FlagWhiteSpace As Integer = 256
        ''' <summary>CR (U+000D) or LF (U+000A).</summary>
        Public Const FlagCrLf As Integer = 512
        ''' <summary>Exact U+0020 (the <c> ?</c> optional leading space).</summary>
        Public Const FlagExactSpace As Integer = 1024
        ''' <summary>[A-Za-z].</summary>
        Public Const FlagAsciiLetter As Integer = 2048
        ''' <summary>Rust \w = \p{Alphabetic} ∪ \p{M} ∪ \p{Nd} ∪ \p{Pc} ∪ Join_Control.</summary>
        Public Const FlagWord As Integer = 4096
        ''' <summary>DeepSeek CJK three ranges.</summary>
        Public Const FlagCjk As Integer = 8192
        ''' <summary>DeepSeek ASCII punctuation ranges.</summary>
        Public Const FlagDg2Punct As Integer = 16384

        ' ---- Composite masks used by the scanners ----
        ''' <summary>Letter or mark (DeepSeek \p{L}\p{M} runs).</summary>
        Public Const MaskLetterOrMark As Integer = FlagLetter Or FlagMark
        ''' <summary>Punctuation or symbol (DeepSeek \p{P}\p{S} runs).</summary>
        Public Const MaskPunctOrSymbol As Integer = FlagPunctuation Or FlagSymbol
        ''' <summary>Whitespace or letter or number (Gpt2 [^\s\p{L}\p{N}] complement).</summary>
        Public Const MaskWsLetterNumber As Integer = FlagWhiteSpace Or FlagLetter Or FlagNumber
        ''' <summary>CR/LF or letter or punctuation or symbol (DeepSeek [^\r\n\p{L}\p{P}\p{S}] complement).</summary>
        Public Const MaskCrLfLetterPunctSymbol As Integer = FlagCrLf Or FlagLetter Or FlagPunctuation Or FlagSymbol

        ' ---- 128-entry ASCII flag table (built once; ASCII never needs a GetUnicodeCategory call) ----
        Private ReadOnly AsciiFlags As Integer() = BuildAsciiFlags()

        ''' <summary>
        ''' Classifies the scalar starting at <paramref name="net"/>. Caller guarantees
        ''' <c>net &lt; text.Length</c>. Exactly one Unicode-category query for non-ASCII scalars;
        ''' zero for ASCII.
        ''' </summary>
        Public Function Classify(text As String, net As Integer) As ScalarClass
            Dim c As Char = text(net)
            Dim cp As Integer = AscW(c)
            If cp < 128 Then
                Return New ScalarClass(cp, 1, 1, AsciiFlags(cp))
            End If

            Dim netLen As Integer = 1
            Dim utf8Len As Integer
            If cp < &H800 Then
                utf8Len = 2
            Else
                utf8Len = 3
            End If

            ' Decode a surrogate pair into the supplementary scalar value.
            If cp >= &HD800 AndAlso cp <= &HDBFF AndAlso net + 1 < text.Length Then
                Dim lo As Integer = AscW(text(net + 1))
                If lo >= &HDC00 AndAlso lo <= &HDFFF Then
                    cp = ((cp - &HD800) << 10) + (lo - &HDC00) + &H10000
                    netLen = 2
                    utf8Len = 4
                End If
            End If

            ' One category query per scalar. For a surrogate pair (netLen=2) the string overload
            ' returns the whole-pair category; for a BMP scalar the char overload is equivalent
            ' and avoids the string-indexing surrogate checks.
            Dim cat As UnicodeCategory
            If netLen = 2 Then
                cat = CharUnicodeInfo.GetUnicodeCategory(text, net)
            Else
                cat = CharUnicodeInfo.GetUnicodeCategory(c)
            End If

            Return New ScalarClass(cp, utf8Len, netLen, ComputeFlags(cp, cat))
        End Function

        Private Function BuildAsciiFlags() As Integer()
            Dim arr(127) As Integer
            For cp As Integer = 0 To 127
                arr(cp) = ComputeFlags(cp, CharUnicodeInfo.GetUnicodeCategory(ChrW(cp)))
            Next
            Return arr
        End Function

        Private Function ComputeFlags(cp As Integer, cat As UnicodeCategory) As Integer
            Dim flags As Integer = 0
            If IsLetterCategory(cat) Then flags = flags Or FlagLetter
            If IsMarkCategory(cat) Then flags = flags Or FlagMark
            If IsNumberCategory(cat) Then flags = flags Or FlagNumber
            If cat = UnicodeCategory.DecimalDigitNumber Then flags = flags Or FlagDecimalDigit
            If cat = UnicodeCategory.LetterNumber Then flags = flags Or FlagLetterNumber
            If IsPunctuationCategory(cat) Then flags = flags Or FlagPunctuation
            If cat = UnicodeCategory.ConnectorPunctuation Then flags = flags Or FlagConnectorPunct
            If IsSymbolCategory(cat) Then flags = flags Or FlagSymbol

            ' Whitespace matches Char.IsWhiteSpace; a supplementary scalar is never whitespace.
            If cp < &H10000 Then
                If Char.IsWhiteSpace(ChrW(cp)) Then flags = flags Or FlagWhiteSpace
                If cp = 10 OrElse cp = 13 Then flags = flags Or FlagCrLf
            End If
            If cp = &H20 Then flags = flags Or FlagExactSpace

            If (cp >= &H41 AndAlso cp <= &H5A) OrElse (cp >= &H61 AndAlso cp <= &H7A) Then
                flags = flags Or FlagAsciiLetter
            End If

            ' Rust \w.
            If (flags And (FlagLetter Or FlagMark Or FlagDecimalDigit Or FlagLetterNumber Or FlagConnectorPunct)) <> 0 OrElse
               cp = &H200C OrElse cp = &H200D Then
                flags = flags Or FlagWord
            End If

            ' DeepSeek CJK three ranges.
            If (cp >= &H4E00 AndAlso cp <= &H9FA5) OrElse
               (cp >= &H3040 AndAlso cp <= &H309F) OrElse
               (cp >= &H30A0 AndAlso cp <= &H30FF) Then
                flags = flags Or FlagCjk
            End If

            ' DeepSeek ASCII punctuation ranges (!-/  :-@  [-\`  {-~).
            If (cp >= &H21 AndAlso cp <= &H2F) OrElse
               (cp >= &H3A AndAlso cp <= &H40) OrElse
               (cp >= &H5B AndAlso cp <= &H60) OrElse
               (cp >= &H7B AndAlso cp <= &H7E) Then
                flags = flags Or FlagDg2Punct
            End If

            Return flags
        End Function

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

    End Module

End Namespace
