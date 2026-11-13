// Host-free doubles for the actual drawing-profile scanner. Public member shapes
// were checked against AutoCAD2024 acdbmgd metadata; Profile* members are fixture controls.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Autodesk.AutoCAD.DatabaseServices;

public enum OpenMode { ForRead, ForWrite, ForNotify }

public readonly struct Handle
{
    private readonly long value;
    public Handle(long value) { this.value = value; }
    public override string ToString() => value.ToString("X", CultureInfo.InvariantCulture);
}

internal sealed class ProfileObjectSlot
{
    internal Database Owner;
    internal DBObject Value;
    internal Exception OpenFailure;
    internal bool Erased;
}

public readonly struct ObjectId : IEquatable<ObjectId>
{
    internal readonly ProfileObjectSlot Slot;
    internal ObjectId(ProfileObjectSlot slot) { Slot = slot; }
    public static ObjectId Null => default;
    public bool IsNull => Slot == null;
    public bool IsErased => Slot?.Erased ?? false;
    public bool IsEffectivelyErased => IsErased || Slot?.Value.OwnerId.IsErased == true;
    public Handle Handle => Slot?.Value.Handle ?? default;
    public bool Equals(ObjectId other) => ReferenceEquals(Slot, other.Slot);
    public override bool Equals(object other) => other is ObjectId id && Equals(id);
    public override int GetHashCode() => Slot == null ? 0 : RuntimeHelpers.GetHashCode(Slot);
    public static bool operator ==(ObjectId first, ObjectId second) => first.Equals(second);
    public static bool operator !=(ObjectId first, ObjectId second) => !first.Equals(second);
}

public partial class DBObject
{
    public ObjectId ObjectId { get; internal set; }
    public ObjectId OwnerId { get; set; }
    public Handle Handle { get; internal set; }

    protected void ProfileWrite()
    {
        var database = ObjectId.Slot?.Owner;
        if (database == null) return;
        database.ProfileMutations++;
        database.Raise("modified", this);
    }
}

public partial class Database
{
    private TransactionManager profileTransactions;
    private long profileNextHandle = 1;
    public ObjectId BlockTableId { get; internal set; }
    public ObjectId LayerTableId { get; internal set; }
    public TransactionManager TransactionManager => profileTransactions ??= new TransactionManager(this);
    internal List<ObjectId> ProfileOpenedIds { get; } = new();
    internal List<OpenMode> ProfileOpenModes { get; } = new();
    internal int ProfileTransactionStarts, ProfileTransactionCommits, ProfileTransactionDisposals, ProfileMutations;

    internal ObjectId ProfileAdd(DBObject value, bool erased = false)
    {
        var id = new ObjectId(new ProfileObjectSlot { Owner = this, Value = value, Erased = erased });
        value.ObjectId = id;
        value.Handle = new Handle(profileNextHandle++);
        return id;
    }

    internal void ProfileResetMetrics()
    {
        ProfileOpenedIds.Clear(); ProfileOpenModes.Clear();
        ProfileTransactionStarts = ProfileTransactionCommits = ProfileTransactionDisposals = ProfileMutations = 0;
    }
}

public sealed class TransactionManager
{
    private readonly Database database;
    private int active;
    internal TransactionManager(Database database) { this.database = database; }
    public int NumberOfActiveTransactions => active;
    public Transaction StartTransaction()
    {
        database.ProfileTransactionStarts++; active++;
        return new Transaction(database, () => active--);
    }
}

public sealed class Transaction : IDisposable
{
    private readonly Database database;
    private readonly Action disposed;
    private bool ended;
    internal Transaction(Database database, Action disposed) { this.database = database; this.disposed = disposed; }
    public DBObject GetObject(ObjectId id, OpenMode mode)
    {
        if (ended) throw new ObjectDisposedException(nameof(Transaction));
        database.ProfileOpenedIds.Add(id); database.ProfileOpenModes.Add(mode);
        if (mode != OpenMode.ForRead) throw new InvalidOperationException("Profile must open every object ForRead.");
        if (id.IsNull || !ReferenceEquals(id.Slot.Owner, database)) throw new InvalidOperationException("Invalid fixture ObjectId.");
        if (id.IsErased) throw new InvalidOperationException("Erased object must not be opened.");
        if (id.Slot.OpenFailure != null) throw id.Slot.OpenFailure;
        return id.Slot.Value;
    }
    public void Commit() { if (ended) throw new ObjectDisposedException(nameof(Transaction)); database.ProfileTransactionCommits++; }
    public void Abort() => throw new NotSupportedException("Read-only fixture.");
    public void AddNewlyCreatedDBObject(DBObject value, bool add) => throw new NotSupportedException("Read-only fixture.");
    public void Dispose()
    {
        if (ended) return;
        ended = true; database.ProfileTransactionDisposals++; disposed();
    }
}

public class SymbolTableRecord : DBObject
{
    private string name;
    public string Name
    {
        get => name;
        set { name = value; ProfileWrite(); }
    }
}

