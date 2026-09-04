Imports System.Text.Json
Imports Tokenizers.Internal
Imports Tokenizers.PreTokenizers

Namespace Serialization

    ''' <summary>
    ''' Shared enum/string conversions used by both <c>ToJson</c> serializers and the
    ''' <see cref="ComponentFactory"/> dispatcher. Mirrors the serde names used by the Rust
    ''' tokenizers.
    ''' </summary>
    Public Module SerializationHelpers

        ''' <summary>Returns the child element at <paramref name="key"/>, or <c>Nothing</c> when absent or null.</summary>
        Public Function GetProperty(el As JsonElement, key As String) As JsonElement?
            If el.ValueKind <> JsonValueKind.Object Then Return Nothing
            Dim child As JsonElement
            If el.TryGetProperty(key, child) AndAlso child.ValueKind <> JsonValueKind.Null Then
                Return child
            End If
            Return Nothing
        End Function

        ''' <summary>Reads a string field, or <c>Nothing</c> when absent, null, or not a string.</summary>
        Public Function GetString(el As JsonElement, key As String) As String
            Dim child As JsonElement
            If el.ValueKind = JsonValueKind.Object AndAlso el.TryGetProperty(key, child) AndAlso
               child.ValueKind = JsonValueKind.String Then
                Return child.GetString()
            End If
            Return Nothing
        End Function

        ''' <summary>Reads an integer field, or <c>Nothing</c> when absent, null, or not an integral number.</summary>
        Public Function GetInt(el As JsonElement, key As String) As Integer?
            Dim child As JsonElement
            Dim value As Integer
            If el.ValueKind = JsonValueKind.Object AndAlso el.TryGetProperty(key, child) AndAlso
               child.ValueKind = JsonValueKind.Number AndAlso child.TryGetInt32(value) Then
                Return value
            End If
            Return Nothing
        End Function

        ''' <summary>Reads a boolean field, or <c>Nothing</c> when absent or null.</summary>
        Public Function GetBool(el As JsonElement, key As String) As Boolean?
            Dim child As JsonElement
            If el.ValueKind = JsonValueKind.Object AndAlso el.TryGetProperty(key, child) AndAlso
               (child.ValueKind = JsonValueKind.True OrElse child.ValueKind = JsonValueKind.False) Then
                Return child.GetBoolean()
            End If
            Return Nothing
        End Function

        ''' <summary>Reads a double field, or <c>Nothing</c> when absent or null.</summary>
        Public Function GetDouble(el As JsonElement, key As String) As Double?
            Dim child As JsonElement
            Dim value As Double
            If el.ValueKind = JsonValueKind.Object AndAlso el.TryGetProperty(key, child) AndAlso
               child.ValueKind = JsonValueKind.Number AndAlso child.TryGetDouble(value) Then
                Return value
            End If
            Return Nothing
        End Function

        ''' <summary>Maps a <see cref="SplitDelimiterBehavior"/> to its serde string.</summary>
        Public Function SplitDelimiterBehaviorToString(behavior As SplitDelimiterBehavior) As String
            Select Case behavior
                Case SplitDelimiterBehavior.Removed
                    Return "Removed"
                Case SplitDelimiterBehavior.Isolated
                    Return "Isolated"
                Case SplitDelimiterBehavior.MergedWithPrevious
                    Return "MergedWithPrevious"
                Case SplitDelimiterBehavior.MergedWithNext
                    Return "MergedWithNext"
                Case SplitDelimiterBehavior.Contiguous
                    Return "Contiguous"
                Case Else
                    Return "Isolated"
            End Select
        End Function

        ''' <summary>
        ''' Parses a <see cref="SplitDelimiterBehavior"/> from its serde string, accepting both the
        ''' CamelCase names and the snake_case aliases.
        ''' </summary>
        Public Function ParseSplitDelimiterBehavior(s As String) As SplitDelimiterBehavior
            Select Case s
                Case "Removed", "removed"
                    Return SplitDelimiterBehavior.Removed
                Case "Isolated", "isolated"
                    Return SplitDelimiterBehavior.Isolated
                Case "MergedWithPrevious", "merged_with_previous"
                    Return SplitDelimiterBehavior.MergedWithPrevious
                Case "MergedWithNext", "merged_with_next"
                    Return SplitDelimiterBehavior.MergedWithNext
                Case "Contiguous", "contiguous"
                    Return SplitDelimiterBehavior.Contiguous
                Case Else
                    Throw New ArgumentException($"Unknown SplitDelimiterBehavior '{s}'")
            End Select
        End Function

        ''' <summary>Maps a <see cref="PrependScheme"/> to its snake_case serde string.</summary>
        Public Function PrependSchemeToString(scheme As PrependScheme) As String
            Select Case scheme
                Case PrependScheme.First
                    Return "first"
                Case PrependScheme.Never
                    Return "never"
                Case Else
                    Return "always"
            End Select
        End Function

        ''' <summary>
        ''' Parses a <see cref="PrependScheme"/> from its serde string, accepting both the
        ''' snake_case and CamelCase forms.
        ''' </summary>
        Public Function ParsePrependScheme(s As String) As PrependScheme
            Select Case s
                Case "first", "First"
                    Return PrependScheme.First
                Case "never", "Never"
                    Return PrependScheme.Never
                Case "always", "Always"
                    Return PrependScheme.Always
                Case Else
                    Throw New ArgumentException($"Unknown PrependScheme '{s}'")
            End Select
        End Function

        ''' <summary>Maps a <see cref="TruncationDirection"/> to its serde string.</summary>
        Public Function TruncationDirectionToString(d As TruncationDirection) As String
            Return If(d = TruncationDirection.Left, "left", "right")
        End Function

        ''' <summary>Parses a <see cref="TruncationDirection"/> accepting both cases.</summary>
        Public Function ParseTruncationDirection(s As String) As TruncationDirection
            If String.Equals(s, "left", StringComparison.OrdinalIgnoreCase) Then Return TruncationDirection.Left
            If String.Equals(s, "right", StringComparison.OrdinalIgnoreCase) Then Return TruncationDirection.Right
            Throw New ArgumentException($"Unknown TruncationDirection '{s}'")
        End Function

        ''' <summary>Maps a <see cref="TruncationStrategy"/> to its serde string.</summary>
        Public Function TruncationStrategyToString(s As TruncationStrategy) As String
            Select Case s
                Case TruncationStrategy.OnlyFirst
                    Return "OnlyFirst"
                Case TruncationStrategy.OnlySecond
                    Return "OnlySecond"
                Case Else
                    Return "LongestFirst"
            End Select
        End Function

        ''' <summary>Parses a <see cref="TruncationStrategy"/> accepting both cases.</summary>
        Public Function ParseTruncationStrategy(s As String) As TruncationStrategy
            Select Case s
                Case "LongestFirst", "longest_first"
                    Return TruncationStrategy.LongestFirst
                Case "OnlyFirst", "only_first"
                    Return TruncationStrategy.OnlyFirst
                Case "OnlySecond", "only_second"
                    Return TruncationStrategy.OnlySecond
                Case Else
                    Throw New ArgumentException($"Unknown TruncationStrategy '{s}'")
            End Select
        End Function

        ''' <summary>Maps a <see cref="PaddingDirection"/> to its serde string.</summary>
        Public Function PaddingDirectionToString(d As PaddingDirection) As String
            Return If(d = PaddingDirection.Left, "left", "right")
        End Function

        ''' <summary>Parses a <see cref="PaddingDirection"/> accepting both cases.</summary>
        Public Function ParsePaddingDirection(s As String) As PaddingDirection
            If String.Equals(s, "left", StringComparison.OrdinalIgnoreCase) Then Return PaddingDirection.Left
            If String.Equals(s, "right", StringComparison.OrdinalIgnoreCase) Then Return PaddingDirection.Right
            Throw New ArgumentException($"Unknown PaddingDirection '{s}'")
        End Function

        ''' <summary>
        ''' Serializes a <see cref="PaddingStrategy"/> to its serde form: <c>"BatchLongest"</c> or
        ''' <c>{"Fixed": N}</c>.
        ''' </summary>
        Public Function PaddingStrategyToNode(strategy As PaddingStrategy) As System.Text.Json.Nodes.JsonNode
            If strategy.Kind = PaddingStrategyKind.Fixed Then
                Dim o As New System.Text.Json.Nodes.JsonObject()
                o("Fixed") = strategy.FixedLength
                Return o
            Else
                Return System.Text.Json.Nodes.JsonValue.Create("BatchLongest")
            End If
        End Function

        ''' <summary>
        ''' Parses a <see cref="PaddingStrategy"/> from its serde form (either the string
        ''' <c>"BatchLongest"</c> or <c>{"Fixed": N}</c>).
        ''' </summary>
        Public Function ParsePaddingStrategy(prop As JsonElement?) As PaddingStrategy
            If Not prop.HasValue OrElse prop.Value.ValueKind = JsonValueKind.Null Then
                Return PaddingStrategy.BatchLongest
            End If
            Dim el As JsonElement = prop.Value
            If el.ValueKind = JsonValueKind.String Then
                Dim s As String = el.GetString()
                If String.Equals(s, "BatchLongest", StringComparison.OrdinalIgnoreCase) Then
                    Return PaddingStrategy.BatchLongest
                End If
                Throw New ArgumentException($"Unknown PaddingStrategy '{s}'")
            End If
            If el.ValueKind = JsonValueKind.Object Then
                Dim fixedVal As JsonElement
                Dim fixedLength As Integer
                If el.TryGetProperty("Fixed", fixedVal) AndAlso fixedVal.ValueKind = JsonValueKind.Number AndAlso
                   fixedVal.TryGetInt32(fixedLength) Then
                    Return PaddingStrategy.Fixed(fixedLength)
                End If
            End If
            Throw New ArgumentException("Invalid PaddingStrategy JSON")
        End Function

    End Module

End Namespace
