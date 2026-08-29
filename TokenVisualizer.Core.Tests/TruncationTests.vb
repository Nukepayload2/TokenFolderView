Imports Tokenizers.Internal

Namespace TokenVisualizer.Core.Tests

    ''' <summary>
    ''' Ports the Rust <c>utils/truncation.rs</c> unit tests, including the exhaustive
    ''' LongestFirst matrix.
    ''' </summary>
    <TestClass>
    Public Class TruncationTests

        Private Shared Function GetEmpty() As Encoding
            Return New Encoding()
        End Function

        Private Shared Function GetShort() As Encoding
            Return MakeEncoding({1, 2}, {"a", "b"}, {0, 1}, {(0, 1), (1, 2)})
        End Function

        Private Shared Function GetMedium() As Encoding
            Return MakeEncoding({3, 4, 5, 6}, {"d", "e", "f", "g"}, {0, 1, 2, 3}, {(0, 1), (1, 2), (2, 3), (3, 4)})
        End Function

        Private Shared Function GetLong() As Encoding
            Return MakeEncoding(
                {7, 8, 9, 10, 11, 12, 13, 14},
                {"h", "i", "j", "k", "l", "m", "n", "o"},
                {0, 1, 2, 3, 4, 5, 6, 7},
                {(0, 1), (1, 2), (2, 3), (3, 4), (4, 5), (5, 6), (6, 7), (6, 8)})
        End Function

        Private Shared Function MakeEncoding(ids As Integer(), tokens As String(), words As Integer?(), offsets As (Integer, Integer)()) As Encoding
            Dim e As New Encoding()
            e.Ids = ids.ToList()
            e.TypeIds = Enumerable.Repeat(0, ids.Length).ToList()
            e.Tokens = tokens.ToList()
            e.Words = words.ToList()
            e.Offsets = offsets.ToList()
            e.SpecialTokensMask = Enumerable.Repeat(0, ids.Length).ToList()
            e.AttentionMask = Enumerable.Repeat(1, ids.Length).ToList()
            Return e
        End Function

        Private Shared Sub TruncateAndAssert(encoding1 As Encoding, encoding2 As Encoding, params As TruncationParams, n1 As Integer, n2 As Integer)
            Dim result As (Encoding, Encoding) = Truncation.TruncateEncodings(encoding1, encoding2, params)
            Assert.AreEqual(n1, result.Item1.Length, "first encoding length")
            Assert.AreEqual(n2, result.Item2.Length, "second encoding length")
        End Sub

        <TestMethod>
        Public Sub TruncateEncodingsLongestFirst()
            Dim params As New TruncationParams()
            params.MaxLength = 7
            params.Strategy = TruncationStrategy.LongestFirst
            params.Stride = 0
            params.Direction = TruncationDirection.Right

            TruncateAndAssert(GetEmpty(), GetEmpty(), params, 0, 0)
            TruncateAndAssert(GetEmpty(), GetShort(), params, 0, 2)
            TruncateAndAssert(GetEmpty(), GetMedium(), params, 0, 4)
            TruncateAndAssert(GetEmpty(), GetLong(), params, 0, 7)

            TruncateAndAssert(GetShort(), GetEmpty(), params, 2, 0)
            TruncateAndAssert(GetShort(), GetShort(), params, 2, 2)
            TruncateAndAssert(GetShort(), GetMedium(), params, 2, 4)
            TruncateAndAssert(GetShort(), GetLong(), params, 2, 5)

            TruncateAndAssert(GetMedium(), GetEmpty(), params, 4, 0)
            TruncateAndAssert(GetMedium(), GetShort(), params, 4, 2)
            TruncateAndAssert(GetMedium(), GetMedium(), params, 3, 4)
            TruncateAndAssert(GetMedium(), GetLong(), params, 3, 4)

            TruncateAndAssert(GetLong(), GetEmpty(), params, 7, 0)
            TruncateAndAssert(GetLong(), GetShort(), params, 5, 2)
            TruncateAndAssert(GetLong(), GetMedium(), params, 4, 3)
            TruncateAndAssert(GetLong(), GetLong(), params, 3, 4)
        End Sub

        <TestMethod>
        Public Sub TruncateEncodingsEmpty()
            Dim params As New TruncationParams()
            params.MaxLength = 0
            params.Strategy = TruncationStrategy.LongestFirst
            params.Stride = 0
            params.Direction = TruncationDirection.Right

            TruncateAndAssert(GetEmpty(), GetShort(), params, 0, 0)
            TruncateAndAssert(GetMedium(), GetMedium(), params, 0, 0)
            TruncateAndAssert(GetLong(), GetLong(), params, 0, 0)
        End Sub

        <TestMethod>
        Public Sub OnlyFirstTruncatesFirst()
            Dim params As New TruncationParams()
            params.MaxLength = 4
            params.Strategy = TruncationStrategy.OnlyFirst
            params.Stride = 0
            params.Direction = TruncationDirection.Right

            Dim result As (Encoding, Encoding) = Truncation.TruncateEncodings(GetMedium(), GetShort(), params)
            Assert.AreEqual(2, result.Item1.Length)
            Assert.AreEqual(2, result.Item2.Length)
        End Sub

        <TestMethod>
        Public Sub OnlySecondTruncatesSecond()
            Dim params As New TruncationParams()
            params.MaxLength = 4
            params.Strategy = TruncationStrategy.OnlySecond
            params.Stride = 0
            params.Direction = TruncationDirection.Right

            Dim result As (Encoding, Encoding) = Truncation.TruncateEncodings(GetShort(), GetMedium(), params)
            Assert.AreEqual(2, result.Item1.Length)
            Assert.AreEqual(2, result.Item2.Length)
        End Sub

        <TestMethod>
        Public Sub OnlySecondWithoutPairThrows()
            Dim params As New TruncationParams()
            params.MaxLength = 4
            params.Strategy = TruncationStrategy.OnlySecond
            params.Stride = 0
            params.Direction = TruncationDirection.Right

            ' total length (8) > max_length (4) so the OnlySecond branch is reached, and since
            ' there is no pair, the Rust code raises SecondSequenceNotProvided.
            Assert.ThrowsExactly(Of InvalidOperationException)(
                Sub()
                    Truncation.TruncateEncodings(GetLong(), Nothing, params)
                End Sub)
        End Sub

    End Class

End Namespace
