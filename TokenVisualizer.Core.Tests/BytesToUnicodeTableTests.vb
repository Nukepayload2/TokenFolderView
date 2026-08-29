Imports Tokenizers.Internal

Namespace TokenVisualizer.Core.Tests

    <TestClass>
    Public Class BytesToUnicodeTableTests

        <TestMethod>
        Public Sub SpotChecks()
            Dim table = BytesToUnicodeTable.GetBytesToChar()

            Assert.AreEqual("Ā"c, table(CByte(&H00)))
            Assert.AreEqual("Ċ"c, table(CByte(&H0A)))
            Assert.AreEqual("Ġ"c, table(CByte(&H20)))
            Assert.AreEqual("Ń"c, table(CByte(&HAD))) ' U+0143
        End Sub

        <TestMethod>
        Public Sub PrintableAsciiIsIdentity()
            Dim table = BytesToUnicodeTable.GetBytesToChar()
            For b As Integer = &H21 To &H7E
                Assert.AreEqual(ChrW(b), table(CByte(b)), $"byte 0x{b:X2}")
            Next
        End Sub

        <TestMethod>
        Public Sub InverseRoundTrip_AllBytes()
            Dim table = BytesToUnicodeTable.GetBytesToChar()
            Dim inverse = BytesToUnicodeTable.GetCharToBytes()
            For b As Integer = 0 To 255
                Dim c As Char = table(CByte(b))
                Assert.AreEqual(CByte(b), inverse(c), $"byte 0x{b:X2} -> char U+{AscW(c):X4}")
            Next
        End Sub

        <TestMethod>
        Public Sub AlphabetHas256Entries()
            Assert.HasCount(256, BytesToUnicodeTable.GetBytesToChar())
            Assert.HasCount(256, BytesToUnicodeTable.GetCharToBytes())
        End Sub

    End Class

End Namespace
