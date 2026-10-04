using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using LanguageCore.Runtime;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

struct ProcessorInitialization : IComponentData
{
    public FileId SourceFile;
}
