﻿using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ChoETL
{
    public static class ChoJSONExtensions
    {
        static ChoJSONExtensions()
        {
        }

        private static string GetTypeConverterName(Type type)
        {
            if (type == null) return String.Empty;

            type = type.GetUnderlyingType();
            if (typeof(Array).IsAssignableFrom(type))
                return $"{type.GetItemType().GetUnderlyingType().Name}ArrayConverter";
            else if (typeof(IList).IsAssignableFrom(type))
                return $"{type.GetItemType().GetUnderlyingType().Name}ListConverter";
            else
                return $"{type.Name}Converter";
        }

        public static JToken SerializeToJToken(this JsonSerializer serializer, object value)
        {
            Type vt = value != null ? value.GetType() : typeof(object);
            var convName = GetTypeConverterName(vt);
            var conv = serializer.Converters.Where(c => c.GetType().Name == convName || (c.GetType().IsGenericType && c.GetType().GetGenericArguments()[0] == vt)).FirstOrDefault();
            if (conv == null && ChoJSONConvertersCache.IsInitialized)
            {
                if (ChoJSONConvertersCache.Contains(convName))
                    conv = ChoJSONConvertersCache.Get(convName);
            }

            JToken t = null;
            if (conv == null)
            {
                t = JToken.FromObject(value, serializer);
            }
            else
            {
                t = JToken.Parse(JsonConvert.SerializeObject(value, serializer.Formatting, conv));
            }
            return t;
        }

        public static object DeserializeObject(this JsonSerializer serializer, JsonReader reader, Type objType)
        {
            var convName = GetTypeConverterName(objType);
            var conv = serializer.Converters.Where(c => c.GetType().Name == convName || (c.GetType().IsGenericType && c.GetType().GetGenericArguments()[0] == objType)).FirstOrDefault();
            if (conv == null && ChoJSONConvertersCache.IsInitialized)
            {
                if (ChoJSONConvertersCache.Contains(convName))
                    conv = ChoJSONConvertersCache.Get(convName);
            }

            JToken t = null;
            if (conv == null)
            {
                return serializer.Deserialize(reader, objType);
            }
            else
            {
                return JsonConvert.DeserializeObject(JObject.ReadFrom(reader).ToString(), objType, conv);
            }
        }

        public static string DumpAsJson(this DataTable table, Formatting formatting = Formatting.Indented)
        {
            if (table == null)
                return String.Empty;

            return JsonConvert.SerializeObject(table, formatting);
        }

        public static object GetNameAt(this JObject @this, int index)
        {
            if (@this == null || index < 0)
                return null;

            return @this.Properties().Skip(index).Select(p => p.Name).FirstOrDefault();
        }

        public static object GetValueAt(this JObject @this, int index)
        {
            if (@this == null || index < 0)
                return null;

            return @this.Properties().Skip(index).Select(p => p.Value).FirstOrDefault();
        }

        public static object ToJSONObject(this IDictionary<string, object> dict, Type type)
        {
            object target = ChoActivator.CreateInstance(type);
            string key = null;
            foreach (var p in ChoType.GetProperties(type))
            {
                if (p.GetCustomAttribute<JsonIgnoreAttribute>() != null)
                    continue;

                key = p.Name;
                var attr = p.GetCustomAttribute<JsonPropertyAttribute>();
                if (attr != null && !attr.PropertyName.IsNullOrWhiteSpace())
                    key = attr.PropertyName.NTrim();

                if (!dict.ContainsKey(key))
                    continue;

                p.SetValue(target, dict[key].CastObjectTo(p.PropertyType));
            }

            return target;
        }

        public static T ToJSONObject<T>(this IDictionary<string, object> dict)
            where T : class, new()
        {
            return (T)ToJSONObject(dict, typeof(T));
        }
    }
}
