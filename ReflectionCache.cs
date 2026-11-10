using System;
using System.Collections.Generic;
using System.Reflection;

namespace JapaneseIME
{
    internal static class ReflectionCache
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, FieldInfo> Fields = new Dictionary<string, FieldInfo>();

        internal static FieldInfo GetFieldInfo(Type type, string name)
        {
            if (type == null || string.IsNullOrEmpty(name))
            {
                return null;
            }

            string key = type.FullName + "." + name;
            lock (Sync)
            {
                FieldInfo cached;
                if (Fields.TryGetValue(key, out cached))
                {
                    return cached;
                }
            }

            FieldInfo field = null;
            for (Type current = type; current != null && field == null; current = current.BaseType)
            {
                field = current.GetField(name, BindingFlags.Instance | BindingFlags.Static |
                                               BindingFlags.Public | BindingFlags.NonPublic);
            }

            lock (Sync)
            {
                Fields[key] = field;
            }

            return field;
        }

        internal static T GetField<T>(object instance, string name, T fallback)
        {
            if (instance == null)
            {
                return fallback;
            }

            try
            {
                FieldInfo field = GetFieldInfo(instance.GetType(), name);
                if (field == null || !typeof(T).IsAssignableFrom(field.FieldType))
                {
                    return fallback;
                }

                object value = field.GetValue(instance);
                return value is T ? (T)value : fallback;
            }
            catch
            {
                return fallback;
            }
        }

        internal static bool TrySetField(object instance, string name, object value)
        {
            if (instance == null)
            {
                return false;
            }

            try
            {
                FieldInfo field = GetFieldInfo(instance.GetType(), name);
                if (field == null)
                {
                    return false;
                }

                field.SetValue(instance, value);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
