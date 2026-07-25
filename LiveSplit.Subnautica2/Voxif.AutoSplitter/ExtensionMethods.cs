using LiveSplit.Subnautica2;
using LiveSplit.Subnautica2.Enums;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Text;
using System.Xml;

namespace Voxif.AutoSplitter {
    public static class ExtensionMethods {
        //
        // XML
        //
        public static XmlElement ToElement<T>(this XmlDocument document, string name, T value) {
            XmlElement xmlElement = document.CreateElement(name);
            xmlElement.InnerText = value.ToString();
            return xmlElement;
        }

        //
        // ENUM
        //
        public static string GetDescription(this Enum enumVal) {
            return enumVal.GetAttributeOfType<DescriptionAttribute>()?.Description;
        }
        public static string GetTranslation(this Enum enumVal) {
            return Localization.GetDisplayName(enumVal);
        }
        public static Type GetType(this Enum enumVal) {
            return enumVal.GetAttributeOfType<TypeAttribute>()?.Type;
        }
        public static T GetAttributeOfType<T>(this Enum enumVal) where T : Attribute {
            Type type = enumVal.GetType();
            object[] attributes = type.GetMember(Enum.GetName(type, enumVal))[0].GetCustomAttributes(typeof(T), false);
            return (attributes.Length > 0) ? (T)attributes[0] : null;
        }
        public static TTarget ConvertTo<TTarget>(this Enum source) where TTarget : struct, Enum
        {
            if (Enum.TryParse<TTarget>(source.ToString(), ignoreCase: true, out var result))
                return result;

            throw new ArgumentException($"No matching value in {typeof(TTarget).Name} for '{source}'.");
        }


        //
        // ASSEMBLY
        //
        public static string[] ReadAllLinesFromResource(this Assembly assembly, string resourceName)
        {
            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                    throw new FileNotFoundException($"Embedded resource '{resourceName}' not found in assembly '{assembly.FullName}'.");

                using (var reader = new StreamReader(stream))
                {
                    var lines = new List<string>();
                    while (!reader.EndOfStream)
                        lines.Add(reader.ReadLine() ?? string.Empty);

                    return lines.ToArray();
                }                    
            }                
        }
        public static string FullComponentName(this Assembly asm) {
            StringBuilder sb = new StringBuilder();

            var componentNameAttribute = asm.GetCustomAttributes(typeof(ComponentNameAttribute), false);
            if(componentNameAttribute.Length == 0) {
                string name = asm.GetName().Name.Substring(10);
                sb.Append(name[0]);
                for(int i = 1; i < name.Length; i++) {
                    if(Char.IsUpper(name[i]) && name[i - 1] != ' ') {
                        sb.Append(' ');
                    }
                    sb.Append(name[i]);
                }
            } else {
                sb.Append(((ComponentNameAttribute)componentNameAttribute[0]).Value);
            }

            sb.Append(" Autosplitter v").Append(asm.GetName().Version.ToString(3));
            return sb.ToString();
        }
        public static string GitMainURL(this Assembly asm) => Path.Combine("https://raw.githubusercontent.com/Voxelse", asm.GetName().Name, "main/");
        public static string ResourcesURL(this Assembly asm) => Path.Combine(asm.GitMainURL(), "Resources");
        public static string ResourcesPath(this Assembly asm) => Path.Combine(Path.GetDirectoryName(asm.Location), asm.GetName().Name);
        public static string Description(this Assembly asm) => ((AssemblyDescriptionAttribute)Attribute.GetCustomAttribute(asm, typeof(AssemblyDescriptionAttribute))).Description;

        //
        // Dictionary<InventoryItem, int>
        //
        public static int GetCount(this Dictionary<InventoryItem, int> dict, InventoryItem type) => dict.TryGetValue(type, out var count) ? count : 0;
    }
}
