// System.Tuple<...> reference tuples and Tuple/ValueTuple.Create factories.
#nullable disable
using System;
using System.Collections.Generic;
using System.Text;

namespace CsharpcCoreLib
{
    [Shadows("System.Tuple`1")]
    public class TupleImpl1<T1>
    {
        public T1 Item1 { get; }
        public TupleImpl1(T1 item1) { Item1 = item1; }
        public override string ToString() => "(" + (Item1 == null ? "" : Item1.ToString()) + ")";
        public override bool Equals(object obj) => obj is TupleImpl1<T1> t && EqualityComparer<T1>.Default.Equals(Item1, t.Item1);
        public override int GetHashCode() { int h = 17; h = h * 31 + (Item1 == null ? 0 : Item1.GetHashCode()); return h; }
    }

    [Shadows("System.Tuple`2")]
    public class TupleImpl2<T1, T2>
    {
        public T1 Item1 { get; }
        public T2 Item2 { get; }
        public TupleImpl2(T1 item1, T2 item2) { Item1 = item1; Item2 = item2; }
        public override string ToString() => "(" + (Item1 == null ? "" : Item1.ToString()) + ", " + (Item2 == null ? "" : Item2.ToString()) + ")";
        public override bool Equals(object obj) => obj is TupleImpl2<T1, T2> t && EqualityComparer<T1>.Default.Equals(Item1, t.Item1) && EqualityComparer<T2>.Default.Equals(Item2, t.Item2);
        public override int GetHashCode() { int h = 17; h = h * 31 + (Item1 == null ? 0 : Item1.GetHashCode()); h = h * 31 + (Item2 == null ? 0 : Item2.GetHashCode()); return h; }
    }

    [Shadows("System.Tuple`3")]
    public class TupleImpl3<T1, T2, T3>
    {
        public T1 Item1 { get; }
        public T2 Item2 { get; }
        public T3 Item3 { get; }
        public TupleImpl3(T1 item1, T2 item2, T3 item3) { Item1 = item1; Item2 = item2; Item3 = item3; }
        public override string ToString() => "(" + (Item1 == null ? "" : Item1.ToString()) + ", " + (Item2 == null ? "" : Item2.ToString()) + ", " + (Item3 == null ? "" : Item3.ToString()) + ")";
        public override bool Equals(object obj) => obj is TupleImpl3<T1, T2, T3> t && EqualityComparer<T1>.Default.Equals(Item1, t.Item1) && EqualityComparer<T2>.Default.Equals(Item2, t.Item2) && EqualityComparer<T3>.Default.Equals(Item3, t.Item3);
        public override int GetHashCode() { int h = 17; h = h * 31 + (Item1 == null ? 0 : Item1.GetHashCode()); h = h * 31 + (Item2 == null ? 0 : Item2.GetHashCode()); h = h * 31 + (Item3 == null ? 0 : Item3.GetHashCode()); return h; }
    }

    [Shadows("System.Tuple`4")]
    public class TupleImpl4<T1, T2, T3, T4>
    {
        public T1 Item1 { get; }
        public T2 Item2 { get; }
        public T3 Item3 { get; }
        public T4 Item4 { get; }
        public TupleImpl4(T1 item1, T2 item2, T3 item3, T4 item4) { Item1 = item1; Item2 = item2; Item3 = item3; Item4 = item4; }
        public override string ToString() => "(" + (Item1 == null ? "" : Item1.ToString()) + ", " + (Item2 == null ? "" : Item2.ToString()) + ", " + (Item3 == null ? "" : Item3.ToString()) + ", " + (Item4 == null ? "" : Item4.ToString()) + ")";
        public override bool Equals(object obj) => obj is TupleImpl4<T1, T2, T3, T4> t && EqualityComparer<T1>.Default.Equals(Item1, t.Item1) && EqualityComparer<T2>.Default.Equals(Item2, t.Item2) && EqualityComparer<T3>.Default.Equals(Item3, t.Item3) && EqualityComparer<T4>.Default.Equals(Item4, t.Item4);
        public override int GetHashCode() { int h = 17; h = h * 31 + (Item1 == null ? 0 : Item1.GetHashCode()); h = h * 31 + (Item2 == null ? 0 : Item2.GetHashCode()); h = h * 31 + (Item3 == null ? 0 : Item3.GetHashCode()); h = h * 31 + (Item4 == null ? 0 : Item4.GetHashCode()); return h; }
    }