public class SymbolTable : DBObject, IEnumerable
{
    internal List<ObjectId> ProfileRecords { get; } = new();
    internal Dictionary<string, ObjectId> ProfileNames { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal int? ProfileEnumerationFaultAfter;
    internal int ProfileEnumerationStarts;
    public ObjectId this[string name] => ProfileNames[name];
    public SymbolTableEnumerator GetEnumerator()
    {
        ProfileEnumerationStarts++;
        return new SymbolTableEnumerator(ProfileRecords, ProfileEnumerationFaultAfter);
    }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public class BlockTable : SymbolTable { }

public class LayerTable : SymbolTable
{
    public bool Has(string name) => ProfileNames.ContainsKey(name);
    public void UpgradeOpen() => throw new NotSupportedException("Read-only fixture.");
    public ObjectId Add(LayerTableRecord value) => throw new NotSupportedException("Read-only fixture.");
}

public class LayerTableRecord : SymbolTableRecord
{
    public Autodesk.AutoCAD.Colors.Color Color { get; set; } = new Autodesk.AutoCAD.Colors.Color(7);
    public bool IsLocked { get; set; }
    public bool IsFrozen { get; set; }
    public bool IsOff { get; set; }
}

public sealed class SymbolTableEnumerator : IEnumerator, IDisposable
{
    private readonly ProfileIdEnumerator inner;
    internal SymbolTableEnumerator(IReadOnlyList<ObjectId> ids, int? faultAfter)
        { inner = new ProfileIdEnumerator(ids, faultAfter, null); }
    public ObjectId Current => inner.Current;
    object IEnumerator.Current => Current;
    public bool MoveNext() => inner.MoveNext();
    public void Reset() => inner.Reset();
    public void Dispose() { }
}

public class BlockTableRecord : SymbolTableRecord, IEnumerable
{
    public const string ModelSpace = "*Model_Space";
    internal List<ObjectId> ProfileEntities { get; } = new();
    internal List<ObjectId> ProfileReferenceIds { get; } = new();
    internal List<ObjectId> ProfileAnonymousIds { get; } = new();
    internal Exception ProfileReferenceFailure;
    internal int? ProfileEnumerationFaultAfter;
    internal Action<int> ProfileOnMoveNext;
    internal int ProfileEnumerationStarts;
    internal Exception ProfileXrefFailure;
    private bool isLayout, isFromExternalReference, isFromOverlayReference;
    public bool IsLayout
    {
        get => isLayout;
        set { isLayout = value; ProfileWrite(); }
    }
    public ObjectId LayoutId { get; set; }
    public bool IsAnonymous { get; set; }
    public bool IsDependent { get; set; }
    public bool IsDynamicBlock { get; set; }
    public ObjectIdCollection GetAnonymousBlockIds() => new ObjectIdCollection(ProfileAnonymousIds);
    public ObjectIdCollection GetBlockReferenceIds(bool directOnly, bool forceValidity)
    {
        if (!directOnly) throw new InvalidOperationException("The fixture provides only direct reference IDs.");
        if (ProfileReferenceFailure != null) throw ProfileReferenceFailure;
        return new ObjectIdCollection(ProfileReferenceIds);
    }
    public bool IsFromExternalReference
    {
        get { if (ProfileXrefFailure != null) throw ProfileXrefFailure; return isFromExternalReference; }
        set { isFromExternalReference = value; ProfileWrite(); }
    }
    public bool IsFromOverlayReference
    {
        get { if (ProfileXrefFailure != null) throw ProfileXrefFailure; return isFromOverlayReference; }
        set { isFromOverlayReference = value; ProfileWrite(); }
    }
    public BlockTableRecordEnumerator GetEnumerator()
    {
        ProfileEnumerationStarts++;
        return new BlockTableRecordEnumerator(ProfileEntities, ProfileEnumerationFaultAfter, ProfileOnMoveNext);
    }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public sealed class ObjectIdCollection : List<ObjectId>, IDisposable
{
    internal ObjectIdCollection(IEnumerable<ObjectId> ids) : base(ids) { }
    public void Dispose() { }
}

public sealed class BlockTableRecordEnumerator : IEnumerator, IDisposable
{
    private readonly ProfileIdEnumerator inner;
    internal BlockTableRecordEnumerator(IReadOnlyList<ObjectId> ids, int? faultAfter, Action<int> onMoveNext)
        { inner = new ProfileIdEnumerator(ids, faultAfter, onMoveNext); }
    public ObjectId Current => inner.Current;
    object IEnumerator.Current => Current;
    public bool MoveNext() => inner.MoveNext();
    public void Reset() => inner.Reset();
    public void Dispose() { }
}

internal sealed class ProfileIdEnumerator
{
    private readonly IReadOnlyList<ObjectId> ids;
    private readonly int? faultAfter;
    private readonly Action<int> onMoveNext;
    private int index = -1;
    internal ProfileIdEnumerator(IReadOnlyList<ObjectId> ids, int? faultAfter, Action<int> onMoveNext)
        { this.ids = ids; this.faultAfter = faultAfter; this.onMoveNext = onMoveNext; }
    internal ObjectId Current => ids[index];
    internal bool MoveNext()
    {
        var next = index + 1;
        onMoveNext?.Invoke(next);
        if (faultAfter.HasValue && next >= faultAfter.Value) throw new InvalidOperationException("Simulated enumeration failure.");
        index = next; return index < ids.Count;
    }
    internal void Reset() { index = -1; }
}

public class Layout : DBObject
{
    private string layoutName;
    internal Exception ProfileNameFailure;
    public string LayoutName
    {
        get { if (ProfileNameFailure != null) throw ProfileNameFailure; return layoutName; }
        set { layoutName = value; ProfileWrite(); }
    }
}

public partial class Entity : DBObject
{
    private string layer;
    internal Exception ProfileLayerFailure;
    public string Layer
    {
        get { if (ProfileLayerFailure != null) throw ProfileLayerFailure; return layer; }
        set { layer = value; ProfileWrite(); }
    }
}

public partial class Line : Curve { }
public partial class Circle : Curve { }
public partial class DBText : Entity { }
public class ProxyEntity : Entity { }
public partial class BlockReference : Entity
{
    public ObjectId BlockTableRecord { get; set; }
}
