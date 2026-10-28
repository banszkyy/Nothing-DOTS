using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using SaintsField.Playa;
using UnityEngine;
using UnityEngine.UIElements;

class UIDocumentSchemaGenerator : MonoBehaviour
{
    [Button]
    public void Generate()
    {
        string input = Path.Combine(Application.dataPath, "UI");
        string output = Path.Combine(Application.dataPath, "UI", "Models");

        List<string> generated = new();

        foreach (string xmlFile in Directory.EnumerateFileSystemEntries(input, "*.uxml", SearchOption.TopDirectoryOnly))
        {
            XmlDocument doc = new();
            doc.Load(xmlFile);

            Dictionary<string, (string ElementType, string PropertyName, XmlElement Element, string? ParentPropertyName)> namedElements = new();

            bool IsPropertyNameTaken(string name) => name == "Root" || namedElements.Values.Any(v => v.PropertyName == name);

            string MakePropertyNameUnique(string name)
            {
                if (IsPropertyNameTaken(name))
                {
                    int i = 0;
                    while (IsPropertyNameTaken(name + i.ToString()))
                    {
                        i++;
                    }
                    name += i.ToString();
                }
                return name;
            }

            Queue<XmlElement> elements = new();
            elements.Enqueue(doc.DocumentElement);
            while (elements.TryDequeue(out XmlElement? e))
            {
                foreach (XmlElement item in e.ChildNodes.OfType<XmlElement>())
                {
                    elements.Enqueue(item);
                }

                if (!e.HasAttribute("name")) continue;
                if (e.Name == "ui:Template") continue;

                string name = e.GetAttribute("name");
                StringBuilder propertyNameBuilder = new();
                bool isBeginningWord = true;
                foreach (char c in name)
                {
                    if (char.IsLetter(c))
                    {
                        propertyNameBuilder.Append(isBeginningWord ? char.ToUpperInvariant(c) : c);
                        isBeginningWord = false;
                    }
                    else if (char.IsNumber(c))
                    {
                        propertyNameBuilder.Append(c);
                    }
                    else
                    {
                        isBeginningWord = true;
                    }
                }

                if (propertyNameBuilder.Length == 0) { propertyNameBuilder.Append('E'); }
                if (char.IsNumber(propertyNameBuilder[0])) propertyNameBuilder.Insert(0, 'E');

                string propertyName = MakePropertyNameUnique(propertyNameBuilder.ToString());

                string propertyType = e.Name switch
                {
                    "ui:Button" => nameof(Button),
                    "ui:Label" => nameof(Label),
                    "ui:ScrollView" => nameof(ScrollView),
                    "ui:TextField" => nameof(TextField),
                    "ui:ProgressBar" => nameof(ProgressBar),
                    "ui:TabView" => nameof(TabView),
                    "ui:Foldout" => nameof(Foldout),
                    "ui:Toggle" => nameof(Toggle),
                    "ui:Image" => nameof(Image),
                    "ui:GroupBox" => nameof(GroupBox),
                    "ui:VisualElement" => nameof(VisualElement),
                    "ui:Instance" => nameof(VisualElement),
                    _ => ((Func<string>)(() =>
                    {
                        Debug.LogWarning($"Unknown element name {e.Name}");
                        return nameof(VisualElement);
                    }))(),
                };

                namedElements.TryAdd(name, (propertyType, propertyName, e, null));
            }

            string className = $"{Path.GetFileNameWithoutExtension(xmlFile)}Schema";

            string outputFilename = Path.Combine(output, className + ".cs");
            generated.Add(outputFilename);

            Directory.CreateDirectory(Path.GetDirectoryName(outputFilename));
            using FileStream f = File.Open(outputFilename, FileMode.Create, FileAccess.Write);
            using StreamWriter writer = new(f);
            writer.WriteLine("using System.Diagnostics.CodeAnalysis;");
            writer.WriteLine("using UnityEngine.UIElements;");
            writer.WriteLine();
            writer.WriteLine($"public class {className} : IDocumentSchema");
            writer.WriteLine("{");
            writer.WriteLine($"    [NotNull] public VisualElement? Root {{ get; private set; }}");
            foreach ((string name, (string elementType, string propertyName, XmlElement e, _)) in namedElements.OrderBy(v => v.Key))
            {
                writer.WriteLine($"    /// <summary><code>&lt;{e.Name} name=\"{name}\"&gt;</code></summary>");
                writer.WriteLine($"    [NotNull] public {elementType}? {propertyName} {{ get; private set; }}");
            }
            writer.WriteLine();
            writer.WriteLine($"    public {className}()");
            writer.WriteLine("    {");
            writer.WriteLine("    }");
            writer.WriteLine();
            writer.WriteLine($"    public {className}(VisualElement root)");
            writer.WriteLine("    {");
            writer.WriteLine("        Q(root);");
            writer.WriteLine("    }");
            writer.WriteLine();
            writer.WriteLine("    public void Q(VisualElement root)");
            writer.WriteLine("    {");
            writer.WriteLine($"        Root = root;");
            foreach ((string name, (string elementType, string propertyName, XmlElement e, _)) in namedElements.OrderBy(v => v.Key))
            {
                writer.WriteLine($"        {propertyName} = root.Q<{elementType}>(\"{name}\");");
            }
            writer.WriteLine();
            foreach ((string name, (string elementType, string propertyName, XmlElement e, _)) in namedElements.OrderBy(v => v.Key))
            {
                writer.WriteLine($"        if ({propertyName} is null) Debug.LogError(\"Element \\\"{name}\\\" was not found in document {Path.GetRelativePath(Application.dataPath, xmlFile)}\");");
            }
            writer.WriteLine("    }");
            writer.WriteLine("}");
            writer.WriteLine();
        }

        foreach (string csFile in Directory.EnumerateFileSystemEntries(output, "*.cs", SearchOption.TopDirectoryOnly))
        {
            if (generated.Contains(csFile)) continue;
            File.Delete(csFile);
            if (File.Exists(csFile + ".meta")) File.Delete(csFile + ".meta");
        }
    }
}

static class UISchemaExtensions
{
    public static TSchema AddNew<TSchema>(this VisualElement target, VisualTreeAsset visualTreeAsset) where TSchema : IDocumentSchema
    {
        VisualElement element = visualTreeAsset.Instantiate();
        target.Add(element);
        TSchema res = Activator.CreateInstance<TSchema>();
        res.Q(element);
        return res;
    }
}