    [Shadows("System.Tuple`5")]
    public class TupleImpl5<T1, T2, T3, T4, T5>
    {
        public T1 Item1 { get; }
        public T2 Item2 { get; }
        public T3 Item3 { get; }
        public T4 Item4 { get; }
        public T5 Item5 { get; }
        public TupleImpl5(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5) { Item1 = item1; Item2 = item2; Item3 = item3; Item4 = item4; Item5 = item5; }
        public override string ToString() => "(" + (Item1 == null ? "" : Item1.ToString()) + ", " + (Item2 == null ? "" : Item2.ToString()) + ", " + (Item3 == null ? "" : Item3.ToString()) + ", " + (Item4 == null ? "" : Item4.ToString()) + ", " + (Item5 == null ? "" : Item5.ToString()) + ")";
        public override bool Equals(object obj) => obj is TupleImpl5<T1, T2, T3, T4, T5> t && EqualityComparer<T1>.Default.Equals(Item1, t.Item1) && EqualityComparer<T2>.Default.Equals(Item2, t.Item2) && EqualityComparer<T3>.Default.Equals(Item3, t.Item3) && EqualityComparer<T4>.Default.Equals(Item4, t.Item4) && EqualityComparer<T5>.Default.Equals(Item5, t.Item5);
        public override int GetHashCode() { int h = 17; h = h * 31 + (Item1 == null ? 0 : Item1.GetHashCode()); h = h * 31 + (Item2 == null ? 0 : Item2.GetHashCode()); h = h * 31 + (Item3 == null ? 0 : Item3.GetHashCode()); h = h * 31 + (Item4 == null ? 0 : Item4.GetHashCode()); h = h * 31 + (Item5 == null ? 0 : Item5.GetHashCode()); return h; }
    }

    [Shadows("System.Tuple`6")]
    public class TupleImpl6<T1, T2, T3, T4, T5, T6>
    {
        public T1 Item1 { get; }
        public T2 Item2 { get; }
        public T3 Item3 { get; }
        public T4 Item4 { get; }
        public T5 Item5 { get; }
        public T6 Item6 { get; }
        public TupleImpl6(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5, T6 item6) { Item1 = item1; Item2 = item2; Item3 = item3; Item4 = item4; Item5 = item5; Item6 = item6; }
        public override string ToString() => "(" + (Item1 == null ? "" : Item1.ToString()) + ", " + (Item2 == null ? "" : Item2.ToString()) + ", " + (Item3 == null ? "" : Item3.ToString()) + ", " + (Item4 == null ? "" : Item4.ToString()) + ", " + (Item5 == null ? "" : Item5.ToString()) + ", " + (Item6 == null ? "" : Item6.ToString()) + ")";
        public override bool Equals(object obj) => obj is TupleImpl6<T1, T2, T3, T4, T5, T6> t && EqualityComparer<T1>.Default.Equals(Item1, t.Item1) && EqualityComparer<T2>.Default.Equals(Item2, t.Item2) && EqualityComparer<T3>.Default.Equals(Item3, t.Item3) && EqualityComparer<T4>.Default.Equals(Item4, t.Item4) && EqualityComparer<T5>.Default.Equals(Item5, t.Item5) && EqualityComparer<T6>.Default.Equals(Item6, t.Item6);
        public override int GetHashCode() { int h = 17; h = h * 31 + (Item1 == null ? 0 : Item1.GetHashCode()); h = h * 31 + (Item2 == null ? 0 : Item2.GetHashCode()); h = h * 31 + (Item3 == null ? 0 : Item3.GetHashCode()); h = h * 31 + (Item4 == null ? 0 : Item4.GetHashCode()); h = h * 31 + (Item5 == null ? 0 : Item5.GetHashCode()); h = h * 31 + (Item6 == null ? 0 : Item6.GetHashCode()); return h; }
    }

