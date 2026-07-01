
namespace LanSpire.Helpers;

public static class PatchHelper
{
    private const BindingFlags AllFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    public static MethodInfo? Method(Type type, string name) =>
        type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

    /// <summary>
    /// Gets a private/public instance or static field by name. Returns null
    /// and logs if the field is missing - callers must null-check before use.
    /// </summary>
    public static FieldInfo? Field(Type type, string name)
    {
        var field = type.GetField(name, AllFlags);
        if (field == null)
            Log($"FIELD NOT FOUND: {type.FullName}.{name}");
        return field;
    }

    /// <summary>True if the field exists on the type (any binding flags).</summary>
    public static bool HasField(Type type, string name) =>
        type.GetField(name, AllFlags) != null;

    /// <summary>True if the method exists and is declared on this type (not inherited).</summary>
    public static bool HasMethod(Type type, string name) =>
        type.GetMethod(name, AllFlags)?.DeclaringType == type;

    /// <summary>
    /// Patches a constructor. Harmony needs AccessTools.Constructor, not
    /// GetMethod(".ctor"). Returns true if patched, false if skipped.
    /// </summary>
    public static bool PatchConstructor(Harmony harmony, Type targetType,
        MethodInfo? prefix = null, MethodInfo? postfix = null)
    {
        try
        {
            var ctor = AccessTools.Constructor(targetType);
            if (ctor == null)
            {
                Log($"SKIPPED {targetType.FullName} .ctor: constructor not found");
                return false;
            }
            harmony.Patch(ctor,
                prefix: prefix == null ? null : new HarmonyMethod(prefix),
                postfix: postfix == null ? null : new HarmonyMethod(postfix));
            Log($"Patched {targetType.FullName} .ctor");
            return true;
        }
        catch (Exception exception)
        {
            Log($"FAILED {targetType.FullName} .ctor: {exception}");
            return false;
        }
    }

    public static void Patch(Harmony harmony, Type targetType, string methodName,
        MethodInfo? prefix = null, MethodInfo? postfix = null, MethodInfo? transpiler = null,
        BindingFlags flags = AllFlags)
    {
        try
        {
            var target = targetType.GetMethod(methodName, flags);
            if (target == null)
            {
                Log($"SKIPPED {targetType.FullName}.{methodName}: method not found");
                return;
            }
            if (!IsImplementedOnTargetType(targetType, target))
            {
                Log($"SKIPPED {targetType.FullName}.{methodName}: inherited extern/base method is not safe to patch");
                return;
            }
            harmony.Patch(
                target,
                prefix: prefix == null ? null : new HarmonyMethod(prefix),
                postfix: postfix == null ? null : new HarmonyMethod(postfix),
                transpiler: transpiler == null ? null : new HarmonyMethod(transpiler));
            Log($"Patched {targetType.FullName}.{methodName}");
        }
        catch (Exception exception)
        {
            Log($"FAILED {targetType.FullName}.{methodName}: {exception}");
        }
    }

    public static void PatchGetter(Harmony harmony, Type targetType, string propertyName, MethodInfo prefix)
    {
        try
        {
            var property = targetType.GetProperty(propertyName, AllFlags);
            var getter = property?.GetGetMethod(true);
            if (getter == null)
            {
                Log($"FAILED {targetType.FullName}.{propertyName} getter: not found");
                return;
            }
            harmony.Patch(getter, prefix == null ? null : new HarmonyMethod(prefix));
            Log($"Patched {targetType.FullName}.{propertyName} getter");
        }
        catch (Exception exception)
        {
            Log($"FAILED {targetType.FullName}.{propertyName} getter: {exception}");
        }
    }

    private static bool IsImplementedOnTargetType(Type targetType, MethodInfo method)
    {
        if (method.DeclaringType != targetType)
            return false;
        if ((method.Attributes & MethodAttributes.PinvokeImpl) != 0)
            return false;
        if ((method.GetMethodImplementationFlags() & MethodImplAttributes.InternalCall) != 0)
            return false;
        return true;
    }

    public static void Log(string message)
    {
        GD.Print($"[LanSpire] {message}");
    }
}