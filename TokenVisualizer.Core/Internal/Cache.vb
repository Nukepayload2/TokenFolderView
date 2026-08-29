Imports System.Collections.Generic

Namespace Internal

    ''' <summary>
    ''' A simple bounded cache with insert-order (FIFO) eviction, used by the Unigram and BPE
    ''' models. The default capacity mirrors the Rust <c>DEFAULT_CACHE_CAPACITY</c> (10 000).
    '''
    ''' NOTE: the current on-disk Rust <c>utils/cache.rs</c> implements a HashMap-based cache that
    ''' silently drops inserts once at capacity. This port follows the task specification instead:
    ''' FIFO eviction of the oldest entry when at capacity.
    ''' </summary>
    Public NotInheritable Class Cache(Of TKey, TValue)

        Private ReadOnly _capacity As Integer
        Private ReadOnly _map As Dictionary(Of TKey, TValue)
        Private ReadOnly _order As Queue(Of TKey)

        Public Sub New(Optional capacity As Integer = 10000)
            If capacity < 0 Then capacity = 0
            _capacity = capacity
            _map = New Dictionary(Of TKey, TValue)()
            _order = New Queue(Of TKey)()
        End Sub

        Public ReadOnly Property Capacity As Integer
            Get
                Return _capacity
            End Get
        End Property

        Public ReadOnly Property Count As Integer
            Get
                Return _map.Count
            End Get
        End Property

        ''' <summary>Returns the cached value for <paramref name="key"/>, or <c>Nothing</c>/default if absent.</summary>
        Public Function GetValue(key As TKey) As TValue
            Dim v As TValue
            If _map.TryGetValue(key, v) Then Return v
            Return Nothing
        End Function

        ''' <summary>Whether the key is present in the cache.</summary>
        Public Function Contains(key As TKey) As Boolean
            Return _map.ContainsKey(key)
        End Function

        ''' <summary>
        ''' Get-or-create: returns the cached value if present, otherwise computes it via
        ''' <paramref name="createFn"/>, inserts it, and returns it.
        ''' </summary>
        Public Function [Get](key As TKey, createFn As Func(Of TKey, TValue)) As TValue
            Dim v As TValue
            If _map.TryGetValue(key, v) Then Return v
            v = createFn(key)
            Insert(key, v)
            Return v
        End Function

        ''' <summary>
        ''' Inserts a value, evicting the oldest entry (insertion order) when at capacity.
        ''' Re-inserting an existing key updates its value without changing its eviction order.
        ''' </summary>
        Public Sub Insert(key As TKey, value As TValue)
            If _capacity = 0 Then Return
            If _map.ContainsKey(key) Then
                _map(key) = value
                Return
            End If
            If _map.Count >= _capacity Then
                Dim oldest As TKey = _order.Dequeue()
                _map.Remove(oldest)
            End If
            _map(key) = value
            _order.Enqueue(key)
        End Sub

        Public Sub Clear()
            _map.Clear()
            _order.Clear()
        End Sub
    End Class

End Namespace