    [Shadows("System.Tuple`7")]
    public class TupleImpl7<T1, T2, T3, T4, T5, T6, T7>
    {
        public T1 Item1 { get; }
        public T2 Item2 { get; }
        public T3 Item3 { get; }
        public T4 Item4 { get; }
        public T5 Item5 { get; }
        public T6 Item6 { get; }
        public T7 Item7 { get; }
        public TupleImpl7(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5, T6 item6, T7 item7) { Item1 = item1; Item2 = item2; Item3 = item3; Item4 = item4; Item5 = item5; Item6 = item6; Item7 = item7; }
        public override string ToString() => "(" + (Item1 == null ? "" : Item1.ToString()) + ", " + (Item2 == null ? "" : Item2.ToString()) + ", " + (Item3 == null ? "" : Item3.ToString()) + ", " + (Item4 == null ? "" : Item4.ToString()) + ", " + (Item5 == null ? "" : Item5.ToString()) + ", " + (Item6 == null ? "" : Item6.ToString()) + ", " + (Item7 == null ? "" : Item7.ToString()) + ")";
        public override bool Equals(object obj) => obj is TupleImpl7<T1, T2, T3, T4, T5, T6, T7> t && EqualityComparer<T1>.Default.Equals(Item1, t.Item1) && EqualityComparer<T2>.Default.Equals(Item2, t.Item2) && EqualityComparer<T3>.Default.Equals(Item3, t.Item3) && EqualityComparer<T4>.Default.Equals(Item4, t.Item4) && EqualityComparer<T5>.Default.Equals(Item5, t.Item5) && EqualityComparer<T6>.Default.Equals(Item6, t.Item6) && EqualityComparer<T7>.Default.Equals(Item7, t.Item7);
        public override int GetHashCode() { int h = 17; h = h * 31 + (Item1 == null ? 0 : Item1.GetHashCode()); h = h * 31 + (Item2 == null ? 0 : Item2.GetHashCode()); h = h * 31 + (Item3 == null ? 0 : Item3.GetHashCode()); h = h * 31 + (Item4 == null ? 0 : Item4.GetHashCode()); h = h * 31 + (Item5 == null ? 0 : Item5.GetHashCode()); h = h * 31 + (Item6 == null ? 0 : Item6.GetHashCode()); h = h * 31 + (Item7 == null ? 0 : Item7.GetHashCode()); return h; }
    }

    [Shadows("System.Tuple")]
    public static class TupleFactory
    {
        public static TupleImpl1<T1> Create<T1>(T1 item1) => new TupleImpl1<T1>(item1);
        public static TupleImpl2<T1, T2> Create<T1, T2>(T1 item1, T2 item2) => new TupleImpl2<T1, T2>(item1, item2);
        public static TupleImpl3<T1, T2, T3> Create<T1, T2, T3>(T1 item1, T2 item2, T3 item3) => new TupleImpl3<T1, T2, T3>(item1, item2, item3);
        public static TupleImpl4<T1, T2, T3, T4> Create<T1, T2, T3, T4>(T1 item1, T2 item2, T3 item3, T4 item4) => new TupleImpl4<T1, T2, T3, T4>(item1, item2, item3, item4);
        public static TupleImpl5<T1, T2, T3, T4, T5> Create<T1, T2, T3, T4, T5>(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5) => new TupleImpl5<T1, T2, T3, T4, T5>(item1, item2, item3, item4, item5);
        public static TupleImpl6<T1, T2, T3, T4, T5, T6> Create<T1, T2, T3, T4, T5, T6>(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5, T6 item6) => new TupleImpl6<T1, T2, T3, T4, T5, T6>(item1, item2, item3, item4, item5, item6);
        public static TupleImpl7<T1, T2, T3, T4, T5, T6, T7> Create<T1, T2, T3, T4, T5, T6, T7>(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5, T6 item6, T7 item7) => new TupleImpl7<T1, T2, T3, T4, T5, T6, T7>(item1, item2, item3, item4, item5, item6, item7);
    }

    [ShadowMembers("System.ValueTuple")]
    public static class ValueTupleFactory
    {
        public static (T1, T2) Create<T1, T2>(T1 item1, T2 item2) => (item1, item2);
        public static (T1, T2, T3) Create<T1, T2, T3>(T1 item1, T2 item2, T3 item3) => (item1, item2, item3);
        public static (T1, T2, T3, T4) Create<T1, T2, T3, T4>(T1 item1, T2 item2, T3 item3, T4 item4) => (item1, item2, item3, item4);
        public static (T1, T2, T3, T4, T5) Create<T1, T2, T3, T4, T5>(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5) => (item1, item2, item3, item4, item5);
        public static (T1, T2, T3, T4, T5, T6) Create<T1, T2, T3, T4, T5, T6>(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5, T6 item6) => (item1, item2, item3, item4, item5, item6);
        public static (T1, T2, T3, T4, T5, T6, T7) Create<T1, T2, T3, T4, T5, T6, T7>(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5, T6 item6, T7 item7) => (item1, item2, item3, item4, item5, item6, item7);
    }
}
