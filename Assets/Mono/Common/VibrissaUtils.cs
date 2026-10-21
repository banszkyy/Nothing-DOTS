using System.IO;
using System.Text;
using LanguageCore.Runtime;
using UnityEngine;

class VibrissaUtils : MonoBehaviour
{
    [SaintsField.Playa.Button("Generate Config")]
    public void GenerateConfig()
    {
        StringBuilder result = new();
        foreach (IExternalFunction externalFunction in ProcessorAPI.GenerateManagedExternalFunctions())
        {
            result.AppendLine($"externalfunc={externalFunction.Name} {externalFunction.ReturnValueSize} {externalFunction.ParametersSize}");
        }
        File.WriteAllText(Path.Combine(Application.streamingAssetsPath, "lib", "vi.conf"), result.ToString());
    }
}
