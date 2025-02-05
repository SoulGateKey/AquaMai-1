// 一些线索：
// AdjustWndProcFlagsFromMetadata
// DebuggableAttribute

namespace MelonLoader;

[AttributeUsage(AttributeTargets.Assembly)]
public class MelonInfoAttribute : Attribute
{
    public MelonInfoAttribute(Type type, string name, string version, string author, string downloadLink = null)
    {
    }

    public MelonInfoAttribute(Type type, string name, int versionMajor, int versionMinor, int versionRevision, string versionIdentifier, string author, string downloadLink = null)
        : this(type, name, $"{versionMajor}.{versionMinor}.{versionRevision}{(string.IsNullOrEmpty(versionIdentifier) ? "" : versionIdentifier)}", author, downloadLink)
    {
    }

    public MelonInfoAttribute(Type type, string name, int versionMajor, int versionMinor, int versionRevision, string author, string downloadLink = null)
        : this(type, name, versionMajor, versionMinor, versionRevision, null, author, downloadLink)
    {
    }
}

[AttributeUsage(AttributeTargets.Assembly)]
public class MelonColorAttribute : Attribute
{
    public MelonColorAttribute()
    {
    }

    public MelonColorAttribute(ConsoleColor color)
    {
    }

    public MelonColorAttribute(int alpha, int red, int green, int blue)
    {
    }
}

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public class MelonGameAttribute : Attribute
{
    public MelonGameAttribute(string developer = null, string name = null)
    {
    }
}

[AttributeUsage(AttributeTargets.Assembly)]
public class HarmonyDontPatchAllAttribute : Attribute
{
    public HarmonyDontPatchAllAttribute()
    {
    }
}

public class MelonMod
{
}