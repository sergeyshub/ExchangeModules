using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;

namespace HelpersLib
{
    public static class DictionaryConverter
    {
        public static IDictionary<string, object> ToDictionary(this object value)
        {
            IDictionary<string, object> dictionary = new Dictionary<string, object>();

            foreach (PropertyDescriptor property in TypeDescriptor.GetProperties(value.GetType()))
                dictionary.Add(property.Name, property.GetValue(value));

            return dictionary;

            /* Another way to do this:
            
            using Newtonsoft.Json;

            string str = JsonConvert.SerializeObject(userResult);
            dynamic obj = JsonConvert.DeserializeObject(str);
            */
        }

        public static T ToObject<T>(this IDictionary<string, object> source)
            where T : class, new()
        {
            T someObject = new T();
            Type someObjectType = someObject.GetType();

            foreach (KeyValuePair<string, object> item in source)
                someObjectType.GetProperty(item.Key).SetValue(someObject, item.Value, null);

            return someObject;
        }

        public static IDictionary<string, object> Concatenate(IDictionary<string, object>[] dictionaries)
        {
            return dictionaries.SelectMany(dict => dict)
                .ToLookup(pair => pair.Key, pair => pair.Value)
                .ToDictionary(group => group.Key, group => group.Last());
        }

        public static Dictionary<string, string> Concatenate(Dictionary<string, string> dict1, Dictionary<string, string> dict2)
        {
            var dictionaries = new Dictionary<string, string>[] { dict1, dict2 };

            return dictionaries.SelectMany(dict => dict)
                .ToLookup(pair => pair.Key, pair => pair.Value)
                .ToDictionary(group => group.Key, group => group.Last());
        }
    }
}
