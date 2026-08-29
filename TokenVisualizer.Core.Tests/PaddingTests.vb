Imports Tokenizers.Internal

Namespace TokenVisualizer.Core.Tests

    ''' <summary>
    ''' Ports the Rust <c>utils/padding.rs</c> unit tests.
    ''' </summary>
    <TestClass>
    Public Class PaddingTests

        Private Shared Function GetEncodings() As List(Of Encoding)
            Return New List(Of Encoding) From {
                Encoding.FromIds({0, 1, 2, 3, 4}),
                Encoding.FromIds({0, 1, 2})}
        End Function

        <TestMethod>
        Public Sub PadToMultiple()
            ' Test fixed.
            Dim encodings As List(Of Encoding) = GetEncodings()
            Dim params As New PaddingParams()
            params.Strategy = PaddingStrategy.Fixed(7)
            params.Direction = PaddingDirection.Right
            params.PadToMultipleOf = 8
            params.PadId = 0
            params.PadTypeId = 0
            params.PadToken = "[PAD]"
            Padding.PadEncodings(encodings, params)
            Assert.IsTrue(encodings.All(Function(e) e.Length = 8), "fixed 7 rounded up to multiple of 8")

            ' Test batch.
            encodings = GetEncodings()
            params.Strategy = PaddingStrategy.BatchLongest
            params.PadToMultipleOf = 6
            Padding.PadEncodings(encodings, params)
            Assert.IsTrue(encodings.All(Function(e) e.Length = 6), "batch longest rounded up to multiple of 6")

            ' Do not crash with 0.
            params.PadToMultipleOf = 0
            Padding.PadEncodings(encodings, params)
        End Sub

        <TestMethod>
        Public Sub PadLeftPrependsIds()
            Dim enc As New Encoding()
            enc.Ids = New List(Of Integer) From {1, 2}
            enc.TypeIds = New List(Of Integer) From {0, 0}
            enc.Tokens = New List(Of String) From {"a", "b"}
            enc.Words = New List(Of Integer?) From {0, 1}
            enc.Offsets = New List(Of (Integer, Integer)) From {(0, 1), (1, 2)}
            enc.SpecialTokensMask = New List(Of Integer) From {0, 0}
            enc.AttentionMask = New List(Of Integer) From {1, 1}

            enc.Pad(4, 99, 0, "[PAD]", PaddingDirection.Left)

            CollectionAssert.AreEqual(New Integer() {99, 99, 1, 2}, enc.Ids)
            CollectionAssert.AreEqual(New Integer() {0, 0, 0, 0}, enc.TypeIds)
            CollectionAssert.AreEqual(New String() {"[PAD]", "[PAD]", "a", "b"}, enc.Tokens)
            CollectionAssert.AreEqual(New Integer() {1, 1, 0, 0}, enc.SpecialTokensMask)
            CollectionAssert.AreEqual(New Integer() {0, 0, 1, 1}, enc.AttentionMask)
            CollectionAssert.AreEqual(New List(Of Integer?) From {Nothing, Nothing, 0, 1}, enc.Words)
            CollectionAssert.AreEqual(New (Integer, Integer)() {(0, 0), (0, 0), (0, 1), (1, 2)}, enc.Offsets)
        End Sub

        <TestMethod>
        Public Sub PadRightAppendsIds()
            Dim enc As Encoding = Encoding.FromIds({1, 2})
            enc.Pad(4, 99, 0, "[PAD]", PaddingDirection.Right)
            CollectionAssert.AreEqual(New Integer() {1, 2, 99, 99}, enc.Ids)
        End Sub

    End Class

End Namespace
