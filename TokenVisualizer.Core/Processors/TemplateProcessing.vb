Imports System.Linq
Imports System.Text.Json.Nodes
Imports Tokenizers.Internal

Namespace Processors

    ''' <summary>
    ''' A single piece of a template: either an input sequence (<c>SequenceId</c> = 'A' or 'B') or a
    ''' special token (<c>TokenId</c>). Mirrors the Rust <c>Piece</c> enum (processors/template.rs).
    ''' </summary>
    Public NotInheritable Class TemplatePiece
        Public ReadOnly IsSequence As Boolean
        Public ReadOnly SequenceId As Char
        Public TypeId As Integer
        Public ReadOnly TokenId As String

        Friend Sub New(isSequence As Boolean, sequenceId As Char, typeId As Integer, tokenId As String)
            Me.IsSequence = isSequence
            Me.SequenceId = sequenceId
            Me.TypeId = typeId
            Me.TokenId = tokenId
        End Sub
    End Class

    ''' <summary>
    ''' A bunch of ids/tokens associated to a special token identifier, used by a template. Mirrors
    ''' the Rust <c>SpecialToken</c> struct (processors/template.rs).
    ''' </summary>
    Public NotInheritable Class SpecialToken
        Public ReadOnly Id As String
        Public ReadOnly Ids As List(Of Integer)
        Public ReadOnly Tokens As List(Of String)

        Friend Sub New(id As String, ids As List(Of Integer), tokens As List(Of String))
            Me.Id = id
            Me.Ids = ids
            Me.Tokens = tokens
        End Sub
    End Class

    ''' <summary>
    ''' Post-processor that applies a template to the input encodings, adding special tokens as
    ''' configured. Mirrors the Rust <c>TemplateProcessing</c> (processors/template.rs).
    ''' </summary>
    Public NotInheritable Class TemplateProcessing
        Implements IPostProcessor

        Private ReadOnly _single As List(Of TemplatePiece)
        Private ReadOnly _pair As List(Of TemplatePiece)
        Private ReadOnly _specialTokens As Dictionary(Of String, SpecialToken)

        Public ReadOnly Property AddedSingle As Integer
        Public ReadOnly Property AddedPair As Integer

        ''' <summary>
        ''' Builds a <c>TemplateProcessing</c> from template strings and a map of special tokens.
        ''' The map keys are special token identifiers and values are (ids, tokens) pairs.
        ''' </summary>
        Public Sub New([single] As String,
                       pair As String,
                       specialTokens As Dictionary(Of String, (List(Of Integer), List(Of String))))
            _single = ParseTemplate([single])
            _pair = ParseTemplate(pair)
            _specialTokens = New Dictionary(Of String, SpecialToken)()
            For Each kv In specialTokens
                _specialTokens(kv.Key) = New SpecialToken(kv.Key, kv.Value.Item1, kv.Value.Item2)
            Next
            Validate()
            AddedSingle = CountAdded(_single)
            AddedPair = CountAdded(_pair)
        End Sub

        ''' <summary>Builds the default: single = "$0", pair = "$A:0 $B:1", no special tokens.</summary>
        Public Sub New()
            Me.New("$0", "$A:0 $B:1", New Dictionary(Of String, (List(Of Integer), List(Of String)))())
        End Sub

        Public Function GetAddedTokens(isPair As Boolean) As Integer Implements IPostProcessor.GetAddedTokens
            Return If(isPair, AddedPair, AddedSingle)
        End Function

        Public Function Process(enc As Encoding, pairEnc As Encoding, addSpecialTokens As Boolean) As Encoding
            Return PostProcessorHelpers.DefaultProcess(Me, enc, pairEnc, addSpecialTokens)
        End Function

        Public Function ProcessEncodings(encodings As List(Of Encoding), addSpecialTokens As Boolean) As List(Of Encoding) Implements IPostProcessor.ProcessEncodings
            Dim template As List(Of TemplatePiece) = If(encodings.Count = 2, _pair, _single)
            Return ApplyTemplate(template, encodings, addSpecialTokens)
        End Function

        ''' <summary>Applies the template to the given encodings, returning the piece encodings.</summary>
        Private Function ApplyTemplate(template As List(Of TemplatePiece), encodings As List(Of Encoding), addSpecialTokens As Boolean) As List(Of Encoding)
            Dim finalEncodings As New List(Of Encoding)()
            For Each piece As TemplatePiece In template
                If piece.IsSequence Then
                    Dim i As Integer = If(piece.SequenceId = "A"c, 0, 1)
                    Dim e As Encoding = encodings(i)
                    e.TypeIds = Enumerable.Repeat(piece.TypeId, e.Length).ToList()
                    e.SetSequenceId(i)
                    finalEncodings.Add(e.Clone())
                Else
                    If addSpecialTokens Then
                        Dim tok As SpecialToken = _specialTokens(piece.TokenId)
                        Dim len As Integer = tok.Ids.Count
                        Dim zeroOffset As (Integer, Integer) = (0, 0)
                        Dim e As New Encoding()
                        e.Ids = New List(Of Integer)(tok.Ids)
                        e.TypeIds = Enumerable.Repeat(piece.TypeId, len).ToList()
                        e.Tokens = New List(Of String)(tok.Tokens)
                        e.Words = Enumerable.Repeat(Of Integer?)(Nothing, len).ToList()
                        e.Offsets = Enumerable.Repeat(zeroOffset, len).ToList()
                        e.SpecialTokensMask = Enumerable.Repeat(1, len).ToList()
                        e.AttentionMask = Enumerable.Repeat(1, len).ToList()
                        e.Overflowing = New List(Of Encoding)()
                        e.SequenceRanges = New Dictionary(Of Integer, (Integer, Integer))()
                        finalEncodings.Add(e)
                    End If
                End If
            Next
            Return finalEncodings
        End Function

        Private Shared Function ParseTemplate(s As String) As List(Of TemplatePiece)
            Return s.Split(" "c).Select(Function(p) ParsePiece(p)).ToList()
        End Function

        ''' <summary>Parses a single template piece. Mirrors <c>Piece::try_from</c>.</summary>
        Public Shared Function ParsePiece(s As String) As TemplatePiece
            Dim err As New ArgumentException($"Cannot build Piece from string ""{s}""")
            Dim parts As String() = s.Split(":"c)
            If parts.Length = 1 Then
                Dim piece As TemplatePiece = ExtractId(parts(0))
                If piece Is Nothing Then Throw err
                Return piece
            ElseIf parts.Length = 2 Then
                Dim typeId As Integer
                If Not Integer.TryParse(parts(1), typeId) Then Throw err
                Dim piece As TemplatePiece = ExtractId(parts(0))
                If piece Is Nothing Then Throw err
                piece.TypeId = typeId
                Return piece
            Else
                Throw err
            End If
        End Function

        Private Shared Function ExtractId(s As String) As TemplatePiece
            If s.StartsWith("$"c) Then
                Dim rest As String = s.Substring(1)
                Select Case rest
                    Case "", "A", "a"
                        Return New TemplatePiece(True, "A"c, 0, Nothing)
                    Case "B", "b"
                        Return New TemplatePiece(True, "B"c, 0, Nothing)
                    Case Else
                        Dim typeId As Integer
                        If Integer.TryParse(rest, typeId) Then
                            Return New TemplatePiece(True, "A"c, typeId, Nothing)
                        Else
                            Return Nothing
                        End If
                End Select
            Else
                Return New TemplatePiece(False, " "c, 0, s)
            End If
        End Function

        Private Sub Validate()
            ' The pair template must use both sequences.
            Dim hasA As Boolean = False
            Dim hasB As Boolean = False
            For Each p As TemplatePiece In _pair
                If p.IsSequence Then
                    If p.SequenceId = "A"c Then hasA = True
                    If p.SequenceId = "B"c Then hasB = True
                End If
            Next
            If Not (hasA AndAlso hasB) Then
                Throw New ArgumentException("Template for `pair` must use both sequences")
            End If

            ' Every SpecialToken piece id must exist in the special_tokens map.
            Dim missing As New SortedSet(Of String)()
            For Each p As TemplatePiece In _single
                If Not p.IsSequence AndAlso Not _specialTokens.ContainsKey(p.TokenId) Then missing.Add(p.TokenId)
            Next
            For Each p As TemplatePiece In _pair
                If Not p.IsSequence AndAlso Not _specialTokens.ContainsKey(p.TokenId) Then missing.Add(p.TokenId)
            Next
            If missing.Count > 0 Then
                Throw New ArgumentException("Missing SpecialToken(s) with id(s) `" + String.Join(", ", missing) + "`")
            End If
        End Sub

        Private Function CountAdded(template As List(Of TemplatePiece)) As Integer
            Dim count As Integer = 0
            For Each p As TemplatePiece In template
                If Not p.IsSequence AndAlso _specialTokens.ContainsKey(p.TokenId) Then
                    count += _specialTokens(p.TokenId).Ids.Count
                End If
            Next
            Return count
        End Function

        Public Function ToJson() As JsonObject Implements IPostProcessor.ToJson
            Dim o As New JsonObject()
            o("type") = "TemplateProcessing"
            o("single") = TemplateToJson(_single)
            o("pair") = TemplateToJson(_pair)
            Dim special As New JsonObject()
            For Each key As String In _specialTokens.Keys.OrderBy(Function(k) k, StringComparer.Ordinal)
                Dim st As SpecialToken = _specialTokens(key)
                Dim entry As New JsonObject()
                entry("id") = st.Id
                Dim idsArr As New JsonArray()
                For Each id As Integer In st.Ids
                    idsArr.Add(id)
                Next
                entry("ids") = idsArr
                Dim tokensArr As New JsonArray()
                For Each t As String In st.Tokens
                    tokensArr.Add(t)
                Next
                entry("tokens") = tokensArr
                special(key) = entry
            Next
            o("special_tokens") = special
            Return o
        End Function

        Private Shared Function TemplateToJson(template As List(Of TemplatePiece)) As JsonArray
            Dim arr As New JsonArray()
            For Each p As TemplatePiece In template
                Dim inner As New JsonObject()
                If p.IsSequence Then
                    inner("id") = p.SequenceId.ToString()
                Else
                    inner("id") = p.TokenId
                End If
                inner("type_id") = p.TypeId
                Dim wrapper As New JsonObject()
                If p.IsSequence Then
                    wrapper("Sequence") = inner
                Else
                    wrapper("SpecialToken") = inner
                End If
                arr.Add(wrapper)
            Next
            Return arr
        End Function

    End Class

End Namespace
