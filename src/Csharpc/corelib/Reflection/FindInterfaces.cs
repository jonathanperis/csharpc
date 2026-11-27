// Type.FindInterfaces over csharpc's Type.GetInterfaces (runtime/type.c): RuntimeType's filter loop.
#nullable disable
using System;
using System.Collections.Generic;
using System.Reflection;

namespace CsharpcCoreLib
{
    internal static unsafe partial class ReflectionRuntime
    {
        public static Type[] FindInterfaces(Type type, TypeFilter filter, object filterCriteria)
        {
            ArgumentNullException.ThrowIfNull(filter);
            var found = new List<Type>();
            foreach (var itf in type.GetInterfaces())
                if (filter(itf, filterCriteria)) found.Add(itf);
            return found.ToArray();
        }
    }
}
