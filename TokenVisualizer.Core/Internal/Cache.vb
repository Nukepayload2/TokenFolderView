Imports System.Collections.Generic

Namespace Internal

    ''' <summary>
    ''' A snapshot of a cache's hit / miss / skip / eviction counters. The counters are only
    ''' meaningful when the cache has stats enabled (see <see cref="Cache(Of TKey, TValue).EnableStats"/>);
    ''' when disabled they remain zero so the GetValue/Insert hot paths carry no measurable
    ''' overhead.
    ''' </summary>
    Public Structure CacheStats
        Public Hits As Integer
        Public Misses As Integer
        Public Skips As Integer
        Public Evictions As Integer
    End Structure

    ''' <summary>
    ''' A simple bounded cache with insert-order (FIFO) eviction, used by the Unigram and BPE
    ''' models. The default capacity mirrors the Rust <c>DEFAULT_CACHE_CAPACITY</c> (10 000).
    '''
    ''' NOTE: the current on-disk Rust <c>utils/cache.rs</c> implements a HashMap-based cache that
    ''' silently drops inserts once at capacity. This port follows the task specification instead:
    ''' FIFO eviction of the oldest entry when at capacity.
    '''
    ''' The internal <see cref="Dictionary(Of TKey, TValue)"/> and <see cref="Queue(Of TKey)"/> are
    ''' pre-allocated to the requested capacity (legal for 0) so a bounded cache grows in one shot
    ''' instead of re-hashing from zero as it fills.
    ''' </summary>
    Public NotInheritable Class Cache(Of TKey, TValue)

        Private ReadOnly _capacity As Integer
        Private ReadOnly _map As Dictionary(Of TKey, TValue)
        Private ReadOnly _order As Queue(Of TKey)
        Private _statsEnabled As Boolean
        Private _hits As Integer
        Private _misses As Integer
        Private _skips As Integer
        Private _evictions As Integer

        Public Sub New(Optional capacity As Integer = 10000, Optional enableStats As Boolean = False,
                       Optional comparer As IEqualityComparer(Of TKey) = Nothing)
            If capacity < 0 Then capacity = 0
            _capacity = capacity
            If comparer Is Nothing Then
                _map = New Dictionary(Of TKey, TValue)(capacity)
            Else
                _map = New Dictionary(Of TKey, TValue)(capacity, comparer)
            End If
            _order = New Queue(Of TKey)(capacity)
            _statsEnabled = enableStats
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

        ''' <summary>Whether the cache is counting hits/misses/skips/evictions. Off by default.</summary>
        Public ReadOnly Property StatsEnabled As Boolean
            Get
                Return _statsEnabled
            End Get
        End Property

        ''' <summary>Turns on the statistics counters. Never changes cache behavior.</summary>
        Public Sub EnableStats()
            _statsEnabled = True
        End Sub

        ''' <summary>Zeros all statistics counters.</summary>
        Public Sub ResetStats()
            _hits = 0
            _misses = 0
            _skips = 0
            _evictions = 0
        End Sub

        ''' <summary>Returns a snapshot of the statistics counters.</summary>
        Public Function GetStats() As CacheStats
            Dim s As New CacheStats()
            s.Hits = _hits
            s.Misses = _misses
            s.Skips = _skips
            s.Evictions = _evictions
            Return s
        End Function

        ''' <summary>Returns the cached value for <paramref name="key"/>, or <c>Nothing</c>/default if absent.</summary>
        Public Function GetValue(key As TKey) As TValue
            Dim v As TValue
            If _map.TryGetValue(key, v) Then
                If _statsEnabled Then _hits += 1
                Return v
            End If
            If _statsEnabled Then _misses += 1
            Return Nothing
        End Function

        ''' <summary>
        ''' M9: alternate-lookup twin of <see cref="GetValue"/>, keyed by a
        ''' <see cref="ReadOnlyMemory(Of Char)"/> over a pooled mapping buffer instead of a materialized
        ''' <see cref="String"/>. Only valid when <c>TKey = String</c> AND the dictionary was built
        ''' with <see cref="StringMemoryAlternateComparer"/> (the BPE word cache). A cache hit returns
        ''' the value without allocating the key String — the M9 target (hit ~96%, so the mapped-string
        ''' <c>New String</c> is eliminated). <see cref="ReadOnlyMemory(Of Char)"/> (not
        ''' <see cref="ReadOnlySpan(Of Char)"/>) is the key type because the VB compiler does not
        ''' support ByRef-like types in method signatures (P-011 / BinaryDetector precedent).
        ''' </summary>
        Public Function GetValueMemory(key As ReadOnlyMemory(Of Char)) As TValue
            Dim v As TValue
            Dim lookup = _map.GetAlternateLookup(Of ReadOnlyMemory(Of Char))()
            If lookup.TryGetValue(key, v) Then
                If _statsEnabled Then _hits += 1
                Return v
            End If
            If _statsEnabled Then _misses += 1
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
            If _map.TryGetValue(key, v) Then
                If _statsEnabled Then _hits += 1
                Return v
            End If
            If _statsEnabled Then _misses += 1
            v = createFn(key)
            Insert(key, v)
            Return v
        End Function

        ''' <summary>
        ''' Records that the cache was intentionally bypassed for a key (e.g. an over-long word
        ''' under a max-word-length policy). Counts a "skip" when stats are enabled.
        ''' </summary>
        Public Sub RecordSkip()
            If _statsEnabled Then _skips += 1
        End Sub

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
                If _statsEnabled Then _evictions += 1
            End If
            _map(key) = value
            _order.Enqueue(key)
        End Sub

        Public Sub Clear()
            _map.Clear()
            _order.Clear()
        End Sub
    End Class

    ''' <summary>
    ''' M9: dictionary comparer for the BPE word cache that ALSO supports alternate lookup by a
    ''' <see cref="ReadOnlyMemory(Of Char)"/> over a pooled mapping buffer (instead of a materialized
    ''' <see cref="String"/>), so a cache hit never allocates the key. Equivalence and hashing are
    ''' ordinal and mutually consistent between the String and Memory forms (both delegate to the
    ''' runtime's string hashing over the same chars), so an entry inserted by one form is found by
    ''' the other. <see cref="ReadOnlyMemory(Of Char)"/> (not <see cref="ReadOnlySpan(Of Char)"/>) is
    ''' the alternate type because the VB compiler does not support ByRef-like types in method
    ''' signatures (P-011 / BinaryDetector precedent); the span is used only as an inferred local
    ''' inside the equivalence/hash helpers.
    ''' </summary>
    Friend NotInheritable Class StringMemoryAlternateComparer
        Implements IEqualityComparer(Of String)
        Implements IAlternateEqualityComparer(Of ReadOnlyMemory(Of Char), String)

        Public Function EqualsString(x As String, y As String) As Boolean Implements IEqualityComparer(Of String).Equals
            Return String.Equals(x, y)
        End Function

        Public Function GetHashCodeString(obj As String) As Integer Implements IEqualityComparer(Of String).GetHashCode
            Return obj.GetHashCode()
        End Function

        Public Function EqualsMemory(alternate As ReadOnlyMemory(Of Char), key As String) As Boolean Implements IAlternateEqualityComparer(Of ReadOnlyMemory(Of Char), String).Equals
            Return key.AsSpan().SequenceEqual(alternate.Span)
        End Function

        Public Function GetHashCodeMemory(alternate As ReadOnlyMemory(Of Char)) As Integer Implements IAlternateEqualityComparer(Of ReadOnlyMemory(Of Char), String).GetHashCode
            Return String.GetHashCode(alternate.Span)
        End Function

        Public Function CreateMemory(alternate As ReadOnlyMemory(Of Char)) As String Implements IAlternateEqualityComparer(Of ReadOnlyMemory(Of Char), String).Create
            Return alternate.ToString()
        End Function
    End Class

End Namespace
