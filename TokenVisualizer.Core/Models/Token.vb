Namespace Models

    ''' <summary>
    ''' A single output token produced by a model. Mirrors the Rust <c>Token</c> struct:
    ''' <c>Id</c> is the vocabulary id, <c>Value</c> is the reconstructed token string
    ''' (from the reverse vocabulary), and <c>Offsets</c> is the UTF-8 byte range of the
    ''' token within the input word.
    ''' </summary>
    Public Structure Token
        ''' <summary>Vocabulary id of the token.</summary>
        Public Id As Integer
        ''' <summary>The token string (from the reverse vocabulary).</summary>
        Public Value As String
        ''' <summary>Byte offsets (start, end) within the input word.</summary>
        Public Offsets As (Integer, Integer)

        Public Sub New(id As Integer, value As String, offsets As (Integer, Integer))
            Me.Id = id
            Me.Value = value
            Me.Offsets = offsets
        End Sub

        Public Overrides Function Equals(obj As Object) As Boolean
            If TypeOf obj Is Token Then
                Dim other As Token = DirectCast(obj, Token)
                Return Id = other.Id AndAlso Value = other.Value AndAlso Offsets.Equals(other.Offsets)
            End If
            Return False
        End Function

        Public Overrides Function GetHashCode() As Integer
            Dim valueHash As Integer = If(Value Is Nothing, 0, Value.GetHashCode())
            Return Id.GetHashCode() Xor valueHash Xor Offsets.GetHashCode()
        End Function

        Public Overrides Function ToString() As String
            Return $"Token(id={Id}, value='{Value}', offsets={Offsets})"
        End Function
    End Structure

End Namespace
